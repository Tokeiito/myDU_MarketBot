using System;

namespace MarketBot.Domain
{
    /// <summary>
    /// Base class for all warehouse events
    /// </summary>
    public abstract class WarehouseEvent
    {
        /// <summary>
        /// Timestamp when the event occurred
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Type of warehouse event
        /// </summary>
        public abstract string EventType { get; }

        /// <summary>
        /// Item involved in the event
        /// </summary>
        public ulong ItemId { get; set; }

        /// <summary>
        /// Market where the event occurred (null for global)
        /// </summary>
        public ulong? MarketId { get; set; }
    }

    /// <summary>
    /// Event emitted when items are added to warehouse
    /// </summary>
    public class ItemsAddedEvent : WarehouseEvent
    {
        public override string EventType => "items_added";

        /// <summary>
        /// Quantity of items added
        /// </summary>
        public long Quantity { get; set; }

        /// <summary>
        /// Unit cost of added items
        /// </summary>
        public long UnitCost { get; set; }
    }

    /// <summary>
    /// Event emitted when items are consumed from warehouse
    /// </summary>
    public class ItemsConsumedEvent : WarehouseEvent
    {
        public override string EventType => "items_consumed";

        /// <summary>
        /// Quantity of items consumed
        /// </summary>
        public long ConsumedQuantity { get; set; }

        /// <summary>
        /// Weighted average cost of consumed items
        /// </summary>
        public long WeightedAverageCost { get; set; }
    }

    /// <summary>
    /// Event emitted when lots are merged during consolidation
    /// </summary>
    public class LotMergedEvent : WarehouseEvent
    {
        public override string EventType => "lot_merged";

        /// <summary>
        /// ID of first lot that was merged
        /// </summary>
        public string OldLotId1 { get; set; } = string.Empty;

        /// <summary>
        /// ID of second lot that was merged
        /// </summary>
        public string OldLotId2 { get; set; } = string.Empty;

        /// <summary>
        /// ID of the new merged lot
        /// </summary>
        public string NewLotId { get; set; } = string.Empty;

        /// <summary>
        /// Unit cost of the new merged lot
        /// </summary>
        public long NewUnitCost { get; set; }
    }

    /// <summary>
    /// Event emitted when a new lot is created
    /// </summary>
    public class LotCreatedEvent : WarehouseEvent
    {
        public override string EventType => "lot_created";

        /// <summary>
        /// ID of the created lot
        /// </summary>
        public string LotId { get; set; } = string.Empty;

        /// <summary>
        /// Unit cost of the created lot
        /// </summary>
        public long UnitCost { get; set; }
    }
}