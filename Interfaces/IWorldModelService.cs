using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using MarketBot.Domain.WorldModel;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// World Model Service responsible for simulating realistic resource distribution 
    /// across planets and markets using Local Abundance Index (LAI) dynamics.
    /// </summary>
    public interface IWorldModelService : ITickable, IMetricsProvider
    {
        /// <summary>
        /// Initialize the world model service, loading planetary configurations 
        /// and setting up initial LAI values.
        /// </summary>
        /// <returns>Task representing the async initialization</returns>
        Task InitializeAsync();

        /// <summary>
        /// Get the current Local Abundance Index for a specific resource at a market.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>Current LAI value, or null if not initialized</returns>
        Task<double?> GetLocalAbundanceIndex(ulong planetId, ulong resourceId);

        /// <summary>
        /// Update the Local Abundance Index for a resource at a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <param name="newValue">New LAI value to set</param>
        /// <returns>Task representing the async update operation</returns>
        Task UpdateLocalAbundanceIndex(ulong planetId, ulong resourceId, double newValue);

        /// <summary>
        /// Generate resource supply for all configured planets based on current LAI values 
        /// and planetary baselines. Integrates with warehouse inventory system.
        /// </summary>
        /// <returns>Task representing the async generation operation</returns>
        Task GenerateResourceSupply();

        /// <summary>
        /// Get the complete resource profile for a planet, including baselines and current LAI.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Planet resource profile or null if planet not configured</returns>
        Task<PlanetResourceProfile?> GetPlanetProfile(ulong planetId);

        /// <summary>
        /// Get all resource generation events for a specific time period.
        /// Useful for analysis and debugging of supply generation.
        /// </summary>
        /// <param name="planetId">Planet identifier (optional)</param>
        /// <param name="since">Start time for event filter</param>
        /// <returns>Collection of resource generation events</returns>
        Task<IEnumerable<ResourceGenerationEvent>> GetGenerationEvents(ulong? planetId = null, DateTime? since = null);

        /// <summary>
        /// Update all LAI values using Ornstein-Uhlenbeck dynamics.
        /// Called periodically by the tick system.
        /// </summary>
        /// <returns>Task representing the async LAI update operation</returns>
        Task UpdateAllLAIValues();
    }
}