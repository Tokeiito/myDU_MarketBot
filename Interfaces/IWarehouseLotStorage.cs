using System.Collections.Generic;
using System.Threading.Tasks;
using MarketBot.Domain;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Interface for warehouse lot storage operations to enable better testability
    /// </summary>
    public interface IWarehouseLotStorage
    {
        /// <summary>
        /// Store a lot in storage
        /// </summary>
        Task<bool> StoreLotAsync(WarehouseLot lot);

        /// <summary>
        /// Retrieve a lot by ID
        /// </summary>
        Task<WarehouseLot?> GetLotAsync(string lotId);

        /// <summary>
        /// Get all lots for an item/market combination
        /// </summary>
        Task<List<WarehouseLot>> GetLotsForItemAsync(ulong itemId, ulong? marketId);

        /// <summary>
        /// Update an existing lot's quantity
        /// </summary>
        Task<bool> UpdateLotQuantityAsync(string lotId, long newQuantity);

        /// <summary>
        /// Delete a lot completely
        /// </summary>
        Task<bool> DeleteLotAsync(WarehouseLot lot);

        /// <summary>
        /// Store item summary data (total quantity, avg cost, etc.)
        /// </summary>
        Task<bool> StoreItemSummaryAsync(WarehouseLotSummary summary);

        /// <summary>
        /// Generate a unique lot ID
        /// </summary>
        string GenerateLotId();

        /// <summary>
        /// Clean up empty lots for an item
        /// </summary>
        Task<int> CleanupEmptyLotsAsync(ulong itemId, ulong? marketId);
    }
}