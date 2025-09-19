using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Service responsible for persisting Local Abundance Index (LAI) values in Redis
    /// with support for atomic updates, expiration policies, and bulk operations.
    /// LAI values are stored per (planet, market, resource) combination.
    /// </summary>
    public interface ILAIStateService
    {
        /// <summary>
        /// Get the LAI value for a specific resource at a specific market on a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>LAI value if found, null if not set</returns>
        Task<double?> GetLAIValue(ulong planetId, ulong marketId, ulong resourceId);

        /// <summary>
        /// Set the LAI value for a specific resource at a specific market on a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <param name="laiValue">LAI value to store</param>
        /// <param name="expiry">Optional expiry time for the value</param>
        /// <returns>Task representing the async operation</returns>
        Task SetLAIValue(ulong planetId, ulong marketId, ulong resourceId, double laiValue, TimeSpan? expiry = null);

        /// <summary>
        /// Get all LAI values for a specific planet across all markets.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Dictionary of (marketId, resourceId) to LAI value</returns>
        Task<Dictionary<(ulong marketId, ulong resourceId), double>> GetAllLAIForPlanet(ulong planetId);

        /// <summary>
        /// Get all LAI values for a specific market on a planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <returns>Dictionary of resource ID to LAI value</returns>
        Task<Dictionary<ulong, double>> GetAllLAIForMarket(ulong planetId, ulong marketId);

        /// <summary>
        /// Set multiple LAI values for a market atomically.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="laiValues">Dictionary of resource ID to LAI value</param>
        /// <param name="expiry">Optional expiry time for all values</param>
        /// <returns>Task representing the async operation</returns>
        Task SetAllLAIForMarket(ulong planetId, ulong marketId, Dictionary<ulong, double> laiValues, TimeSpan? expiry = null);

        /// <summary>
        /// Get LAI values for multiple planets efficiently.
        /// </summary>
        /// <param name="planetIds">Collection of planet identifiers</param>
        /// <returns>Dictionary of planet ID to market+resource LAI values</returns>
        Task<Dictionary<ulong, Dictionary<(ulong marketId, ulong resourceId), double>>> GetLAIForMultiplePlanets(IEnumerable<ulong> planetIds);

        /// <summary>
        /// Get the timestamp when a specific LAI value was last updated.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>Last update timestamp if available, null otherwise</returns>
        Task<DateTime?> GetLastUpdateTime(ulong planetId, ulong marketId, ulong resourceId);

        /// <summary>
        /// Set the last update timestamp for a specific LAI value.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <param name="timestamp">Timestamp to store</param>
        /// <param name="expiry">Optional expiry time for the timestamp</param>
        /// <returns>Task representing the async operation</returns>
        Task SetLastUpdateTime(ulong planetId, ulong marketId, ulong resourceId, DateTime timestamp, TimeSpan? expiry = null);

        /// <summary>
        /// Delete LAI value for a specific resource at a specific market on a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>True if value was deleted, false if it didn't exist</returns>
        Task<bool> DeleteLAIValue(ulong planetId, ulong marketId, ulong resourceId);

        /// <summary>
        /// Delete all LAI values for a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Number of values deleted</returns>
        Task<long> DeleteAllLAIForPlanet(ulong planetId);

        /// <summary>
        /// Delete all LAI values for a specific market on a planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <returns>Number of values deleted</returns>
        Task<long> DeleteAllLAIForMarket(ulong planetId, ulong marketId);

        /// <summary>
        /// Check if LAI value exists for a specific resource at a specific market on a specific planet.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <returns>True if value exists, false otherwise</returns>
        Task<bool> LAIValueExists(ulong planetId, ulong marketId, ulong resourceId);

        /// <summary>
        /// Get all planets that have LAI values stored.
        /// </summary>
        /// <returns>Collection of planet identifiers</returns>
        Task<IEnumerable<ulong>> GetPlanetsWithLAIData();

        /// <summary>
        /// Clear all LAI data from Redis. Use with caution.
        /// </summary>
        /// <returns>Number of keys deleted</returns>
        Task<long> ClearAllLAIData();

        /// <summary>
        /// Get statistics about LAI data storage.
        /// </summary>
        /// <returns>Storage statistics including key counts and memory usage estimates</returns>
        Task<LAIStorageStatistics> GetStorageStatistics();
    }

    /// <summary>
    /// Statistics about LAI data storage in Redis.
    /// </summary>
    public class LAIStorageStatistics
    {
        /// <summary>
        /// Total number of LAI value keys stored
        /// </summary>
        public long TotalLAIKeys { get; set; }

        /// <summary>
        /// Total number of timestamp keys stored
        /// </summary>
        public long TotalTimestampKeys { get; set; }

        /// <summary>
        /// Number of unique planets with LAI data
        /// </summary>
        public int UniquePlanets { get; set; }

        /// <summary>
        /// Estimated memory usage in bytes
        /// </summary>
        public long EstimatedMemoryUsage { get; set; }

        /// <summary>
        /// When these statistics were calculated
        /// </summary>
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Get a formatted string representation of the statistics
        /// </summary>
        /// <returns>Formatted statistics string</returns>
        public override string ToString()
        {
            return $"LAI Storage: {TotalLAIKeys} values, {TotalTimestampKeys} timestamps, " +
                   $"{UniquePlanets} planets, ~{EstimatedMemoryUsage:N0} bytes";
        }
    }
}