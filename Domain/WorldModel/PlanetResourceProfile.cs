using System;
using System.Collections.Generic;

namespace MarketBot.Domain.WorldModel
{
    /// <summary>
    /// Represents the complete resource profile for a planet, including baseline abundances
    /// and current Local Abundance Index (LAI) values for all resources.
    /// </summary>
    public class PlanetResourceProfile
    {
        /// <summary>
        /// Unique identifier for the planet
        /// </summary>
        public ulong PlanetId { get; set; }

        /// <summary>
        /// Human-readable name of the planet
        /// </summary>
        public string PlanetName { get; set; } = string.Empty;

        /// <summary>
        /// Baseline resource abundance values for this planet.
        /// Key: Resource ID, Value: Baseline abundance [0.0, 1.0]
        /// </summary>
        public Dictionary<ulong, double> BaselineAbundance { get; set; } = new Dictionary<ulong, double>();

        /// <summary>
        /// Current Local Abundance Index values for each resource.
        /// Key: Resource ID, Value: Current LAI multiplier [0.1, 2.0]
        /// </summary>
        public Dictionary<ulong, double> CurrentLAI { get; set; } = new Dictionary<ulong, double>();

        /// <summary>
        /// Timestamp of the last LAI update for this planet
        /// </summary>
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Regional variance factor for this planet, affecting LAI volatility.
        /// Higher values create more dramatic abundance swings.
        /// </summary>
        public double RegionalVariance { get; set; } = 0.1;

        /// <summary>
        /// Seasonal strength factor for this planet.
        /// Controls how much seasonal cycles affect resource abundance.
        /// </summary>
        public double SeasonalStrength { get; set; } = 0.05;

        /// <summary>
        /// Get the effective abundance for a resource (baseline * LAI).
        /// </summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <returns>Effective abundance, or 0 if resource not found</returns>
        public double GetEffectiveAbundance(ulong resourceId)
        {
            if (BaselineAbundance.TryGetValue(resourceId, out var baseline) &&
                CurrentLAI.TryGetValue(resourceId, out var lai))
            {
                return baseline * lai;
            }
            return 0.0;
        }

        /// <summary>
        /// Check if this planet has configuration for the specified resource.
        /// </summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <returns>True if resource is configured for this planet</returns>
        public bool HasResource(ulong resourceId)
        {
            return BaselineAbundance.ContainsKey(resourceId);
        }

        /// <summary>
        /// Get all configured resource IDs for this planet.
        /// </summary>
        /// <returns>Collection of resource identifiers</returns>
        public IEnumerable<ulong> GetConfiguredResources()
        {
            return BaselineAbundance.Keys;
        }
    }
}