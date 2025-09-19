using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using BotLib.Generated;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using NQ;
using NQutils.Def;
using StackExchange.Redis;

public class InventoryService : IInventoryService, IMetricsProvider
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<IInventoryService> _logger;
    private readonly IMetricsService _metricsService;
    private readonly IGameplayBank _gameplayBank;

    public InventoryService(IDatabase redisDatabase, ILogger<IInventoryService> logger, IMetricsService metricsService, IGameplayBank gameplayBank)
    {
        _redisDatabase = redisDatabase;
        _logger = logger;
        _metricsService = metricsService;
        _gameplayBank = gameplayBank;
    }

    #region IMetricsProvider Implementation

    public string ProviderName => "inventory";

    public IEnumerable<string> SimpleMetrics => new[]
    {
        "inventory_items_total",
        "inventory_unique_items_per_market",
        "inventory_item_quantity",
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
            
            // Calculate unique items per market (markets 1-5)
            var totalMarketItems = 0;
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var marketItems = await GetAll(marketId);
                var count = marketItems.Count;
                metricsService.SetGauge("inventory_unique_items_per_market", new[] { marketId.ToString() }, count);
                
                // Also set individual item quantities for each item in this market (converted to display quantities)
                foreach (var item in marketItems)
                {
                    if (long.TryParse(item.Value, out long rawQuantity))
                    {
                        var displayQuantity = ConvertRawToDisplay(item.Key, rawQuantity);
                        metricsService.SetGauge("inventory_item_quantity", new[] { marketId.ToString(), item.Key.ToString() }, displayQuantity);
                    }
                }
                
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
                var newMarketQuantity = currentMarketQuantity + value;
                await _redisDatabase.HashSetAsync(marketKey, itemId.ToString(), newMarketQuantity);
                
                // Update metrics for market-specific items (convert to display quantities)
                _metricsService.Increment("inventory_operations_total");
                var displayQuantity = ConvertRawToDisplay(itemId, newMarketQuantity);
                _metricsService.SetGauge("inventory_item_quantity", new[] { marketId.ToString(), itemId.ToString() }, displayQuantity);
                
                // If this is a new item in the market, update unique items count
                if (currentMarketQuantity == 0)
                {
                    var marketItems = await GetAll(marketId);
                    _metricsService.SetGauge("inventory_unique_items_per_market", new[] { marketId.ToString() }, marketItems.Count);
                }
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
                    
                    // Update market metrics (convert to display quantities)
                    var displayQuantity = ConvertRawToDisplay(itemId, newMarketQuantity);
                    _metricsService.SetGauge("inventory_item_quantity", new[] { marketId.ToString(), itemId.ToString() }, displayQuantity);
                    
                    // If item is completely removed from market, update unique items count
                    if (newMarketQuantity == 0)
                    {
                        var marketItems = await GetAll(marketId);
                        _metricsService.SetGauge("inventory_unique_items_per_market", new[] { marketId.ToString() }, marketItems.Count);
                    }
                    
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
        else if (marketId.HasValue && currentQuantity > 0)
        {
            // Set specific item quantity to 0
            _metricsService.SetGauge("inventory_item_quantity", new[] { marketId.ToString(), itemId.ToString() }, 0);
            
            // Update unique items count for the market
            var marketItems = await GetAll(marketId);
            _metricsService.SetGauge("inventory_unique_items_per_market", new[] { marketId.ToString() }, marketItems.Count);
        }
    }

    // Function to clean the inventory; marketId is optional.
    public async Task CleanInventory(ulong? marketId = null)
    {
        // Get current items before deletion for metrics cleanup
        var currentItems = await GetAll(marketId);
        
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
            // Reset unique items count for the market
            _metricsService.SetGauge("inventory_unique_items_per_market", new[] { marketId.ToString() }, 0);
            
            // Set all individual item quantities to 0
            foreach (var item in currentItems)
            {
                _metricsService.SetGauge("inventory_item_quantity", new[] { marketId.ToString(), item.Key.ToString() }, 0);
            }
        }
    }

    #region Raw Quantity Methods

    /// <summary>
    /// Adds/updates raw fixed-point quantity directly. Raw quantities are stored in game's native format.
    /// For materials, this means the volume is stored as fixed-point integer (volume * 2^24).
    /// For non-materials, raw quantity equals the item count.
    /// </summary>
    public async Task AddUpdateRaw(ulong itemId, long rawValue, ulong? marketId = null)
    {
        // Use existing AddUpdate method - it works with raw quantities
        await AddUpdate(itemId, rawValue, marketId);
    }

    /// <summary>
    /// Gets the raw fixed-point quantity as stored in the database.
    /// For materials, this is the volume stored as fixed-point integer (volume * 2^24).
    /// For non-materials, raw quantity equals the item count.
    /// </summary>
    public async Task<long> GetRaw(ulong itemId, ulong? marketId = null)
    {
        // Use existing Get method - it returns raw quantities
        return await Get(itemId, marketId);
    }

    /// <summary>
    /// Converts display volume/count to raw fixed-point quantity using GameplayBank, then stores it.
    /// This is the method to use when you have human-readable quantities from configuration or user input.
    /// For materials: converts volume to fixed-point representation (volume * 2^24).
    /// For non-materials: stores count directly.
    /// </summary>
    public async Task AddUpdateFromDisplay(ulong itemId, long displayQuantity, ulong? marketId = null)
    {
        // Convert display to raw using GameplayBank
        var rawQuantity = _gameplayBank.QuantityFromGDValue(itemId, displayQuantity);
        await AddUpdateRaw(itemId, rawQuantity, marketId);
    }

    /// <summary>
    /// Gets the display volume/count (human-readable) by converting from raw fixed-point quantity.
    /// For materials: converts from fixed-point to volume (raw / 2^24).
    /// For non-materials: returns count directly.
    /// Use this for logging, metrics, and UI display.
    /// </summary>
    public async Task<long> GetDisplay(ulong itemId, ulong? marketId = null)
    {
        var rawQuantity = await GetRaw(itemId, marketId);
        return ConvertRawToDisplay(itemId, rawQuantity);
    }

    /// <summary>
    /// Converts raw fixed-point quantity to display volume/count.
    /// 
    /// Materials use fixed-point arithmetic where:
    /// - Raw quantity = Volume * 2^24 (16777216)
    /// - This allows storing fractional volumes (e.g., 1.5 cubic meters) as integers
    /// - Non-materials store count directly (no conversion needed)
    /// 
    /// Uses Backend.QuantityConstants.QuantityToVolumeCoeff for the conversion factor.
    /// </summary>
    private long ConvertRawToDisplay(ulong itemId, long rawQuantity)
    {
        // Check if it's a material using GameplayBank
        if (_gameplayBank.GetDefinition(itemId).BaseObject is Material)
        {
            return rawQuantity / (long)QuantityConstants.QuantityToVolumeCoeff;
        }
        return rawQuantity;
    }

    #endregion

    #region Local Resource Tracking Methods

    /// <summary>
    /// Adds/updates locally generated resources using display quantities.
    /// Tracks separately from imported/crafted resources for capacity management.
    /// </summary>
    /// <param name="itemId">Resource identifier</param>
    /// <param name="displayQuantity">Quantity in display units (human-readable)</param>
    /// <param name="marketId">Market identifier</param>
    public async Task AddUpdateFromDisplayLocal(ulong itemId, long displayQuantity, ulong? marketId = null)
    {
        // Convert display to raw using GameplayBank
        var rawQuantity = _gameplayBank.QuantityFromGDValue(itemId, displayQuantity);
        await AddUpdateLocalRaw(itemId, rawQuantity, marketId);
    }

    /// <summary>
    /// Gets locally generated resource quantity in display units (human-readable).
    /// </summary>
    /// <param name="itemId">Resource identifier</param>
    /// <param name="marketId">Market identifier</param>
    /// <returns>Quantity in display units</returns>
    public async Task<long> GetLocalResourceDisplay(ulong itemId, ulong? marketId = null)
    {
        var rawQuantity = await GetLocalResourceRaw(itemId, marketId);
        return ConvertRawToDisplay(itemId, rawQuantity);
    }

    /// <summary>
    /// Adds/updates raw locally generated resource quantities.
    /// Uses separate Redis keys to track local vs imported resources.
    /// </summary>
    /// <param name="itemId">Resource identifier</param>
    /// <param name="rawValue">Raw quantity value</param>
    /// <param name="marketId">Market identifier</param>
    private async Task AddUpdateLocalRaw(ulong itemId, long rawValue, ulong? marketId = null)
    {
        var localKey = marketId.HasValue ? $"inventory:local:{marketId}:items" : "inventory:local:global:items";
        
        // Check current local quantity
        long currentLocalQuantity = await GetLocalResourceRaw(itemId, marketId);
        
        if (rawValue > 0)
        {
            // Increase local quantity
            var newQuantity = currentLocalQuantity + rawValue;
            await _redisDatabase.HashSetAsync(localKey, itemId.ToString(), newQuantity);
            
            _logger.LogTrace("Added {RawValue} local resources for item {ItemId} in market {MarketId} (new total: {NewQuantity})", 
                rawValue, itemId, marketId?.ToString() ?? "global", newQuantity);
        }
        else if (rawValue < 0)
        {
            // Decrease local quantity
            long quantityToReduce = Math.Abs(rawValue);
            long actualReduction = Math.Min(currentLocalQuantity, quantityToReduce);
            var newQuantity = currentLocalQuantity - actualReduction;
            
            if (newQuantity > 0)
            {
                await _redisDatabase.HashSetAsync(localKey, itemId.ToString(), newQuantity);
            }
            else
            {
                await _redisDatabase.HashDeleteAsync(localKey, itemId.ToString());
            }
            
            if (actualReduction < quantityToReduce)
            {
                _logger.LogWarning("Could only reduce {ActualReduction} local resources for item {ItemId}, requested {Requested}", 
                    actualReduction, itemId, quantityToReduce);
            }
        }
        
        // Update metrics
        _metricsService.Increment("inventory_operations_total");
    }

    /// <summary>
    /// Gets raw locally generated resource quantity.
    /// </summary>
    /// <param name="itemId">Resource identifier</param>
    /// <param name="marketId">Market identifier</param>
    /// <returns>Raw quantity value</returns>
    private async Task<long> GetLocalResourceRaw(ulong itemId, ulong? marketId = null)
    {
        var localKey = marketId.HasValue ? $"inventory:local:{marketId}:items" : "inventory:local:global:items";
        var value = await _redisDatabase.HashGetAsync(localKey, itemId.ToString());
        
        if (value.HasValue && value.TryParse(out long quantity))
        {
            return quantity;
        }
        return 0;
    }

    /// <summary>
    /// Gets all locally generated resources for a market.
    /// </summary>
    /// <param name="marketId">Market identifier (null for global)</param>
    /// <returns>Dictionary of resource ID to raw quantity</returns>
    public async Task<Dictionary<ulong, long>> GetAllLocalResources(ulong? marketId = null)
    {
        var localKey = marketId.HasValue ? $"inventory:local:{marketId}:items" : "inventory:local:global:items";
        var hashEntries = await _redisDatabase.HashGetAllAsync(localKey);
        
        return hashEntries.ToDictionary(
            entry => (ulong)entry.Name,
            entry => (long)entry.Value
        );
    }

    /// <summary>
    /// Cleans all local resource data for a market.
    /// </summary>
    /// <param name="marketId">Market identifier (null for global)</param>
    public async Task CleanLocalResources(ulong? marketId = null)
    {
        var localKey = marketId.HasValue ? $"inventory:local:{marketId}:items" : "inventory:local:global:items";
        await _redisDatabase.KeyDeleteAsync(localKey);
        
        _logger.LogInformation("Cleaned local resource data for market {MarketId}", marketId?.ToString() ?? "global");
    }

    #endregion

    #region Data Cleanup Methods

    /// <summary>
    /// Cleans all existing inventory data from Redis.
    /// USE WITH CAUTION: This will delete all inventory data.
    /// Recommended to use before switching out of dry-run mode to prevent format conflicts.
    /// </summary>
    public async Task CleanAllInventoryData()
    {
        _logger.LogWarning("CLEANING ALL INVENTORY DATA - THIS WILL DELETE ALL STORED INVENTORY INCLUDING LOCAL RESOURCE TRACKING");
        
        // Clean global inventory using existing method
        await CleanInventory(null);
        await CleanLocalResources(null);
        
        // Clean market-specific inventories (markets 1-5)
        for (ulong marketId = 1; marketId <= 5; marketId++)
        {
            await CleanInventory(marketId);
            await CleanLocalResources(marketId);
        }
        
        _logger.LogInformation("All inventory data (including local resource tracking) has been cleaned");
    }

    /// <summary>
    /// Safely cleans inventory data with backup to a timestamped key.
    /// Creates backup keys that expire after 7 days.
    /// </summary>
    public async Task SafeCleanInventoryWithBackup()
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _logger.LogInformation("Creating inventory backup with timestamp {Timestamp} before cleanup...", timestamp);
        
        try
        {
            // Backup global inventory and local resources
            await BackupInventoryKey("inventory:global:items", $"backup:{timestamp}:inventory:global:items");
            await BackupInventoryKey("inventory:local:global:items", $"backup:{timestamp}:inventory:local:global:items");
            
            // Backup market inventories and local resources
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                await BackupInventoryKey($"inventory:{marketId}:items", $"backup:{timestamp}:inventory:{marketId}:items");
                await BackupInventoryKey($"inventory:local:{marketId}:items", $"backup:{timestamp}:inventory:local:{marketId}:items");
            }
            
            _logger.LogInformation("Backup created successfully. Proceeding with cleanup...");
            
            // Now clean the data
            await CleanAllInventoryData();
            
            _logger.LogInformation("Inventory cleanup completed. Backup available with timestamp {Timestamp} (expires in 7 days)", timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to safely clean inventory data with backup");
            throw;
        }
    }

    /// <summary>
    /// Helper method to backup a single inventory key.
    /// </summary>
    private async Task BackupInventoryKey(string sourceKey, string backupKey)
    {
        bool exists = await _redisDatabase.KeyExistsAsync(sourceKey);
        if (exists)
        {
            var hashEntries = await _redisDatabase.HashGetAllAsync(sourceKey);
            if (hashEntries.Length > 0)
            {
                await _redisDatabase.HashSetAsync(backupKey, hashEntries);
                await _redisDatabase.KeyExpireAsync(backupKey, TimeSpan.FromDays(7));
                _logger.LogDebug("Backed up {Count} items from {SourceKey} to {BackupKey}", 
                    hashEntries.Length, sourceKey, backupKey);
            }
        }
    }

    #endregion
}
