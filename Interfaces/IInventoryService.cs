using System.Collections.Generic;
using System.Threading.Tasks;

public interface IInventoryService
{
    Task<Dictionary<ulong, string>> GetAll(ulong? marketId);
    Task<long> Get(ulong itemId, ulong? marketId);
    Task AddUpdate(ulong itemId, long value, ulong? marketId);
    Task Delete(ulong itemId, ulong? marketId);
    Task CleanInventory(ulong? market);
}