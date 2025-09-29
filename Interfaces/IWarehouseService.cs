using System.Collections.Generic;
using System.Threading.Tasks;
using MarketBot.Domain;

public interface IWarehouseService
{
    // Core lot-based operations
    Task<bool> AddItems(ulong itemId, long quantity, long unitCost = 0, ulong? marketId = null);
    Task<(bool Success, long ConsumedQuantity, long WeightedAverageCost)> TakeItems(ulong itemId, long quantity, ulong? marketId = null);
    Task<long> GetAvailableQuantity(ulong itemId, ulong? marketId = null);
    Task<long> GetWeightedAverageCost(ulong itemId, ulong? marketId = null);
    Task<(bool Available, long WeightedAverageCost)> GetConsumptionCost(ulong itemId, long quantity, ulong? marketId = null);
    
    // Lot management
    Task<WarehouseLotSummary> GetLotSummary(ulong itemId, ulong? marketId = null);
    Task<List<WarehouseLot>> GetLots(ulong itemId, ulong? marketId = null);
    Task<int> CleanupEmptyLots(ulong itemId, ulong? marketId = null);
    
    // Display quantity methods (for human-readable values)
    Task<bool> AddItemsFromDisplay(ulong itemId, long displayQuantity, long unitCost = 0, ulong? marketId = null);
    Task<long> GetDisplayQuantity(ulong itemId, ulong? marketId = null);
    
    // Warehouse-wide operations
    Task<Dictionary<ulong, WarehouseLotSummary>> GetAllItemSummaries(ulong? marketId = null);
    Task<long> GetTotalInventoryValue(ulong? marketId = null);
    Task<Dictionary<ulong, long>> GetAllItemQuantities(ulong? marketId = null);
    
    // Local resource tracking (for resource generation capacity management)
    Task<long> GetLocalResourceDisplay(ulong itemId, ulong? marketId = null);
    Task AddUpdateFromDisplayLocal(ulong itemId, long displayQuantity, ulong? marketId = null);
    Task<Dictionary<ulong, long>> GetAllLocalResources(ulong? marketId = null);
    Task CleanLocalResources(ulong? marketId = null);
    
    // Legacy compatibility methods (for backward compatibility with existing services)
    Task AddUpdate(ulong itemId, long value, ulong? marketId = null);
    Task AddUpdateRaw(ulong itemId, long rawValue, ulong? marketId = null);
    Task AddUpdateFromDisplay(ulong itemId, long displayQuantity, ulong? marketId = null);
    Task<long> Get(ulong itemId, ulong? marketId = null);
    Task SafeCleanInventoryWithBackup(ulong? marketId = null);
    
    // External system integration methods
    Task<bool> AddMinedResources(ulong itemId, long quantity, ulong? marketId = null);
    Task<bool> AddPurchasedItems(ulong itemId, long quantity, long unitPrice, ulong? marketId = null);
    Task<bool> AddCraftedItems(ulong itemId, long quantity, long calculatedCost, ulong? marketId = null);
    Task<bool> AddShippedItems(ulong itemId, long quantity, long originalCost, long freightCost, ulong? marketId = null);
    
    // Management operations
    Task<bool> DeleteAllItems(ulong itemId, ulong? marketId = null);
    Task CleanWarehouse(ulong? marketId = null);
    Task CleanAllWarehouseData();
}
