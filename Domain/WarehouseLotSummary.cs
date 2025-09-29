using System.Collections.Generic;
using System.Linq;

namespace MarketBot.Domain
{
    /// <summary>
    /// Summary information about lots for a specific item/market combination.
    /// Used for reporting and cost basis calculations.
    /// </summary>
    public class WarehouseLotSummary
    {
        /// <summary>
        /// Item identifier
        /// </summary>
        public ulong ItemId { get; set; }

        /// <summary>
        /// Market identifier (null for global)
        /// </summary>
        public ulong? MarketId { get; set; }

        /// <summary>
        /// All lots for this item/market
        /// </summary>
        public List<WarehouseLot> Lots { get; set; } = new List<WarehouseLot>();

        /// <summary>
        /// Total quantity across all lots
        /// </summary>
        public long TotalQuantity => Lots.Sum(l => l.Quantity);

        /// <summary>
        /// Total value across all lots
        /// </summary>
        public long TotalValue => Lots.Sum(l => l.TotalValue);

        /// <summary>
        /// Weighted average cost per unit across all lots
        /// </summary>
        public long WeightedAverageCost
        {
            get
            {
                var totalQty = TotalQuantity;
                if (totalQty == 0) return 0;
                return TotalValue / totalQty;
            }
        }

        /// <summary>
        /// Number of lots
        /// </summary>
        public int LotCount => Lots.Count;

        /// <summary>
        /// Lowest unit cost among all lots
        /// </summary>
        public long LowestCost => Lots.Count > 0 ? Lots.Min(l => l.UnitCost) : 0;

        /// <summary>
        /// Highest unit cost among all lots
        /// </summary>
        public long HighestCost => Lots.Count > 0 ? Lots.Max(l => l.UnitCost) : 0;

        /// <summary>
        /// Get lots sorted by unit cost (cheapest first, for FIFO consumption)
        /// </summary>
        public List<WarehouseLot> GetLotsSortedByCost()
        {
            return Lots.OrderBy(l => l.UnitCost).ThenBy(l => l.CreatedAt).ToList();
        }

        public override string ToString()
        {
            return $"Item {ItemId} in Market {MarketId?.ToString() ?? "Global"}: {LotCount} lots, {TotalQuantity} total qty, avg cost {WeightedAverageCost}";
        }
    }
}