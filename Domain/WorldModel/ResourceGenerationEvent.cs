using System;

namespace MarketBot.Domain.WorldModel
{
    /// <summary>
    /// Represents a single resource generation event, tracking when and how much 
    /// of a resource was generated at a specific planet/market location.
    /// </summary>
    public class ResourceGenerationEvent
    {
        /// <summary>
        /// Unique identifier for this generation event
        /// </summary>
        public Guid EventId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Planet where the resource generation occurred
        /// </summary>
        public ulong PlanetId { get; set; }

        /// <summary>
        /// Market ID where the resources were added (may be null for planet-wide generation)
        /// </summary>
        public ulong? MarketId { get; set; }

        /// <summary>
        /// Type of resource that was generated
        /// </summary>
        public ulong ResourceId { get; set; }

        /// <summary>
        /// Quantity of resources generated in this event
        /// </summary>
        public long GeneratedQuantity { get; set; }

        /// <summary>
        /// Local Abundance Index value at the time of generation
        /// </summary>
        public double LAIValue { get; set; }

        /// <summary>
        /// Planetary baseline abundance used in the calculation
        /// </summary>
        public double PlanetaryBaseline { get; set; }

        /// <summary>
        /// Effective abundance value (baseline * LAI) used for generation
        /// </summary>
        public double EffectiveAbundance { get; set; }

        /// <summary>
        /// Timestamp when this generation event occurred
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Base generation rate used in the calculation
        /// </summary>
        public double BaseGenerationRate { get; set; }


        /// <summary>
        /// Additional notes or context about this generation event (optional)
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Whether this was a dry-run generation (logged but not actually applied)
        /// </summary>
        public bool IsDryRun { get; set; } = false;

        /// <summary>
        /// Whether generation was throttled due to local resource capacity limits
        /// </summary>
        public bool IsThrottledByCapacity { get; set; } = false;

        /// <summary>
        /// Current local resource quantity at time of generation attempt (display units)
        /// </summary>
        public long CurrentLocalQuantity { get; set; } = 0;

        /// <summary>
        /// Capacity limit that was applied during generation attempt (-1 = unlimited)
        /// </summary>
        public long CapacityLimit { get; set; } = -1;

        /// <summary>
        /// Calculate the generation efficiency (actual vs theoretical maximum)
        /// </summary>
        /// <param name="maxPossibleGeneration">Maximum possible generation for comparison</param>
        /// <returns>Efficiency ratio [0.0, 1.0]</returns>
        public double CalculateEfficiency(long maxPossibleGeneration)
        {
            if (maxPossibleGeneration <= 0) return 0.0;
            return Math.Min(1.0, (double)GeneratedQuantity / maxPossibleGeneration);
        }

        /// <summary>
        /// Get a human-readable summary of this generation event
        /// </summary>
        /// <returns>Formatted string describing the event</returns>
        public override string ToString()
        {
            var dryRunPrefix = IsDryRun ? "[DRY-RUN] " : "";
            var marketInfo = MarketId.HasValue ? $" at market {MarketId}" : "";
            var capacityInfo = IsThrottledByCapacity ? $" [THROTTLED - {CurrentLocalQuantity}/{CapacityLimit}]" : "";
            
            return $"{dryRunPrefix}Generated {GeneratedQuantity} of resource {ResourceId} on planet {PlanetId}{marketInfo} " +
                   $"(LAI: {LAIValue:F3}, Baseline: {PlanetaryBaseline:F3}){capacityInfo} at {Timestamp:yyyy-MM-dd HH:mm:ss}";
        }
    }
}