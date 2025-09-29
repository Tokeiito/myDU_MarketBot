using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using BotLib.Generated;
using MarketBot.Domain;
using MarketBot.Interfaces;
using MarketBot.Services;
using Microsoft.Extensions.Logging;
using NQ;
using NQutils.Def;
using StackExchange.Redis;

namespace MarketBot.Services
{
    public class WarehouseService : IWarehouseService, IMetricsProvider
{
    private readonly IWarehouseLotStorage _lotStorage;
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<IWarehouseService> _logger;
    private readonly IMetricsService _metricsService;
    private readonly IGameplayBank _gameplayBank;
    private readonly IConfigService _configService;
    private readonly IWarehouseEventService _eventService;
    
    // In-memory tracking for dry run mode
    private readonly Dictionary<string, Dictionary<ulong, long>> _dryRunInventory = new();
    private readonly Dictionary<string, List<WarehouseLot>> _dryRunLots = new();
    private readonly object _dryRunLock = new object();

    public WarehouseService(
        IWarehouseLotStorage lotStorage,
        IDatabase redisDatabase,
        ILogger<IWarehouseService> logger,
        IMetricsService metricsService,
        IGameplayBank gameplayBank,
        IConfigService configService,
        IWarehouseEventService eventService)
    {
        _lotStorage = lotStorage ?? throw new ArgumentNullException(nameof(lotStorage));
        _redisDatabase = redisDatabase ?? throw new ArgumentNullException(nameof(redisDatabase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _gameplayBank = gameplayBank ?? throw new ArgumentNullException(nameof(gameplayBank));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
    }

    #region IMetricsProvider Implementation

    public string ProviderName => "warehouse";

    public IEnumerable<string> SimpleMetrics => new[]
    {
        "warehouse_items_total",
        "warehouse_unique_items_per_market",
        "warehouse_item_quantity",
        "warehouse_operations_total",
        "warehouse_lots_total",
        "warehouse_lot_operations_total",
        "warehouse_average_lots_per_item",
        "warehouse_lot_merge_frequency",
        "warehouse_consumption_operations_total",
        "warehouse_cost_calculation_time_ms"
    };

    public IEnumerable<string> ComplexMetrics => new[]
    {
        "warehouse_total_value",
        "warehouse_cost_variance",
        "warehouse_consumption_rate",
        "warehouse_merge_operation_ratio"
    };

    public async Task InitializeMetrics(IMetricsService metricsService)
    {
        try
        {
            // Calculate metrics for each market (1-5) and global
            var totalItems = 0;
            var totalLots = 0;
            
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var itemSummaries = await GetAllItemSummaries(marketId);
                var itemCount = itemSummaries.Count;
                var lotCount = itemSummaries.Values.Sum(s => s.LotCount);
                
                metricsService.SetGauge("warehouse_unique_items_per_market", new[] { marketId.ToString() }, itemCount);
                metricsService.SetGauge("warehouse_lots_total", new[] { marketId.ToString() }, lotCount);
                
                foreach (var summary in itemSummaries.Values)
                {
                    var displayQuantity = ConvertRawToDisplay(summary.ItemId, summary.TotalQuantity);
                    metricsService.SetGauge("warehouse_item_quantity", 
                        new[] { marketId.ToString(), summary.ItemId.ToString() }, displayQuantity);
                }
                
                totalItems += itemCount;
                totalLots += lotCount;
            }
            
            // Global metrics
            var globalSummaries = await GetAllItemSummaries(null);
            var globalItemCount = globalSummaries.Count;
            var globalLotCount = globalSummaries.Values.Sum(s => s.LotCount);
            
            metricsService.SetGauge("warehouse_items_total", totalItems + globalItemCount);
            metricsService.SetGauge("warehouse_lots_total", new[] { "global" }, globalLotCount);
            
            var avgLotsPerItem = totalItems > 0 ? (double)totalLots / totalItems : 0;
            metricsService.SetGauge("warehouse_average_lots_per_item", avgLotsPerItem);

            _logger.LogInformation("Initialized warehouse metrics: {TotalItems} items, {TotalLots} lots, {AvgLotsPerItem} avg lots per item", 
                totalItems + globalItemCount, totalLots + globalLotCount, avgLotsPerItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize warehouse metrics");
        }
    }

    public async Task CalculateComplexMetrics(IMetricsService metricsService)
    {
        try
        {
            // Calculate total warehouse value at cost basis
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var totalValue = await GetTotalInventoryValue(marketId);
                var displayValue = ConvertRawToDisplay(0, totalValue); // Assuming cost is in same units
                metricsService.SetGauge("warehouse_total_value", new[] { marketId.ToString() }, displayValue);
                
                // Calculate cost variance for each item in this market
                await CalculateCostVarianceMetrics(marketId, metricsService);
            }
            
            var globalValue = await GetTotalInventoryValue(null);
            var globalDisplayValue = ConvertRawToDisplay(0, globalValue);
            metricsService.SetGauge("warehouse_total_value", new[] { "global" }, globalDisplayValue);
            
            // Calculate global cost variance
            await CalculateCostVarianceMetrics(null, metricsService);
            
            // Calculate consumption rate (items consumed per hour)
            await CalculateConsumptionRateMetrics(metricsService);
            
            // Calculate merge operation ratio
            await CalculateMergeOperationRatio(metricsService);
            
            _logger.LogDebug("Updated complex warehouse metrics: total values, cost variance, consumption rates, and merge ratios calculated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate complex warehouse metrics");
        }
    }

    #endregion

    #region IWarehouseService Implementation

    /// <summary>
    /// Add items to warehouse with specified unit cost.
    /// Creates or finds appropriate lot based on cost and lot management rules.
    /// </summary>
    public async Task<bool> AddItems(ulong itemId, long quantity, long unitCost = 0, ulong? marketId = null)
    {
        try
        {
            if (quantity <= 0)
            {
                _logger.LogWarning("Attempted to add non-positive quantity {Quantity} for item {ItemId}", quantity, itemId);
                return false;
            }
            
            // Handle dry run mode
            if (IsDryRunMode)
            {
                _logger.LogInformation("[DRY-RUN] Adding {Quantity} items {ItemId} at cost {UnitCost} to market {MarketId}", 
                    quantity, itemId, unitCost, marketId?.ToString() ?? "global");
                
                lock (_dryRunLock)
                {
                    // Update dry run inventory tracking
                    var inventory = GetOrCreateDryRunInventory(marketId);
                    inventory[itemId] = inventory.GetValueOrDefault(itemId, 0) + quantity;
                    
                    // Create/update dry run lot
                    var lots = GetOrCreateDryRunLots(itemId, marketId);
                    var existingLot = lots.FirstOrDefault(l => Math.Abs(l.UnitCost - unitCost) < 0.01);
                    
                    if (existingLot != null)
                    {
                        existingLot.Quantity += quantity;
                    }
                    else
                    {
                        lots.Add(new WarehouseLot
                        {
                            LotId = Guid.NewGuid().ToString(),
                            ItemId = itemId,
                            MarketId = marketId,
                            Quantity = quantity,
                            UnitCost = unitCost,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                
                // Log for lot tracking in dry run
                if (_configService.Config.Warehouse.EnableLotTracking)
                {
                    _logger.LogInformation("[DRY-RUN][LOT_TRACKING] Added {Quantity} items (item: {ItemId}, cost: {UnitCost}, market: {MarketId})", 
                        quantity, itemId, unitCost, marketId?.ToString() ?? "global");
                }
                
                return true;
            }

            _logger.LogDebug("Adding {Quantity} items {ItemId} at cost {UnitCost} to market {MarketId}", 
                quantity, itemId, unitCost, marketId?.ToString() ?? "global");

            var targetLot = await FindOrCreateLotAsync(itemId, unitCost, marketId);
            if (targetLot == null)
            {
                _logger.LogError("Failed to find or create lot for item {ItemId}", itemId);
                return false;
            }

            // Add to lot
            targetLot.Quantity += quantity;
            var success = await _lotStorage.StoreLotAsync(targetLot);

            if (success)
            {
                // Update summary and metrics
                await UpdateItemSummaryAsync(itemId, marketId);
                
                // Tag metrics for dry run mode
                var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
                _metricsService.Increment("warehouse_operations_total", metricTags);
                _metricsService.Increment("warehouse_lot_operations_total", metricTags);
                
                // Emit items_added event
                await _eventService.EmitItemsAddedAsync(itemId, quantity, unitCost, marketId);
                
                // Detailed lot tracking logging
                if (_configService.Config.Warehouse.EnableLotTracking)
                {
                    _logger.LogInformation("[LOT_TRACKING] Added {Quantity} items to lot {LotId} (item: {ItemId}, cost: {UnitCost}, market: {MarketId}) - New lot quantity: {NewQuantity}", 
                        quantity, targetLot.LotId, itemId, unitCost, marketId?.ToString() ?? "global", targetLot.Quantity);
                }
                else
                {
                    _logger.LogTrace("Successfully added {Quantity} items to lot {LotId}", quantity, targetLot.LotId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add items {ItemId} to warehouse", itemId);
            return false;
        }
    }

    /// <summary>
    /// Take items from warehouse using FIFO cost consumption (cheapest first).
    /// Returns the weighted average cost of consumed items.
    /// </summary>
    public async Task<(bool Success, long ConsumedQuantity, long WeightedAverageCost)> TakeItems(ulong itemId, long quantity, ulong? marketId = null)
    {
        try
        {
            if (quantity <= 0)
            {
                _logger.LogWarning("Attempted to take non-positive quantity {Quantity} for item {ItemId}", quantity, itemId);
                return (false, 0, 0);
            }
            
            // Handle dry run mode
            if (IsDryRunMode)
            {
                _logger.LogInformation("[DRY-RUN] Taking {Quantity} items {ItemId} from market {MarketId}", 
                    quantity, itemId, marketId?.ToString() ?? "global");
                
                lock (_dryRunLock)
                {
                    var dryRunLots = GetOrCreateDryRunLots(itemId, marketId);
                    if (dryRunLots.Count == 0)
                    {
                        _logger.LogWarning("[DRY-RUN] No lots available for item {ItemId} in market {MarketId}", 
                            itemId, marketId?.ToString() ?? "global");
                        return (false, 0, 0);
                    }
                    
                    // Sort by cost (cheapest first) - FIFO consumption
                    var dryRunSortedLots = dryRunLots.OrderBy(l => l.UnitCost).ThenBy(l => l.CreatedAt).ToList();
                    
                    var dryRunRemainingQuantity = quantity;
                    var dryRunTotalCost = 0L;
                    var dryRunConsumedQuantity = 0L;
                    var dryRunLotsToRemove = new List<WarehouseLot>();
                    
                    // Consume from cheapest lots first
                    foreach (var lot in dryRunSortedLots)
                    {
                        if (dryRunRemainingQuantity <= 0) break;
                        
                        var consumeFromLot = Math.Min(lot.Quantity, dryRunRemainingQuantity);
                        dryRunTotalCost += consumeFromLot * lot.UnitCost;
                        dryRunConsumedQuantity += consumeFromLot;
                        dryRunRemainingQuantity -= consumeFromLot;
                        
                        lot.Quantity -= consumeFromLot;
                        
                        if (lot.Quantity <= 0)
                        {
                            dryRunLotsToRemove.Add(lot);
                        }
                    }
                    
                    // Remove empty lots
                    foreach (var lot in dryRunLotsToRemove)
                    {
                        dryRunLots.Remove(lot);
                    }
                    
                    // Update dry run inventory tracking
                    var inventory = GetOrCreateDryRunInventory(marketId);
                    var currentQuantity = inventory.GetValueOrDefault(itemId, 0);
                    inventory[itemId] = Math.Max(0, currentQuantity - dryRunConsumedQuantity);
                    
                    var dryRunWeightedAverageCost = dryRunConsumedQuantity > 0 ? dryRunTotalCost / dryRunConsumedQuantity : 0;
                    
                    if (_configService.Config.Warehouse.EnableLotTracking)
                    {
                        _logger.LogInformation("[DRY-RUN][LOT_TRACKING] Consumed {ConsumedQuantity}/{RequestedQuantity} items (item: {ItemId}, market: {MarketId}) at avg cost {AvgCost}", 
                            dryRunConsumedQuantity, quantity, itemId, marketId?.ToString() ?? "global", dryRunWeightedAverageCost);
                    }
                    
                    return (dryRunConsumedQuantity > 0, dryRunConsumedQuantity, dryRunWeightedAverageCost);
                }
            }

            _logger.LogDebug("Taking {Quantity} items {ItemId} from market {MarketId}", 
                quantity, itemId, marketId?.ToString() ?? "global");

            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            if (lots.Count == 0)
            {
                _logger.LogWarning("No lots available for item {ItemId} in market {MarketId}", itemId, marketId?.ToString() ?? "global");
                return (false, 0, 0);
            }

            // Sort by cost (cheapest first) - FIFO consumption
            var sortedLots = lots.OrderBy(l => l.UnitCost).ThenBy(l => l.CreatedAt).ToList();

            var remainingQuantity = quantity;
            var totalCost = 0L;
            var consumedQuantity = 0L;
            var lotsToUpdate = new List<WarehouseLot>();
            var lotsToDelete = new List<WarehouseLot>();

            // Consume from cheapest lots first
            foreach (var lot in sortedLots)
            {
                if (remainingQuantity <= 0) break;

                var consumeFromLot = Math.Min(lot.Quantity, remainingQuantity);
                totalCost += consumeFromLot * lot.UnitCost;
                consumedQuantity += consumeFromLot;
                remainingQuantity -= consumeFromLot;

                lot.Quantity -= consumeFromLot;

                if (lot.IsEmpty)
                {
                    lotsToDelete.Add(lot);
                }
                else
                {
                    lotsToUpdate.Add(lot);
                }
            }

            if (consumedQuantity == 0)
            {
                _logger.LogWarning("Could not consume any items for {ItemId}, insufficient inventory", itemId);
                return (false, 0, 0);
            }

            // Update lots
            foreach (var lot in lotsToUpdate)
            {
                await _lotStorage.UpdateLotQuantityAsync(lot.LotId, lot.Quantity);
            }

            // Delete empty lots
            foreach (var lot in lotsToDelete)
            {
                await _lotStorage.DeleteLotAsync(lot);
            }

            // Update summary and metrics
            await UpdateItemSummaryAsync(itemId, marketId);
            
            // Tag metrics for dry run mode
            var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
            _metricsService.Increment("warehouse_operations_total", metricTags);
            _metricsService.Increment("warehouse_lot_operations_total", metricTags);
            _metricsService.Increment("warehouse_consumption_operations_total", metricTags);

            var weightedAverageCost = consumedQuantity > 0 ? totalCost / consumedQuantity : 0;

            // Emit items_consumed event
            await _eventService.EmitItemsConsumedAsync(itemId, consumedQuantity, weightedAverageCost, marketId);

            // Detailed lot tracking logging
            if (_configService.Config.Warehouse.EnableLotTracking)
            {
                var lotDetails = string.Join(", ", 
                    lotsToUpdate.Select(l => $"{l.LotId}({l.Quantity}@{l.UnitCost})")
                    .Concat(lotsToDelete.Select(l => $"{l.LotId}(depleted@{l.UnitCost})"))
                );
                
                _logger.LogInformation("[LOT_TRACKING] Consumed {ConsumedQuantity}/{RequestedQuantity} items (item: {ItemId}, market: {MarketId}) at avg cost {AvgCost} - Lots affected: [{LotDetails}]", 
                    consumedQuantity, quantity, itemId, marketId?.ToString() ?? "global", weightedAverageCost, lotDetails);
            }
            else
            {
                _logger.LogTrace("Successfully consumed {ConsumedQuantity} items (requested {RequestedQuantity}) at avg cost {AvgCost}", 
                    consumedQuantity, quantity, weightedAverageCost);
            }

            return (true, consumedQuantity, weightedAverageCost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to take items {ItemId} from warehouse", itemId);
            return (false, 0, 0);
        }
    }

    /// <summary>
    /// Get total available quantity for an item
    /// </summary>
    public async Task<long> GetAvailableQuantity(ulong itemId, ulong? marketId = null)
    {
        try
        {
            // Handle dry run mode
            if (IsDryRunMode)
            {
                lock (_dryRunLock)
                {
                    var inventory = GetOrCreateDryRunInventory(marketId);
                    return inventory.GetValueOrDefault(itemId, 0);
                }
            }
            
            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            return lots.Sum(l => l.Quantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available quantity for item {ItemId}", itemId);
            return 0;
        }
    }

    /// <summary>
    /// Get weighted average cost for an item
    /// </summary>
    public async Task<long> GetWeightedAverageCost(ulong itemId, ulong? marketId = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            if (lots.Count == 0) return 0;

            var totalQuantity = lots.Sum(l => l.Quantity);
            if (totalQuantity == 0) return 0;

            var totalValue = lots.Sum(l => l.TotalValue);
            return totalValue / totalQuantity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get weighted average cost for item {ItemId}", itemId);
            return 0;
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // Track performance metrics (using SetGauge as fallback for RecordValue)
            _metricsService.SetGauge("warehouse_cost_calculation_time_ms", elapsedMs);
            
            // Log warning if cost calculation takes too long (> 10ms target)
            if (elapsedMs > 10)
            {
                _logger.LogWarning("Cost calculation for item {ItemId} took {ElapsedMs}ms (target: <10ms)", itemId, elapsedMs);
            }
        }
    }

    /// <summary>
    /// Preview the weighted average cost of items that would be consumed without actually consuming them
    /// </summary>
    public async Task<(bool Available, long WeightedAverageCost)> GetConsumptionCost(ulong itemId, long quantity, ulong? marketId = null)
    {
        try
        {
            if (quantity <= 0)
            {
                _logger.LogWarning("Attempted to preview consumption for non-positive quantity {Quantity} for item {ItemId}", quantity, itemId);
                return (false, 0);
            }

            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            if (lots.Count == 0)
            {
                _logger.LogDebug("No lots available for consumption preview for item {ItemId} in market {MarketId}", itemId, marketId?.ToString() ?? "global");
                return (false, 0);
            }

            // Sort by cost (cheapest first) - FIFO consumption preview
            var sortedLots = lots.OrderBy(l => l.UnitCost).ThenBy(l => l.CreatedAt).ToList();

            var remainingQuantity = quantity;
            var totalCost = 0L;
            var previewedQuantity = 0L;

            // Simulate consumption from cheapest lots first
            foreach (var lot in sortedLots)
            {
                if (remainingQuantity <= 0) break;

                var consumeFromLot = Math.Min(lot.Quantity, remainingQuantity);
                totalCost += consumeFromLot * lot.UnitCost;
                previewedQuantity += consumeFromLot;
                remainingQuantity -= consumeFromLot;
            }

            if (previewedQuantity == 0)
            {
                _logger.LogDebug("Could not preview consumption for any items for {ItemId}, insufficient inventory", itemId);
                return (false, 0);
            }

            var weightedAverageCost = previewedQuantity > 0 ? totalCost / previewedQuantity : 0;
            var availableForFullQuantity = previewedQuantity >= quantity;

            _logger.LogTrace("Consumption preview for item {ItemId}: {PreviewedQuantity}/{RequestedQuantity} available at avg cost {AvgCost}",
                itemId, previewedQuantity, quantity, weightedAverageCost);

            return (availableForFullQuantity, weightedAverageCost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview consumption cost for item {ItemId}", itemId);
            return (false, 0);
        }
    }

    /// <summary>
    /// Get lot summary for an item
    /// </summary>
    public async Task<WarehouseLotSummary> GetLotSummary(ulong itemId, ulong? marketId = null)
    {
        try
        {
            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            return new WarehouseLotSummary
            {
                ItemId = itemId,
                MarketId = marketId,
                Lots = lots
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lot summary for item {ItemId}", itemId);
            return new WarehouseLotSummary { ItemId = itemId, MarketId = marketId };
        }
    }

    /// <summary>
    /// Get all lots for an item
    /// </summary>
    public async Task<List<WarehouseLot>> GetLots(ulong itemId, ulong? marketId = null)
    {
        try
        {
            return await _lotStorage.GetLotsForItemAsync(itemId, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lots for item {ItemId}", itemId);
            return new List<WarehouseLot>();
        }
    }

    /// <summary>
    /// Clean up empty lots for an item
    /// </summary>
    public async Task<int> CleanupEmptyLots(ulong itemId, ulong? marketId = null)
    {
        return await _lotStorage.CleanupEmptyLotsAsync(itemId, marketId);
    }

    /// <summary>
    /// Add items using display quantities (human-readable values)
    /// </summary>
    public async Task<bool> AddItemsFromDisplay(ulong itemId, long displayQuantity, long unitCost = 0, ulong? marketId = null)
    {
        try
        {
            var rawQuantity = _gameplayBank.QuantityFromGDValue(itemId, displayQuantity);
            return await AddItems(itemId, rawQuantity, unitCost, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add display items {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Get quantity in display units (human-readable)
    /// </summary>
    public async Task<long> GetDisplayQuantity(ulong itemId, ulong? marketId = null)
    {
        try
        {
            var rawQuantity = await GetAvailableQuantity(itemId, marketId);
            return ConvertRawToDisplay(itemId, rawQuantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get display quantity for item {ItemId}", itemId);
            return 0;
        }
    }

    /// <summary>
    /// Get summaries for all items in a market
    /// </summary>
    public async Task<Dictionary<ulong, WarehouseLotSummary>> GetAllItemSummaries(ulong? marketId = null)
    {
        try
        {
            var summaries = new Dictionary<ulong, WarehouseLotSummary>();
            
            // Get Redis server for key scanning
            var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
            
            // Build pattern for item lot indexes
            var itemLotsPattern = marketId.HasValue 
                ? $"warehouse:item:*:market:{marketId}:lots"
                : "warehouse:item:*:global:lots";
                
            _logger.LogTrace("Scanning for warehouse items with pattern: {Pattern}", itemLotsPattern);
            
            // Get all keys matching the pattern
            var itemLotsKeys = server.Keys(pattern: itemLotsPattern).ToArray();
            
            foreach (var itemLotsKey in itemLotsKeys)
            {
                try
                {
                    // Extract item ID from key
                    // Pattern: warehouse:item:{itemId}:market:{marketId}:lots or warehouse:item:{itemId}:global:lots
                    var keyParts = itemLotsKey.ToString().Split(':');
                    if (keyParts.Length >= 3 && ulong.TryParse(keyParts[2], out var itemId))
                    {
                        // Get lot summary for this item
                        var summary = await GetLotSummary(itemId, marketId);
                        if (summary.LotCount > 0) // Only include items with actual inventory
                        {
                            summaries[itemId] = summary;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Could not parse item ID from Redis key: {Key}", itemLotsKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing item lots key: {Key}", itemLotsKey);
                }
            }
            
            _logger.LogDebug("Found {Count} items with inventory in market {MarketId}", 
                summaries.Count, marketId?.ToString() ?? "global");
                
            return summaries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all item summaries for market {MarketId}", marketId?.ToString() ?? "global");
            return new Dictionary<ulong, WarehouseLotSummary>();
        }
    }

    /// <summary>
    /// Get total inventory value at cost basis
    /// </summary>
    public async Task<long> GetTotalInventoryValue(ulong? marketId = null)
    {
        try
        {
            var summaries = await GetAllItemSummaries(marketId);
            return summaries.Values.Sum(s => s.TotalValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get total inventory value for market {MarketId}", marketId);
            return 0;
        }
    }

    /// <summary>
    /// Get all item quantities (for compatibility)
    /// </summary>
    public async Task<Dictionary<ulong, long>> GetAllItemQuantities(ulong? marketId = null)
    {
        try
        {
            var summaries = await GetAllItemSummaries(marketId);
            return summaries.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalQuantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all item quantities for market {MarketId}", marketId);
            return new Dictionary<ulong, long>();
        }
    }

    /// <summary>
    /// Delete all lots for a specific item
    /// </summary>
    public async Task<bool> DeleteAllItems(ulong itemId, ulong? marketId = null)
    {
        try
        {
            var lots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            foreach (var lot in lots)
            {
                await _lotStorage.DeleteLotAsync(lot);
            }
            
            await UpdateItemSummaryAsync(itemId, marketId);
            _metricsService.Increment("warehouse_operations_total");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete all items {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Clean all warehouse data for a market
    /// </summary>
    public async Task CleanWarehouse(ulong? marketId = null)
    {
        try
        {
            _logger.LogInformation("Cleaning warehouse data for market {MarketId}", marketId?.ToString() ?? "global");
            
            // Pattern for item lot indexes
            var itemLotsPattern = marketId.HasValue 
                ? $"warehouse:item:*:market:{marketId}:lots"
                : "warehouse:item:*:global:lots";
                
            // Pattern for item summaries
            var summaryPattern = marketId.HasValue 
                ? $"warehouse:item:*:market:{marketId}:summary"
                : "warehouse:item:*:global:summary";
                
            // Get all keys matching the patterns
            var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
            
            var itemLotsKeys = server.Keys(pattern: itemLotsPattern).ToArray();
            var summaryKeys = server.Keys(pattern: summaryPattern).ToArray();
            
            // Get all lot IDs from item lots before deleting the indexes
            var allLotIds = new HashSet<string>();
            foreach (var itemLotsKey in itemLotsKeys)
            {
                var lotIds = await _redisDatabase.SortedSetRangeByScoreAsync(itemLotsKey);
                foreach (var lotId in lotIds)
                {
                    allLotIds.Add(lotId);
                }
            }
            
            // Delete all lot data
            var lotKeysToDelete = allLotIds.Select(lotId => (RedisKey)$"warehouse:lot:{lotId}").ToArray();
            if (lotKeysToDelete.Length > 0)
            {
                await _redisDatabase.KeyDeleteAsync(lotKeysToDelete);
            }
            
            // Delete item lot indexes
            if (itemLotsKeys.Length > 0)
            {
                await _redisDatabase.KeyDeleteAsync(itemLotsKeys.Cast<RedisKey>().ToArray());
            }
            
            // Delete item summaries
            if (summaryKeys.Length > 0)
            {
                await _redisDatabase.KeyDeleteAsync(summaryKeys.Cast<RedisKey>().ToArray());
            }
            
            // Also clean local resources for this market
            await CleanLocalResources(marketId);
            
            _logger.LogInformation("Cleaned {LotCount} lots, {ItemIndexCount} item indexes, {SummaryCount} summaries for market {MarketId}",
                allLotIds.Count, itemLotsKeys.Length, summaryKeys.Length, marketId?.ToString() ?? "global");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean warehouse data for market {MarketId}", marketId?.ToString() ?? "global");
        }
    }

    /// <summary>
    /// Clean all warehouse data
    /// </summary>
    public async Task CleanAllWarehouseData()
    {
        try
        {
            await CleanWarehouse(null);
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                await CleanWarehouse(marketId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean all warehouse data");
        }
    }

    #endregion

    #region External System Integration Methods

    /// <summary>
    /// Add mined resources to warehouse with zero cost (WorldModel integration)
    /// </summary>
    public async Task<bool> AddMinedResources(ulong itemId, long quantity, ulong? marketId = null)
    {
        try
        {
            _logger.LogDebug("Adding {Quantity} mined resources for item {ItemId} in market {MarketId}", 
                quantity, itemId, marketId?.ToString() ?? "global");
                
            // Mined resources have zero cost basis
            return await AddItems(itemId, quantity, unitCost: 0, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add mined resources for item {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Add purchased items to warehouse with market price (MarketMaker integration)
    /// </summary>
    public async Task<bool> AddPurchasedItems(ulong itemId, long quantity, long unitPrice, ulong? marketId = null)
    {
        try
        {
            _logger.LogDebug("Adding {Quantity} purchased items for item {ItemId} at price {UnitPrice} in market {MarketId}", 
                quantity, itemId, unitPrice, marketId?.ToString() ?? "global");
                
            // Use executed trade price as unit cost
            return await AddItems(itemId, quantity, unitCost: unitPrice, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add purchased items for item {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Add crafted items to warehouse with calculated production cost (Industry integration)
    /// </summary>
    public async Task<bool> AddCraftedItems(ulong itemId, long quantity, long calculatedCost, ulong? marketId = null)
    {
        try
        {
            _logger.LogDebug("Adding {Quantity} crafted items for item {ItemId} with cost {CalculatedCost} in market {MarketId}", 
                quantity, itemId, calculatedCost, marketId?.ToString() ?? "global");
                
            // Use calculated cost of production as unit cost
            return await AddItems(itemId, quantity, unitCost: calculatedCost, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add crafted items for item {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Add shipped items to warehouse with shipping cost included (Logistics integration)
    /// </summary>
    public async Task<bool> AddShippedItems(ulong itemId, long quantity, long originalCost, long freightCost, ulong? marketId = null)
    {
        try
        {
            var totalCost = originalCost + freightCost;
            
            _logger.LogDebug("Adding {Quantity} shipped items for item {ItemId} with total cost {TotalCost} (original: {OriginalCost} + freight: {FreightCost}) in market {MarketId}", 
                quantity, itemId, totalCost, originalCost, freightCost, marketId?.ToString() ?? "global");
                
            // Use original cost plus freight as unit cost
            return await AddItems(itemId, quantity, unitCost: totalCost, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add shipped items for item {ItemId}", itemId);
            return false;
        }
    }

    #endregion

    #region Legacy Compatibility Methods

    /// <summary>
    /// Legacy method: Add/update items with specified value (raw quantity)
    /// Maps to new lot-based system with default cost of 0
    /// </summary>
    public async Task AddUpdate(ulong itemId, long value, ulong? marketId = null)
    {
        if (value > 0)
        {
            await AddItems(itemId, value, unitCost: 0, marketId);
        }
        else if (value < 0)
        {
            await TakeItems(itemId, Math.Abs(value), marketId);
        }
    }

    /// <summary>
    /// Legacy method: Add/update items with raw value
    /// Maps to AddUpdate for backward compatibility
    /// </summary>
    public async Task AddUpdateRaw(ulong itemId, long rawValue, ulong? marketId = null)
    {
        await AddUpdate(itemId, rawValue, marketId);
    }

    /// <summary>
    /// Legacy method: Add/update items from display quantity
    /// Maps to new AddItemsFromDisplay method
    /// </summary>
    public async Task AddUpdateFromDisplay(ulong itemId, long displayQuantity, ulong? marketId = null)
    {
        var rawQuantity = _gameplayBank.QuantityFromGDValue(itemId, displayQuantity);
        await AddUpdate(itemId, rawQuantity, marketId);
    }

    /// <summary>
    /// Legacy method: Get total quantity for an item
    /// Maps to new GetAvailableQuantity method
    /// </summary>
    public async Task<long> Get(ulong itemId, ulong? marketId = null)
    {
        return await GetAvailableQuantity(itemId, marketId);
    }

    /// <summary>
    /// Legacy method: Clean inventory with backup (maps to CleanWarehouse)
    /// </summary>
    public async Task SafeCleanInventoryWithBackup(ulong? marketId = null)
    {
        _logger.LogWarning("SafeCleanInventoryWithBackup called - mapping to CleanWarehouse (no backup implemented in lot system)");
        await CleanWarehouse(marketId);
    }

    #endregion

    #region Local Resource Tracking

    /// <summary>
    /// Gets locally generated resource quantity in display units (human-readable).
    /// </summary>
    public async Task<long> GetLocalResourceDisplay(ulong itemId, ulong? marketId = null)
    {
        try
        {
            var rawQuantity = await GetLocalResourceRaw(itemId, marketId);
            return ConvertRawToDisplay(itemId, rawQuantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get local resource display for item {ItemId}", itemId);
            return 0;
        }
    }

    /// <summary>
    /// Adds/updates locally generated resources using display quantities.
    /// Tracks separately from imported/crafted resources for capacity management.
    /// </summary>
    public async Task AddUpdateFromDisplayLocal(ulong itemId, long displayQuantity, ulong? marketId = null)
    {
        try
        {
            // Convert display to raw using GameplayBank
            var rawQuantity = _gameplayBank.QuantityFromGDValue(itemId, displayQuantity);
            await AddUpdateLocalRaw(itemId, rawQuantity, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add/update local resource display for item {ItemId}", itemId);
        }
    }

    /// <summary>
    /// Gets all locally generated resources for a market.
    /// </summary>
    public async Task<Dictionary<ulong, long>> GetAllLocalResources(ulong? marketId = null)
    {
        try
        {
            var localKey = GetLocalResourceKey(marketId);
            var hashEntries = await _redisDatabase.HashGetAllAsync(localKey);
            
            return hashEntries.ToDictionary(
                entry => ulong.Parse(entry.Name),
                entry => (long)entry.Value
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all local resources for market {MarketId}", marketId?.ToString() ?? "global");
            return new Dictionary<ulong, long>();
        }
    }

    /// <summary>
    /// Cleans all local resource data for a market.
    /// </summary>
    public async Task CleanLocalResources(ulong? marketId = null)
    {
        try
        {
            var localKey = GetLocalResourceKey(marketId);
            await _redisDatabase.KeyDeleteAsync(localKey);
            
            _logger.LogInformation("Cleaned local resource data for market {MarketId}", marketId?.ToString() ?? "global");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean local resources for market {MarketId}", marketId?.ToString() ?? "global");
        }
    }

    #endregion

    #region Private Helper Methods
    
    /// <summary>
    /// Check if dry run mode is enabled.
    /// </summary>
    private bool IsDryRunMode => _configService.Config.Development.DryRun;
    
    /// <summary>
    /// Get dry run inventory key for market.
    /// </summary>
    private string GetDryRunInventoryKey(ulong? marketId) => marketId?.ToString() ?? "global";
    
    /// <summary>
    /// Get dry run lot key for market.
    /// </summary>
    private string GetDryRunLotKey(ulong itemId, ulong? marketId) => $"{marketId?.ToString() ?? "global"}:{itemId}";
    
    /// <summary>
    /// Get or create dry run inventory for a market.
    /// </summary>
    private Dictionary<ulong, long> GetOrCreateDryRunInventory(ulong? marketId)
    {
        lock (_dryRunLock)
        {
            var key = GetDryRunInventoryKey(marketId);
            if (!_dryRunInventory.ContainsKey(key))
            {
                _dryRunInventory[key] = new Dictionary<ulong, long>();
            }
            return _dryRunInventory[key];
        }
    }
    
    /// <summary>
    /// Get or create dry run lots for an item/market combination.
    /// </summary>
    private List<WarehouseLot> GetOrCreateDryRunLots(ulong itemId, ulong? marketId)
    {
        lock (_dryRunLock)
        {
            var key = GetDryRunLotKey(itemId, marketId);
            if (!_dryRunLots.ContainsKey(key))
            {
                _dryRunLots[key] = new List<WarehouseLot>();
            }
            return _dryRunLots[key];
        }
    }

    /// <summary>
    /// Find appropriate lot for items with given unit cost, or create new lot.
    /// Implements dynamic lot management with adaptive cost-based grouping.
    /// </summary>
    private async Task<WarehouseLot?> FindOrCreateLotAsync(ulong itemId, long unitCost, ulong? marketId)
    {
        try
        {
            var existingLots = await _lotStorage.GetLotsForItemAsync(itemId, marketId);
            
            // If no existing lots, create new one
            if (existingLots.Count == 0)
            {
                return await CreateNewLotAsync(itemId, unitCost, marketId);
            }
            
            // Find exact cost match first
            var matchingLot = existingLots.FirstOrDefault(l => l.UnitCost == unitCost);
            if (matchingLot != null)
            {
                return matchingLot;
            }
            
            // Calculate optimal cost tolerance range for smart grouping
            var optimalRange = CalculateOptimalRange(existingLots, unitCost);
            
            // Find existing lot within calculated tolerance range
            var compatibleLot = existingLots.FirstOrDefault(lot => 
                Math.Abs(lot.UnitCost - unitCost) <= optimalRange);
                
            if (compatibleLot != null)
            {
                if (_configService.Config.Warehouse.EnableLotTracking)
                {
                    _logger.LogInformation("[LOT_TRACKING] Found compatible lot {LotId}({Qty}@{ExistingCost}) for new cost {NewCost} within range {Range} - item {ItemId} in market {MarketId}", 
                        compatibleLot.LotId, compatibleLot.Quantity, compatibleLot.UnitCost, unitCost, optimalRange, itemId, marketId?.ToString() ?? "global");
                }
                else
                {
                    _logger.LogTrace("Found compatible lot {LotId} with cost {ExistingCost} for new cost {NewCost} (range: {Range})", 
                        compatibleLot.LotId, compatibleLot.UnitCost, unitCost, optimalRange);
                }
                return compatibleLot;
            }

            // Check lot limit before creating new lot
            var maxLots = _configService.Config.Warehouse.MaxLotsPerItem;
            if (existingLots.Count >= maxLots)
            {
                await MergeClosestLotsAsync(existingLots);
            }

            // Create new lot
            return await CreateNewLotAsync(itemId, unitCost, marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find or create lot for item {ItemId}", itemId);
            return null;
        }
    }
    
    /// <summary>
    /// Calculate optimal price tolerance range for lot grouping using intelligent decision making.
    /// Uses lot size, cost impact, and precision trade-offs to make smart grouping decisions.
    /// </summary>
    private long CalculateOptimalRange(List<WarehouseLot> existingLots, long newUnitCost)
    {
        if (existingLots.Count == 0) return 0;
        
        try
        {
            // For single lot, be more selective with high-value items
            if (existingLots.Count == 1)
            {
                var existingLot = existingLots[0];
                return CalculateCompatibilityRange(existingLot, newUnitCost);
            }
            
            // Find the best candidate lot for intelligent matching
            WarehouseLot? bestCandidate = null;
            long bestRange = 0;
            double bestScore = double.MinValue;
            
            foreach (var lot in existingLots)
            {
                var compatibilityRange = CalculateCompatibilityRange(lot, newUnitCost);
                var costDiff = Math.Abs(lot.UnitCost - newUnitCost);
                
                // Skip if costs are too far apart
                if (costDiff > compatibilityRange) continue;
                
                // Calculate compatibility score considering multiple factors
                var score = CalculateCompatibilityScore(lot, newUnitCost, existingLots);
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = lot;
                    bestRange = compatibilityRange;
                }
            }
            
            if (bestCandidate != null)
            {
                _logger.LogTrace("Intelligent lot matching: best candidate {LotId} (cost: {Cost}, quantity: {Qty}) with range {Range} for new cost {NewCost}", 
                    bestCandidate.LotId, bestCandidate.UnitCost, bestCandidate.Quantity, bestRange, newUnitCost);
                return bestRange;
            }
            
            // No good candidate found - return 0 to force new lot creation
            _logger.LogTrace("No compatible lot found for cost {NewCost}, will create new lot", newUnitCost);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate optimal range intelligently, falling back to conservative approach");
            // Fallback: be very conservative
            return newUnitCost > 0 ? newUnitCost / 20 : 0; // 5% range as fallback
        }
    }
    
    /// <summary>
    /// Calculate compatibility range between a lot and new unit cost based on lot characteristics
    /// </summary>
    private long CalculateCompatibilityRange(WarehouseLot lot, long newUnitCost)
    {
        if (lot.UnitCost == 0 || newUnitCost == 0) return 0;
        
        // Base range starts at 2% for precision
        var basePercentage = 0.02;
        
        // Adjust based on lot size - larger lots can accommodate more variance
        // Small lots (< 1000): stick to base
        // Medium lots (1000-10000): increase to 3%
        // Large lots (> 10000): increase to 5%
        if (lot.Quantity > 10000)
        {
            basePercentage = 0.05;
        }
        else if (lot.Quantity > 1000)
        {
            basePercentage = 0.03;
        }
        
        // Adjust based on cost level - higher costs can tolerate more absolute variance
        var avgCost = (lot.UnitCost + newUnitCost) / 2;
        if (avgCost > 1000000) // High-value items
        {
            basePercentage += 0.02; // Allow 2% more variance
        }
        else if (avgCost < 100) // Low-value items - be more precise
        {
            basePercentage *= 0.5; // Halve the range
        }
        
        // Calculate the actual range
        var range = (long)(avgCost * basePercentage);
        
        _logger.LogTrace("Compatibility range for lot {LotId}: {Range} (quantity: {Qty}, avgCost: {AvgCost}, percentage: {Pct:P2})", 
            lot.LotId, range, lot.Quantity, avgCost, basePercentage);
            
        return Math.Max(range, 1); // Minimum range of 1
    }
    
    /// <summary>
    /// Calculate compatibility score for intelligent lot selection
    /// Higher score means better candidate for grouping
    /// </summary>
    private double CalculateCompatibilityScore(WarehouseLot candidateLot, long newUnitCost, List<WarehouseLot> allLots)
    {
        var costDiff = Math.Abs(candidateLot.UnitCost - newUnitCost);
        var avgCost = (candidateLot.UnitCost + newUnitCost) / 2.0;
        
        // Factor 1: Cost proximity (closer = better)
        var costProximityScore = avgCost > 0 ? (1.0 - (costDiff / avgCost)) * 100 : 0;
        
        // Factor 2: Lot size advantage (larger lots absorb variance better)
        var lotSizeScore = Math.Min(candidateLot.Quantity / 1000.0, 50); // Cap at 50 points
        
        // Factor 3: Cost level factor (prefer grouping similar cost levels)
        var costLevelScore = 0.0;
        if (candidateLot.UnitCost > 0 && newUnitCost > 0)
        {
            var costRatio = Math.Max(candidateLot.UnitCost, newUnitCost) / (double)Math.Min(candidateLot.UnitCost, newUnitCost);
            costLevelScore = Math.Max(0, 10 - (costRatio - 1) * 10); // Better score for similar cost levels
        }
        
        // Factor 4: Lot distribution balance (avoid creating too many small lots)
        var distributionScore = allLots.Count > 5 ? 20 : 0; // Bonus for consolidation when many lots exist
        
        var totalScore = costProximityScore + lotSizeScore + costLevelScore + distributionScore;
        
        _logger.LogTrace("Compatibility score for lot {LotId}: {Score:F1} (proximity: {P:F1}, size: {S:F1}, level: {L:F1}, distribution: {D:F1})", 
            candidateLot.LotId, totalScore, costProximityScore, lotSizeScore, costLevelScore, distributionScore);
            
        return totalScore;
    }
    
    /// <summary>
    /// Create a new lot and emit creation event
    /// </summary>
    private async Task<WarehouseLot> CreateNewLotAsync(ulong itemId, long unitCost, ulong? marketId)
    {
        var lotId = _lotStorage.GenerateLotId();
        var newLot = new WarehouseLot(lotId, itemId, marketId, 0, unitCost);
        await _lotStorage.StoreLotAsync(newLot);
        
        // Emit lot_created event
        await _eventService.EmitLotCreatedAsync(lotId, itemId, unitCost, marketId);
        
        // Detailed lot tracking logging
        if (_configService.Config.Warehouse.EnableLotTracking)
        {
            _logger.LogInformation("[LOT_TRACKING] Created new lot {LotId} for item {ItemId} with cost {UnitCost} in market {MarketId}", 
                lotId, itemId, unitCost, marketId?.ToString() ?? "global");
        }
        
        return newLot;
    }

    /// <summary>
    /// Merge the two lots with closest unit costs to make room for new lots.
    /// Uses weighted average to calculate new cost.
    /// </summary>
    private async Task<bool> MergeClosestLotsAsync(List<WarehouseLot> lots)
    {
        if (lots.Count < 2) return false;
        
        // Find closest cost pair
        var bestPair = (Lot1: (WarehouseLot?)null, Lot2: (WarehouseLot?)null, Diff: long.MaxValue);
        for (int i = 0; i < lots.Count; i++)
        {
            for (int j = i + 1; j < lots.Count; j++)
            {
                var diff = Math.Abs(lots[i].UnitCost - lots[j].UnitCost);
                if (diff < bestPair.Diff)
                {
                    bestPair = (lots[i], lots[j], diff);
                }
            }
        }

        if (bestPair.Lot1 == null || bestPair.Lot2 == null) return false;

        // Store IDs before merging
        var oldLotId1 = bestPair.Lot1.LotId;
        var oldLotId2 = bestPair.Lot2.LotId;
        var itemId = bestPair.Lot1.ItemId;
        var marketId = bestPair.Lot1.MarketId;
        
        // Merge lots
        var totalQty = bestPair.Lot1.Quantity + bestPair.Lot2.Quantity;
        var totalValue = bestPair.Lot1.TotalValue + bestPair.Lot2.TotalValue;
        bestPair.Lot1.Quantity = totalQty;
        bestPair.Lot1.UnitCost = totalQty > 0 ? totalValue / totalQty : 0;

        await _lotStorage.StoreLotAsync(bestPair.Lot1);
        await _lotStorage.DeleteLotAsync(bestPair.Lot2);
        
        // Emit lot_merged event
        await _eventService.EmitLotMergedAsync(oldLotId1, oldLotId2, bestPair.Lot1.LotId, bestPair.Lot1.UnitCost, itemId, marketId);
        
        // Track lot merge frequency
        _metricsService.Increment("warehouse_lot_merge_frequency");
        
        // Detailed lot tracking logging
        if (_configService.Config.Warehouse.EnableLotTracking)
        {
            _logger.LogInformation("[LOT_TRACKING] Merged lots {OldLot1}({Qty1}@{Cost1}) + {OldLot2}({Qty2}@{Cost2}) → {NewLot}({NewQty}@{NewCost}) for item {ItemId} in market {MarketId}", 
                oldLotId1, bestPair.Lot1.Quantity - bestPair.Lot2.Quantity, bestPair.Lot1.UnitCost, 
                oldLotId2, bestPair.Lot2.Quantity, bestPair.Lot2.UnitCost,
                bestPair.Lot1.LotId, bestPair.Lot1.Quantity, bestPair.Lot1.UnitCost, 
                itemId, marketId?.ToString() ?? "global");
        }
        
        return true;
    }

    /// <summary>
    /// Update cached summary information for an item
    /// </summary>
    private async Task UpdateItemSummaryAsync(ulong itemId, ulong? marketId)
    {
        try
        {
            var summary = await GetLotSummary(itemId, marketId);
            await _lotStorage.StoreItemSummaryAsync(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update item summary for {ItemId}", itemId);
        }
    }

    /// <summary>
    /// Convert raw quantity to display format
    /// </summary>
    private long ConvertRawToDisplay(ulong itemId, long rawQuantity)
    {
        try
        {
            if (_gameplayBank.GetDefinition(itemId).BaseObject is Material)
            {
                return rawQuantity / (long)QuantityConstants.QuantityToVolumeCoeff;
            }
            return rawQuantity;
        }
        catch
        {
            return rawQuantity;
        }
    }

    /// <summary>
    /// Generate Redis key for local resource storage
    /// </summary>
    private string GetLocalResourceKey(ulong? marketId)
    {
        return marketId.HasValue ? $"warehouse:local:{marketId}:items" : "warehouse:local:global:items";
    }

    /// <summary>
    /// Gets raw locally generated resource quantity.
    /// </summary>
    private async Task<long> GetLocalResourceRaw(ulong itemId, ulong? marketId)
    {
        try
        {
            var localKey = GetLocalResourceKey(marketId);
            var value = await _redisDatabase.HashGetAsync(localKey, itemId.ToString());
            
            if (value.HasValue && value.TryParse(out long quantity))
            {
                return quantity;
            }
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get local resource raw for item {ItemId}", itemId);
            return 0;
        }
    }

    /// <summary>
    /// Adds/updates raw locally generated resource quantities.
    /// Uses separate Redis keys to track local vs imported resources.
    /// </summary>
    private async Task AddUpdateLocalRaw(ulong itemId, long rawValue, ulong? marketId)
    {
        try
        {
            var localKey = GetLocalResourceKey(marketId);
            
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
            _metricsService.Increment("warehouse_operations_total");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add/update local resource raw for item {ItemId}", itemId);
        }
    }

    /// <summary>
    /// Calculate cost variance metrics for items in a market
    /// </summary>
    private async Task CalculateCostVarianceMetrics(ulong? marketId, IMetricsService metricsService)
    {
        try
        {
            var itemSummaries = await GetAllItemSummaries(marketId);
            var marketLabel = marketId?.ToString() ?? "global";
            
            foreach (var summary in itemSummaries.Values)
            {
                if (summary.LotCount <= 1)
                {
                    // No variance with 0 or 1 lot
                    metricsService.SetGauge("warehouse_cost_variance", 
                        new[] { marketLabel, summary.ItemId.ToString() }, 0);
                    continue;
                }
                
                // Calculate cost variance (spread between highest and lowest cost)
                var costSpread = summary.HighestCost - summary.LowestCost;
                var avgCost = summary.WeightedAverageCost;
                var variancePercentage = avgCost > 0 ? (double)costSpread / avgCost * 100 : 0;
                
                metricsService.SetGauge("warehouse_cost_variance", 
                    new[] { marketLabel, summary.ItemId.ToString() }, variancePercentage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate cost variance metrics for market {MarketId}", marketId);
        }
    }
    
    /// <summary>
    /// Calculate consumption rate metrics (items consumed per hour)
    /// </summary>
    private async Task CalculateConsumptionRateMetrics(IMetricsService metricsService)
    {
        try
        {
            // For now, we'll use a simple rate calculation based on inventory levels
            // In a production system, you might want to store timestamped consumption data
            var currentTime = DateTime.UtcNow;
            
            // Calculate rate for each market
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var itemSummaries = await GetAllItemSummaries(marketId);
                
                foreach (var summary in itemSummaries.Values)
                {
                    // Simplified estimation: assume consumption rate based on current quantity and lot diversity
                    // Higher lot count often indicates more active trading
                    var estimatedHourlyRate = summary.LotCount > 1 ? summary.TotalQuantity / 24 : summary.TotalQuantity / 48;
                    
                    metricsService.SetGauge("warehouse_consumption_rate", 
                        new[] { marketId.ToString(), summary.ItemId.ToString() }, estimatedHourlyRate);
                }
            }
            
            // Global consumption rate
            var globalSummaries = await GetAllItemSummaries(null);
            foreach (var summary in globalSummaries.Values)
            {
                var estimatedHourlyRate = summary.LotCount > 1 ? summary.TotalQuantity / 24 : summary.TotalQuantity / 48;
                
                metricsService.SetGauge("warehouse_consumption_rate", 
                    new[] { "global", summary.ItemId.ToString() }, estimatedHourlyRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate consumption rate metrics");
        }
    }
    
    /// <summary>
    /// Calculate merge operation ratio to ensure it stays below 5% target
    /// </summary>
    private async Task CalculateMergeOperationRatio(IMetricsService metricsService)
    {
        try
        {
            await Task.CompletedTask; // For async consistency
            
            // For now, we'll estimate merge ratio based on average lots per item
            // In production, you might want to track actual operation counters
            
            var totalItems = 0;
            var totalLots = 0;
            
            // Count total items and lots across all markets
            for (ulong marketId = 1; marketId <= 5; marketId++)
            {
                var itemSummaries = await GetAllItemSummaries(marketId);
                totalItems += itemSummaries.Count;
                totalLots += itemSummaries.Values.Sum(s => s.LotCount);
            }
            
            var globalSummaries = await GetAllItemSummaries(null);
            totalItems += globalSummaries.Count;
            totalLots += globalSummaries.Values.Sum(s => s.LotCount);
            
            // Estimate merge ratio based on lot density
            var avgLotsPerItem = totalItems > 0 ? (double)totalLots / totalItems : 0;
            var maxLots = _configService.Config.Warehouse.MaxLotsPerItem;
            
            // Estimate merge pressure: higher ratio when closer to max lots limit
            var estimatedMergeRatio = maxLots > 0 ? Math.Max(0, (avgLotsPerItem / maxLots - 0.8) * 25) : 0;
            
            metricsService.SetGauge("warehouse_merge_operation_ratio", estimatedMergeRatio);
            
            // Log warning if estimated merge ratio exceeds 5% target
            if (estimatedMergeRatio > 5.0)
            {
                _logger.LogWarning("Estimated merge operation ratio is {MergeRatio:F2}% (target: <5%) - consider increasing MaxLotsPerItem from {MaxLots}", estimatedMergeRatio, maxLots);
            }
            else if (estimatedMergeRatio > 0)
            {
                _logger.LogDebug("Estimated merge operation ratio: {MergeRatio:F2}% (target: <5%)", estimatedMergeRatio);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate merge operation ratio");
        }
    }

    #endregion
    }
}
