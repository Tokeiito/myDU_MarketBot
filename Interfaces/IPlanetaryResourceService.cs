using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Service responsible for providing baseline resource abundance data for planets.
    /// Integrates with configuration system and existing recipe/resource type systems.
    /// </summary>
    public interface IPlanetaryResourceService
    {
        /// <summary>
        /// Get the baseline abundance for a specific resource on a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>Baseline abundance [0.0, 1.0], or null if not configured</returns>
        Task<double?> GetPlanetResourceBaseline(ulong planetId, ulong resourceId);

        /// <summary>
        /// Get all configured resource baselines for a planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Dictionary of resource ID to baseline abundance</returns>
        Task<Dictionary<ulong, double>> GetAllResourceBaselines(ulong planetId);

        /// <summary>
        /// Get all configured planets that have resource baselines defined.
        /// </summary>
        /// <returns>Collection of planet identifiers</returns>
        Task<IEnumerable<ulong>> GetConfiguredPlanets();

        /// <summary>
        /// Get all resources configured for a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Collection of resource identifiers</returns>
        Task<IEnumerable<ulong>> GetConfiguredResourcesForPlanet(ulong planetId);

        /// <summary>
        /// Check if a planet has configuration for the specified resource.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <returns>True if resource is configured for this planet</returns>
        Task<bool> HasResourceConfiguration(ulong planetId, ulong resourceId);

        /// <summary>
        /// Get the default baseline abundance for a resource tier when no specific 
        /// configuration is available for a planet-resource combination.
        /// </summary>
        /// <param name="tier">Resource tier (0-5)</param>
        /// <returns>Default baseline abundance for the tier</returns>
        double GetDefaultBaselineForTier(int tier);

        /// <summary>
        /// Get planet name for a given planet ID, if available in configuration.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Planet name or null if not configured</returns>
        Task<string?> GetPlanetName(ulong planetId);

        /// <summary>
        /// Get market IDs associated with a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Collection of market identifiers for the planet</returns>
        Task<IEnumerable<ulong>> GetMarketIdsForPlanet(ulong planetId);

        /// <summary>
        /// Reload configuration data from the configuration source.
        /// Useful for runtime configuration updates.
        /// </summary>
        /// <returns>Task representing the reload operation</returns>
        Task ReloadConfiguration();
    }
}