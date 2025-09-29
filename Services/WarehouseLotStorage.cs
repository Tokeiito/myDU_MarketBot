using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Domain;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace MarketBot.Services
{
    /// <summary>
    /// Redis storage operations for warehouse lots.
    /// Manages lot persistence, retrieval, and indexing.
    /// </summary>
    public class WarehouseLotStorage : IWarehouseLotStorage
    {
        private readonly IDatabase _redisDatabase;
        private readonly ILogger<WarehouseLotStorage> _logger;

        public WarehouseLotStorage(IDatabase redisDatabase, ILogger<WarehouseLotStorage> logger)
        {
            _redisDatabase = redisDatabase ?? throw new ArgumentNullException(nameof(redisDatabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Generate Redis keys for lot storage
        /// </summary>
        private static class RedisKeys
        {
            public static string LotHash(string lotId) => $"warehouse:lot:{lotId}";
            public static string ItemLots(ulong itemId, ulong? marketId) => 
                marketId.HasValue ? $"warehouse:item:{itemId}:market:{marketId}:lots" : $"warehouse:item:{itemId}:global:lots";
            public static string ItemSummary(ulong itemId, ulong? marketId) => 
                marketId.HasValue ? $"warehouse:item:{itemId}:market:{marketId}:summary" : $"warehouse:item:{itemId}:global:summary";
        }

        /// <summary>
        /// Store a lot in Redis
        /// </summary>
        public async Task<bool> StoreLotAsync(WarehouseLot lot)
        {
            try
            {
                var lotKey = RedisKeys.LotHash(lot.LotId);
                var itemLotsKey = RedisKeys.ItemLots(lot.ItemId, lot.MarketId);

                // Store lot data as hash
                var lotData = new HashEntry[]
                {
                    new("lot_id", lot.LotId),
                    new("item_id", lot.ItemId.ToString()),
                    new("market_id", lot.MarketId?.ToString() ?? ""),
                    new("quantity", lot.Quantity.ToString()),
                    new("unit_cost", lot.UnitCost.ToString()),
                    new("created_at", lot.CreatedAt.ToBinary().ToString())
                };

                await _redisDatabase.HashSetAsync(lotKey, lotData);

                // Add lot to item's lot set (using unit cost as score for future sorting)
                await _redisDatabase.SortedSetAddAsync(itemLotsKey, lot.LotId, lot.UnitCost);

                _logger.LogTrace("Stored lot {LotId} for item {ItemId} in market {MarketId}", 
                    lot.LotId, lot.ItemId, lot.MarketId?.ToString() ?? "global");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store lot {LotId}", lot.LotId);
                return false;
            }
        }

        /// <summary>
        /// Retrieve a lot by ID
        /// </summary>
        public async Task<WarehouseLot?> GetLotAsync(string lotId)
        {
            try
            {
                var lotKey = RedisKeys.LotHash(lotId);
                var lotData = await _redisDatabase.HashGetAllAsync(lotKey);

                if (lotData.Length == 0)
                {
                    return null;
                }

                var lotDict = lotData.ToDictionary(x => (string)x.Name, x => (string)x.Value);

                var lot = new WarehouseLot(
                    lotDict["lot_id"],
                    ulong.Parse(lotDict["item_id"]),
                    string.IsNullOrEmpty(lotDict["market_id"]) ? null : ulong.Parse(lotDict["market_id"]),
                    long.Parse(lotDict["quantity"]),
                    long.Parse(lotDict["unit_cost"])
                );

                lot.CreatedAt = DateTime.FromBinary(long.Parse(lotDict["created_at"]));

                return lot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve lot {LotId}", lotId);
                return null;
            }
        }

        /// <summary>
        /// Get all lots for an item/market combination
        /// </summary>
        public async Task<List<WarehouseLot>> GetLotsForItemAsync(ulong itemId, ulong? marketId)
        {
            try
            {
                var itemLotsKey = RedisKeys.ItemLots(itemId, marketId);
                var lotIds = await _redisDatabase.SortedSetRangeByScoreAsync(itemLotsKey);

                if (lotIds.Length == 0)
                {
                    return new List<WarehouseLot>();
                }

                var lots = new List<WarehouseLot>();
                foreach (var lotId in lotIds)
                {
                    var lot = await GetLotAsync(lotId);
                    if (lot != null)
                    {
                        lots.Add(lot);
                    }
                    else
                    {
                        // Clean up dangling reference
                        await _redisDatabase.SortedSetRemoveAsync(itemLotsKey, lotId);
                        _logger.LogWarning("Removed dangling lot reference {LotId} from item {ItemId}", lotId, itemId);
                    }
                }

                return lots;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve lots for item {ItemId} in market {MarketId}", itemId, marketId?.ToString() ?? "global");
                return new List<WarehouseLot>();
            }
        }

        /// <summary>
        /// Update an existing lot's quantity
        /// </summary>
        public async Task<bool> UpdateLotQuantityAsync(string lotId, long newQuantity)
        {
            try
            {
                var lotKey = RedisKeys.LotHash(lotId);
                await _redisDatabase.HashSetAsync(lotKey, "quantity", newQuantity.ToString());

                _logger.LogTrace("Updated lot {LotId} quantity to {Quantity}", lotId, newQuantity);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update quantity for lot {LotId}", lotId);
                return false;
            }
        }

        /// <summary>
        /// Delete a lot completely
        /// </summary>
        public async Task<bool> DeleteLotAsync(WarehouseLot lot)
        {
            try
            {
                var lotKey = RedisKeys.LotHash(lot.LotId);
                var itemLotsKey = RedisKeys.ItemLots(lot.ItemId, lot.MarketId);

                // Remove from both lot hash and item's lot set
                var deleteHash = _redisDatabase.KeyDeleteAsync(lotKey);
                var removeFromSet = _redisDatabase.SortedSetRemoveAsync(itemLotsKey, lot.LotId);

                await Task.WhenAll(deleteHash, removeFromSet);

                _logger.LogTrace("Deleted lot {LotId} for item {ItemId}", lot.LotId, lot.ItemId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete lot {LotId}", lot.LotId);
                return false;
            }
        }

        /// <summary>
        /// Store item summary data (total quantity, avg cost, etc.)
        /// </summary>
        public async Task<bool> StoreItemSummaryAsync(WarehouseLotSummary summary)
        {
            try
            {
                var summaryKey = RedisKeys.ItemSummary(summary.ItemId, summary.MarketId);
                
                var summaryData = new HashEntry[]
                {
                    new("item_id", summary.ItemId.ToString()),
                    new("market_id", summary.MarketId?.ToString() ?? ""),
                    new("total_quantity", summary.TotalQuantity.ToString()),
                    new("total_value", summary.TotalValue.ToString()),
                    new("weighted_avg_cost", summary.WeightedAverageCost.ToString()),
                    new("lot_count", summary.LotCount.ToString()),
                    new("lowest_cost", summary.LowestCost.ToString()),
                    new("highest_cost", summary.HighestCost.ToString()),
                    new("last_updated", DateTime.UtcNow.ToBinary().ToString())
                };

                await _redisDatabase.HashSetAsync(summaryKey, summaryData);

                _logger.LogTrace("Stored summary for item {ItemId} in market {MarketId}: {LotCount} lots, {TotalQuantity} total", 
                    summary.ItemId, summary.MarketId?.ToString() ?? "global", summary.LotCount, summary.TotalQuantity);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store summary for item {ItemId}", summary.ItemId);
                return false;
            }
        }

        /// <summary>
        /// Generate a unique lot ID
        /// </summary>
        public string GenerateLotId()
        {
            return Guid.NewGuid().ToString("N")[..12]; // Short, readable ID
        }

        /// <summary>
        /// Clean up empty lots for an item
        /// </summary>
        public async Task<int> CleanupEmptyLotsAsync(ulong itemId, ulong? marketId)
        {
            try
            {
                var lots = await GetLotsForItemAsync(itemId, marketId);
                var emptyLots = lots.Where(l => l.IsEmpty).ToList();

                foreach (var emptyLot in emptyLots)
                {
                    await DeleteLotAsync(emptyLot);
                }

                if (emptyLots.Count > 0)
                {
                    _logger.LogDebug("Cleaned up {Count} empty lots for item {ItemId} in market {MarketId}", 
                        emptyLots.Count, itemId, marketId?.ToString() ?? "global");
                }

                return emptyLots.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup empty lots for item {ItemId}", itemId);
                return 0;
            }
        }
    }
}