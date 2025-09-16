using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public class InventoryService : IInventoryService, IMetricsProvider
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<IInventoryService> _logger;
    private readonly IMetricsService _metricsService;

    public InventoryService(IDatabase redisDatabase, ILogger<IInventoryService> logger, IMetricsService metricsService)
    {
        _redisDatabase = redisDatabase;
        _logger = logger;
        _metricsService = metricsService;
    }

    #region IMetricsProvider Implementation

    public string ProviderName => "inventory";

    public IEnumerable<string> SimpleMetrics => new[]
    {
        "inventory_items_total",
        "inventory_items_per_market",
        "inventory_operations_total"
    };

    public IEnumerable<string> ComplexMetrics => new string[0]; // No complex metrics until we have real data

    public async Task InitializeMetrics(IMetricsService metricsService)
    {
        try
        {
            // Calculate total global items
            var globalItems = await GetAll(null);
            metricsService.SetGauge("inventory_items_total", globalItems.Count);
            
            // Calculate items per market (markets 1-5)
            var totalMarketItems = 0;
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var marketItems = await GetAll(marketId);
                var count = marketItems.Count;
                metricsService.SetGauge("inventory_items_per_market", new[] { marketId.ToString() }, count);
                totalMarketItems += count;
            }

            _logger.LogInformation("Initialized inventory metrics: {GlobalItems} global, {MarketItems} market items", 
                globalItems.Count, totalMarketItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize inventory metrics");
        }
    }

    public async Task CalculateComplexMetrics(IMetricsService metricsService)
    {
        // No complex metrics implemented yet - waiting for real business requirements
        // Complex metrics will be added when we have:
        // - Price service integration for inventory value calculations
        // - Historical data for turnover rate analysis
        // - Business logic for meaningful derived metrics
        
        await Task.CompletedTask; // Placeholder to satisfy interface
        _logger.LogDebug("No complex inventory metrics to calculate yet");
    }

    // Complex metric calculation methods removed
    // Will be implemented when we have:
    // - IPriceService integration for real item values
    // - Historical inventory data for turnover calculations
    // - Business requirements for specific derived metrics

    #endregion

    // Function to get all items; marketId is optional.
    public async Task<Dictionary<ulong, string>> GetAll(ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        var hashEntries = await _redisDatabase.HashGetAllAsync(key);

        return hashEntries.ToDictionary(
            entry => (ulong)entry.Name, // Converting RedisValue to ulong directly
            entry => entry.Value.ToString() // Converting RedisValue to string for the value
        );
    }

    // Function to get item quantity; marketId is optional.
    public async Task<long> Get(ulong itemId, ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        var value = await _redisDatabase.HashGetAsync(key, itemId.ToString());

        if (value.HasValue && value.TryParse(out long quantity))
        {
            return quantity;
        }
        return 0;
    }

    // Function to add/update an item; marketId is optional.
    public async Task AddUpdate(ulong itemId, long value, ulong? marketId = null)
    {
        var marketKey = marketId.HasValue ? $"inventory:{marketId}:items" : null;
        var globalKey = "inventory:global:items";

        // Check if the item already exists in the market or globally
        long currentMarketQuantity = marketId.HasValue ? await Get(itemId, marketId) : 0;
        long currentGlobalQuantity = await Get(itemId, null); // Get global inventory

        // Case 1: Increase quantity (either new item or update existing)
        if (value > 0)
        {
            if (marketId.HasValue)
            {
                await _redisDatabase.HashSetAsync(marketKey, itemId.ToString(), currentMarketQuantity + value);
                // Update metrics for market-specific items
                _metricsService.Increment("inventory_operations_total");
                _metricsService.SetGauge("inventory_items_per_market", new[] { marketId.ToString() }, currentMarketQuantity + value);
            }
            else
            {
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), currentGlobalQuantity + value);
                // Update metrics for global items
                _metricsService.Increment("inventory_operations_total");
                if (currentGlobalQuantity == 0) // New item added
                {
                    _metricsService.Increment("inventory_items_total");
                }
            }
        }
        // Case 2: Reduce quantity (split between global and market if necessary)
        else if (value < 0)
        {
            long quantityToReduce = Math.Abs(value);

            // Step 1: Reduce from global inventory first
            long remainingReduction = quantityToReduce;

            if (currentGlobalQuantity >= remainingReduction)
            {
                // If global inventory can cover the reduction, just reduce it
                var newQuantity = currentGlobalQuantity - remainingReduction;
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), newQuantity);
                
                // Update metrics
                _metricsService.Increment("inventory_operations_total");
                if (newQuantity == 0) // Item completely removed
                {
                    _metricsService.Decrement("inventory_items_total");
                }
            }
            else
            {
                // If global inventory cannot fully cover the reduction, reduce it to zero and reduce the market
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), 0);
                _metricsService.Increment("inventory_operations_total");
                if (currentGlobalQuantity > 0) // Item removed from global
                {
                    _metricsService.Decrement("inventory_items_total");
                }
                remainingReduction -= currentGlobalQuantity;

                // Step 2: Reduce remaining quantity from market inventory (if applicable)
                if (marketId.HasValue && currentMarketQuantity > 0)
                {
                    long marketReduction = Math.Min(currentMarketQuantity, remainingReduction);
                    var newMarketQuantity = currentMarketQuantity - marketReduction;
                    await _redisDatabase.HashSetAsync(marketKey, itemId.ToString(), newMarketQuantity);
                    
                    // Update market metrics
                    _metricsService.SetGauge("inventory_items_per_market", new[] { marketId.ToString() }, newMarketQuantity);
                    remainingReduction -= marketReduction;
                }

                // Step 3: If any reduction remains, inventory deficit exists (could handle as an error or log)
                if (remainingReduction > 0)
                {
                    _logger.LogWarning($"Insufficient inventory for item {itemId}. Unable to reduce by {quantityToReduce} units.");
                }
            }
        }
    }

    // Function to delete an item; marketId is optional.
    public async Task Delete(ulong itemId, ulong? marketId = null)
    {
        // Check current quantity before deletion for metrics
        var currentQuantity = await Get(itemId, marketId);
        
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        await _redisDatabase.HashDeleteAsync(key, itemId.ToString());
        
        // Update metrics
        _metricsService.Increment("inventory_operations_total");
        if (!marketId.HasValue && currentQuantity > 0)
        {
            _metricsService.Decrement("inventory_items_total");
        }
        else if (marketId.HasValue)
        {
            _metricsService.SetGauge("inventory_items_per_market", new[] { marketId.ToString() }, 0);
        }
    }

    // Function to clean the inventory; marketId is optional.
    public async Task CleanInventory(ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        await _redisDatabase.KeyDeleteAsync(key);
        
        // Reset metrics
        _metricsService.Increment("inventory_operations_total");
        if (!marketId.HasValue)
        {
            _metricsService.SetGauge("inventory_items_total", 0);
        }
        else
        {
            _metricsService.SetGauge("inventory_items_per_market", new[] { marketId.ToString() }, 0);
        }
    }
}
