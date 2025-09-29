using System;

namespace MarketBot.Domain
{
    /// <summary>
    /// Represents a batch of identical items with the same acquisition cost.
    /// Lots are the fundamental unit of inventory cost tracking in the warehouse system.
    /// </summary>
    public class WarehouseLot
    {
        /// <summary>
        /// Unique identifier for this lot
        /// </summary>
        public string LotId { get; set; } = string.Empty;

        /// <summary>
        /// Item type identifier (e.g., iron ore, steel, etc.)
        /// </summary>
        public ulong ItemId { get; set; }

        /// <summary>
        /// Market where this lot is located
        /// </summary>
        public ulong? MarketId { get; set; }

        /// <summary>
        /// Raw quantity of items in this lot.
        /// For materials, this is volume * 2^24 (fixed-point representation).
        /// For non-materials, this equals the item count.
        /// </summary>
        public long Quantity { get; set; }

        /// <summary>
        /// Cost per unit in raw quantity terms.
        /// This is the acquisition cost recorded when items were added to warehouse.
        /// Unit cost corresponds to the same unit scale as Quantity (raw units).
        /// </summary>
        public long UnitCost { get; set; }

        /// <summary>
        /// When this lot was created
        /// </summary>
        public DateTime CreatedAt { get; set; }


        /// <summary>
        /// Calculate total value of this lot (quantity * unit_cost)
        /// </summary>
        public long TotalValue => Quantity * UnitCost;

        /// <summary>
        /// Check if lot is empty (no quantity remaining)
        /// </summary>
        public bool IsEmpty => Quantity <= 0;

        public WarehouseLot()
        {
            CreatedAt = DateTime.UtcNow;
        }

        public WarehouseLot(string lotId, ulong itemId, ulong? marketId, long quantity, long unitCost)
        {
            LotId = lotId ?? throw new ArgumentNullException(nameof(lotId));
            ItemId = itemId;
            MarketId = marketId;
            Quantity = quantity;
            UnitCost = unitCost;
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Create a copy of this lot with specified quantity.
        /// Useful for splitting lots during consumption.
        /// </summary>
        public WarehouseLot CreateCopy(string newLotId, long newQuantity)
        {
            return new WarehouseLot(newLotId, ItemId, MarketId, newQuantity, UnitCost)
            {
                CreatedAt = this.CreatedAt // Preserve original creation time for split lots
            };
        }

        public override string ToString()
        {
            return $"Lot {LotId}: Item {ItemId}, Qty {Quantity}, Cost {UnitCost}";
        }
    }
}