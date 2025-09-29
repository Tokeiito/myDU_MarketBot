using System;
using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Service for emitting warehouse events for telemetry and downstream system notifications
    /// </summary>
    public interface IWarehouseEventService
    {
        /// <summary>
        /// Emit event when items are added to warehouse
        /// </summary>
        Task EmitItemsAddedAsync(ulong itemId, long quantity, long unitCost, ulong? marketId, DateTime? timestamp = null);

        /// <summary>
        /// Emit event when items are consumed from warehouse
        /// </summary>
        Task EmitItemsConsumedAsync(ulong itemId, long consumedQuantity, long weightedAverageCost, ulong? marketId, DateTime? timestamp = null);

        /// <summary>
        /// Emit event when lots are merged during consolidation
        /// </summary>
        Task EmitLotMergedAsync(string oldLotId1, string oldLotId2, string newLotId, long newUnitCost, ulong itemId, ulong? marketId, DateTime? timestamp = null);

        /// <summary>
        /// Emit event when a new lot is created
        /// </summary>
        Task EmitLotCreatedAsync(string lotId, ulong itemId, long unitCost, ulong? marketId, DateTime? timestamp = null);
    }
}