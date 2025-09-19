using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Redis-based implementation of LAI state persistence with atomic updates,
    /// expiration policies, and bulk operations following existing Redis patterns.
    /// </summary>
    public class LAIStateService : ILAIStateService
    {
        private readonly IDatabase _redisDatabase;
        private readonly ILogger<LAIStateService> _logger;

        // Redis key prefixes following existing patterns
        private const string LAI_KEY_PREFIX = "worldmodel:lai";
        private const string TIMESTAMP_KEY_PREFIX = "worldmodel:lai:timestamp";
        private const string PLANET_SET_KEY = "worldmodel:planets";

        public LAIStateService(IDatabase redisDatabase, ILogger<LAIStateService> logger)
        {
            _redisDatabase = redisDatabase ?? throw new ArgumentNullException(nameof(redisDatabase));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<double?> GetLAIValue(ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                string key = GetLAIKey(planetId, marketId, resourceId);
                var value = await _redisDatabase.StringGetAsync(key);

                if (value.HasValue && value.TryParse(out double lai))
                {
                    _logger.LogTrace("Retrieved LAI value {LAI:F4} for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                        lai, planetId, marketId, resourceId);
                    return lai;
                }

                _logger.LogTrace("No LAI value found for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task SetLAIValue(ulong planetId, ulong marketId, ulong resourceId, double laiValue, TimeSpan? expiry = null)
        {
            try
            {
                string laiKey = GetLAIKey(planetId, marketId, resourceId);
                string timestampKey = GetTimestampKey(planetId, marketId, resourceId);
                
                // Use a transaction to set both LAI value and timestamp atomically
                var transaction = _redisDatabase.CreateTransaction();
                
                // Set LAI value
                var setLAITask = transaction.StringSetAsync(laiKey, laiValue, expiry);
                
                // Set timestamp
                var setTimestampTask = transaction.StringSetAsync(timestampKey, DateTime.UtcNow.Ticks, expiry);
                
                // Add planet to the set of planets with LAI data
                var addPlanetTask = transaction.SetAddAsync(PLANET_SET_KEY, planetId);

                bool executed = await transaction.ExecuteAsync();
                
                if (executed)
                {
                    _logger.LogTrace("Set LAI value {LAI:F4} for planet {PlanetId}, market {MarketId}, resource {ResourceId} with expiry {Expiry}", 
                        laiValue, planetId, marketId, resourceId, expiry?.ToString() ?? "none");
                }
                else
                {
                    _logger.LogWarning("Failed to execute LAI value transaction for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                        planetId, marketId, resourceId);
                    throw new InvalidOperationException("Redis transaction failed to execute");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set LAI value {LAI:F4} for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    laiValue, planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task<Dictionary<(ulong marketId, ulong resourceId), double>> GetAllLAIForPlanet(ulong planetId)
        {
            try
            {
                string keyPattern = GetPlanetLAIPattern(planetId);
                var result = new Dictionary<(ulong marketId, ulong resourceId), double>();

                // Get all keys matching the planet pattern
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                var keys = server.Keys(pattern: keyPattern).ToList();

                if (!keys.Any())
                {
                    _logger.LogTrace("No LAI keys found for planet {PlanetId}", planetId);
                    return result;
                }

                // Batch get all values
                var keyValues = await _redisDatabase.StringGetAsync(keys.ToArray());

                for (int i = 0; i < keys.Count; i++)
                {
                    if (keyValues[i].HasValue && keyValues[i].TryParse(out double lai))
                    {
                        if (TryExtractMarketAndResourceIdFromKey(keys[i], out ulong marketId, out ulong resourceId))
                        {
                            result[(marketId, resourceId)] = lai;
                        }
                    }
                }

                _logger.LogDebug("Retrieved {Count} LAI values for planet {PlanetId}", result.Count, planetId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all LAI values for planet {PlanetId}", planetId);
                throw;
            }
        }

        public async Task<Dictionary<ulong, double>> GetAllLAIForMarket(ulong planetId, ulong marketId)
        {
            try
            {
                string keyPattern = GetMarketLAIPattern(planetId, marketId);
                var result = new Dictionary<ulong, double>();

                // Get all keys matching the market pattern
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                var keys = server.Keys(pattern: keyPattern).ToList();

                if (!keys.Any())
                {
                    _logger.LogTrace("No LAI keys found for planet {PlanetId}, market {MarketId}", planetId, marketId);
                    return result;
                }

                // Batch get all values
                var keyValues = await _redisDatabase.StringGetAsync(keys.ToArray());

                for (int i = 0; i < keys.Count; i++)
                {
                    if (keyValues[i].HasValue && keyValues[i].TryParse(out double lai))
                    {
                        if (TryExtractResourceIdFromMarketKey(keys[i], out ulong resourceId))
                        {
                            result[resourceId] = lai;
                        }
                    }
                }

                _logger.LogDebug("Retrieved {Count} LAI values for planet {PlanetId}, market {MarketId}", result.Count, planetId, marketId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all LAI values for planet {PlanetId}, market {MarketId}", planetId, marketId);
                throw;
            }
        }

        public async Task SetAllLAIForMarket(ulong planetId, ulong marketId, Dictionary<ulong, double> laiValues, TimeSpan? expiry = null)
        {
            if (!laiValues?.Any() == true)
            {
                _logger.LogDebug("No LAI values to set for planet {PlanetId}, market {MarketId}", planetId, marketId);
                return;
            }

            try
            {
                var transaction = _redisDatabase.CreateTransaction();
                var tasks = new List<Task>();

                var now = DateTime.UtcNow.Ticks;

                foreach (var kvp in laiValues)
                {
                    var resourceId = kvp.Key;
                    var laiValue = kvp.Value;

                    string laiKey = GetLAIKey(planetId, marketId, resourceId);
                    string timestampKey = GetTimestampKey(planetId, marketId, resourceId);

                    tasks.Add(transaction.StringSetAsync(laiKey, laiValue, expiry));
                    tasks.Add(transaction.StringSetAsync(timestampKey, now, expiry));
                }

                // Add planet to the set
                tasks.Add(transaction.SetAddAsync(PLANET_SET_KEY, planetId));

                bool executed = await transaction.ExecuteAsync();

                if (executed)
                {
                    _logger.LogDebug("Set {Count} LAI values for planet {PlanetId}, market {MarketId} with expiry {Expiry}", 
                        laiValues.Count, planetId, marketId, expiry?.ToString() ?? "none");
                }
                else
                {
                    _logger.LogWarning("Failed to execute bulk LAI transaction for planet {PlanetId}, market {MarketId}", planetId, marketId);
                    throw new InvalidOperationException("Redis bulk transaction failed to execute");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set {Count} LAI values for planet {PlanetId}, market {MarketId}", 
                    laiValues?.Count ?? 0, planetId, marketId);
                throw;
            }
        }

        public async Task<Dictionary<ulong, Dictionary<(ulong marketId, ulong resourceId), double>>> GetLAIForMultiplePlanets(IEnumerable<ulong> planetIds)
        {
            try
            {
                var planets = planetIds.ToList();
                var result = new Dictionary<ulong, Dictionary<(ulong marketId, ulong resourceId), double>>();

                if (!planets.Any())
                {
                    return result;
                }

                var tasks = planets.Select(async planetId =>
                {
                    try
                    {
                        var laiValues = await GetAllLAIForPlanet(planetId);
                        return new { PlanetId = planetId, LAIValues = laiValues };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get LAI values for planet {PlanetId} in bulk operation", planetId);
                        return new { PlanetId = planetId, LAIValues = new Dictionary<(ulong marketId, ulong resourceId), double>() };
                    }
                });

                var results = await Task.WhenAll(tasks);

                foreach (var planetResult in results)
                {
                    result[planetResult.PlanetId] = planetResult.LAIValues;
                }

                _logger.LogDebug("Retrieved LAI values for {PlanetCount} planets", planets.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get LAI values for multiple planets");
                throw;
            }
        }

        public async Task<DateTime?> GetLastUpdateTime(ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                string key = GetTimestampKey(planetId, marketId, resourceId);
                var value = await _redisDatabase.StringGetAsync(key);

                if (value.HasValue && value.TryParse(out long ticks))
                {
                    var timestamp = new DateTime(ticks);
                    _logger.LogTrace("Retrieved update time {Timestamp} for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                        timestamp, planetId, marketId, resourceId);
                    return timestamp;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get last update time for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task SetLastUpdateTime(ulong planetId, ulong marketId, ulong resourceId, DateTime timestamp, TimeSpan? expiry = null)
        {
            try
            {
                string key = GetTimestampKey(planetId, marketId, resourceId);
                await _redisDatabase.StringSetAsync(key, timestamp.Ticks, expiry);

                _logger.LogTrace("Set update time {Timestamp} for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    timestamp, planetId, marketId, resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set last update time for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task<bool> DeleteLAIValue(ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                string laiKey = GetLAIKey(planetId, marketId, resourceId);
                string timestampKey = GetTimestampKey(planetId, marketId, resourceId);

                var transaction = _redisDatabase.CreateTransaction();
                var deleteLAITask = transaction.KeyDeleteAsync(laiKey);
                var deleteTimestampTask = transaction.KeyDeleteAsync(timestampKey);

                bool executed = await transaction.ExecuteAsync();

                if (executed)
                {
                    // Check if this planet has any remaining LAI data
                    var remainingValues = await GetAllLAIForPlanet(planetId);
                    if (!remainingValues.Any())
                    {
                        // Remove planet from the set if no LAI data remains
                        await _redisDatabase.SetRemoveAsync(PLANET_SET_KEY, planetId);
                    }

                    _logger.LogDebug("Deleted LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                        planetId, marketId, resourceId);
                    return true;
                }

                _logger.LogWarning("Failed to delete LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task<long> DeleteAllLAIForPlanet(ulong planetId)
        {
            try
            {
                string keyPattern = GetPlanetLAIPattern(planetId);
                string timestampPattern = GetPlanetTimestampPattern(planetId);

                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                var laiKeys = server.Keys(pattern: keyPattern).ToList();
                var timestampKeys = server.Keys(pattern: timestampPattern).ToList();

                var allKeys = laiKeys.Concat(timestampKeys).ToArray();
                
                if (!allKeys.Any())
                {
                    _logger.LogDebug("No LAI data found to delete for planet {PlanetId}", planetId);
                    return 0;
                }

                long deletedCount = await _redisDatabase.KeyDeleteAsync(allKeys);

                // Remove planet from the set
                await _redisDatabase.SetRemoveAsync(PLANET_SET_KEY, planetId);

                _logger.LogInformation("Deleted {DeletedCount} LAI keys for planet {PlanetId}", deletedCount, planetId);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete all LAI data for planet {PlanetId}", planetId);
                throw;
            }
        }

        public async Task<long> DeleteAllLAIForMarket(ulong planetId, ulong marketId)
        {
            try
            {
                string keyPattern = GetMarketLAIPattern(planetId, marketId);
                string timestampPattern = GetMarketTimestampPattern(planetId, marketId);

                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                var laiKeys = server.Keys(pattern: keyPattern).ToList();
                var timestampKeys = server.Keys(pattern: timestampPattern).ToList();

                var allKeys = laiKeys.Concat(timestampKeys).ToArray();
                
                if (!allKeys.Any())
                {
                    _logger.LogDebug("No LAI data found to delete for planet {PlanetId}, market {MarketId}", planetId, marketId);
                    return 0;
                }

                long deletedCount = await _redisDatabase.KeyDeleteAsync(allKeys);

                _logger.LogInformation("Deleted {DeletedCount} LAI keys for planet {PlanetId}, market {MarketId}", deletedCount, planetId, marketId);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete all LAI data for planet {PlanetId}, market {MarketId}", planetId, marketId);
                throw;
            }
        }

        public async Task<bool> LAIValueExists(ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                string key = GetLAIKey(planetId, marketId, resourceId);
                bool exists = await _redisDatabase.KeyExistsAsync(key);

                _logger.LogTrace("LAI value exists check for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {Exists}", 
                    planetId, marketId, resourceId, exists);
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check LAI value existence for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    planetId, marketId, resourceId);
                throw;
            }
        }

        public async Task<IEnumerable<ulong>> GetPlanetsWithLAIData()
        {
            try
            {
                var planetValues = await _redisDatabase.SetMembersAsync(PLANET_SET_KEY);
                var planets = planetValues.Select(v => (ulong)v).ToList();

                _logger.LogDebug("Found {PlanetCount} planets with LAI data", planets.Count);
                return planets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get planets with LAI data");
                throw;
            }
        }

        public async Task<long> ClearAllLAIData()
        {
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                var laiKeys = server.Keys(pattern: $"{LAI_KEY_PREFIX}:*").ToList();
                var timestampKeys = server.Keys(pattern: $"{TIMESTAMP_KEY_PREFIX}:*").ToList();

                var allKeys = laiKeys.Concat(timestampKeys).ToArray();

                long deletedCount = 0;
                if (allKeys.Any())
                {
                    deletedCount = await _redisDatabase.KeyDeleteAsync(allKeys);
                }

                // Clear the planet set
                await _redisDatabase.KeyDeleteAsync(PLANET_SET_KEY);

                _logger.LogWarning("Cleared all LAI data: {DeletedCount} keys deleted", deletedCount);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear all LAI data");
                throw;
            }
        }

        public async Task<LAIStorageStatistics> GetStorageStatistics()
        {
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
                
                var laiKeys = server.Keys(pattern: $"{LAI_KEY_PREFIX}:*").ToList();
                var timestampKeys = server.Keys(pattern: $"{TIMESTAMP_KEY_PREFIX}:*").ToList();
                var planets = await GetPlanetsWithLAIData();

                // Rough memory estimation (key + value overhead)
                long estimatedMemory = (laiKeys.Count + timestampKeys.Count) * 64; // Rough estimate per key-value pair

                var statistics = new LAIStorageStatistics
                {
                    TotalLAIKeys = laiKeys.Count,
                    TotalTimestampKeys = timestampKeys.Count,
                    UniquePlanets = planets.Count(),
                    EstimatedMemoryUsage = estimatedMemory
                };

                _logger.LogDebug("LAI storage statistics: {Statistics}", statistics.ToString());
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get LAI storage statistics");
                throw;
            }
        }

        #region Private Helper Methods

        private static string GetLAIKey(ulong planetId, ulong marketId, ulong resourceId)
        {
            return $"{LAI_KEY_PREFIX}:{planetId}:{marketId}:{resourceId}";
        }

        private static string GetTimestampKey(ulong planetId, ulong marketId, ulong resourceId)
        {
            return $"{TIMESTAMP_KEY_PREFIX}:{planetId}:{marketId}:{resourceId}";
        }

        private static string GetPlanetLAIPattern(ulong planetId)
        {
            return $"{LAI_KEY_PREFIX}:{planetId}:*";
        }

        private static string GetPlanetTimestampPattern(ulong planetId)
        {
            return $"{TIMESTAMP_KEY_PREFIX}:{planetId}:*";
        }

        private static string GetMarketLAIPattern(ulong planetId, ulong marketId)
        {
            return $"{LAI_KEY_PREFIX}:{planetId}:{marketId}:*";
        }

        private static string GetMarketTimestampPattern(ulong planetId, ulong marketId)
        {
            return $"{TIMESTAMP_KEY_PREFIX}:{planetId}:{marketId}:*";
        }

        private static bool TryExtractMarketAndResourceIdFromKey(string key, out ulong marketId, out ulong resourceId)
        {
            marketId = 0;
            resourceId = 0;
            try
            {
                // Key format: worldmodel:lai:planetId:marketId:resourceId
                var parts = key.Split(':');
                if (parts.Length >= 5 && 
                    ulong.TryParse(parts[3], out marketId) && 
                    ulong.TryParse(parts[4], out resourceId))
                {
                    return true;
                }
            }
            catch
            {
                // Parsing failed
            }
            return false;
        }

        private static bool TryExtractResourceIdFromMarketKey(string key, out ulong resourceId)
        {
            resourceId = 0;
            try
            {
                // Key format: worldmodel:lai:planetId:marketId:resourceId
                var parts = key.Split(':');
                if (parts.Length >= 5 && ulong.TryParse(parts[4], out resourceId))
                {
                    return true;
                }
            }
            catch
            {
                // Parsing failed
            }
            return false;
        }

        #endregion
    }
}