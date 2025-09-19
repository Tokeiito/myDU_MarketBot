using System.Collections.Generic;
using System.Threading.Tasks;

public interface IInventoryService
{
    Task<Dictionary<ulong, string>> GetAll(ulong? marketId);
    Task<long> Get(ulong itemId, ulong? marketId);
    Task AddUpdate(ulong itemId, long quantity, ulong? marketId);
    Task Delete(ulong itemId, ulong? marketId);
    Task CleanInventory(ulong? market);
    
    // New raw quantity methods
    Task AddUpdateRaw(ulong itemId, long rawValue, ulong? marketId = null);
    Task<long> GetRaw(ulong itemId, ulong? marketId = null);
    Task AddUpdateFromDisplay(ulong itemId, long displayQuantity, ulong? marketId = null);
    Task<long> GetDisplay(ulong itemId, ulong? marketId = null);
    
    // Local resource tracking methods
    Task AddUpdateFromDisplayLocal(ulong itemId, long displayQuantity, ulong? marketId = null);
    Task<long> GetLocalResourceDisplay(ulong itemId, ulong? marketId = null);
    Task<Dictionary<ulong, long>> GetAllLocalResources(ulong? marketId = null);
    Task CleanLocalResources(ulong? marketId = null);
    
    // Data cleanup methods
    Task CleanAllInventoryData();
    Task SafeCleanInventoryWithBackup();
}
