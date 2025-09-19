using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketBot.Domain;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace MarketBot.Services
{
    /// <summary>
    /// Read-only, in-memory cached (TTL = process lifetime) planet data loader.
    /// Retrieves planet information from the construct table where base_id IS NULL.
    /// </summary>
    public class PlanetService : IPlanetService
    {
        private readonly IPostgresReadService _db;
        private readonly ILogger<PlanetService> _logger;

        private volatile Task _loadTask;
        private readonly Dictionary<ulong, Planet> _planetsCache = new Dictionary<ulong, Planet>();
        private readonly object _lockObject = new object();

        public PlanetService(IPostgresReadService db, ILogger<PlanetService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Planet?> GetPlanetByIdAsync(ulong planetId, CancellationToken ct = default)
        {
            await EnsureLoadedAsync(ct);

            lock (_lockObject)
            {
                return _planetsCache.TryGetValue(planetId, out var planet) ? planet : null;
            }
        }

        private Task EnsureLoadedAsync(CancellationToken ct)
        {
            var task = _loadTask;
            if (task != null) return task;

            lock (_lockObject)
            {
                if (_loadTask == null)
                {
                    _loadTask = LoadAsync(ct);
                }
                return _loadTask;
            }
        }

        private async Task LoadAsync(CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Loading planet data from database...");

                // Query all constructs where base_id IS NULL (planets and asteroids)
                const string sql = @"
                    SELECT id, name, json_properties 
                    FROM public.construct 
                    WHERE base_id IS NULL 
                    AND json_properties IS NOT NULL 
                    AND json_properties->'planetProperties' IS NOT NULL
                    ORDER BY id;";

                var rows = await _db.QueryAsync(sql, r => new
                {
                    Id = (ulong)r.GetInt64(0),
                    Name = r.GetString(1),
                    JsonProperties = r.GetString(2)
                }, ct);

                var loadedPlanets = new Dictionary<ulong, Planet>();

                foreach (var row in rows)
                {
                    try
                    {
                        var planet = ParsePlanet(row.Id, row.Name, row.JsonProperties);
                        if (planet != null)
                        {
                            loadedPlanets[planet.Id] = planet;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse planet data for {PlanetId} '{PlanetName}'", row.Id, row.Name);
                    }
                }

                lock (_lockObject)
                {
                    _planetsCache.Clear();
                    foreach (var kvp in loadedPlanets)
                    {
                        _planetsCache[kvp.Key] = kvp.Value;
                    }
                }

                _logger.LogInformation("Loaded {PlanetCount} planets into in-memory cache", loadedPlanets.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load planet data from database");
                throw;
            }
        }

        private Planet? ParsePlanet(ulong id, string name, string jsonPropertiesString)
        {
            try
            {
                var jsonProperties = JObject.Parse(jsonPropertiesString);
                var planetProperties = jsonProperties["planetProperties"];
                
                if (planetProperties == null)
                {
                    return null;
                }

                // Check if planetProperties is actually an object, not a primitive value
                if (planetProperties.Type != JTokenType.Object)
                {
                    // This record is not a valid planet, skip it
                    return null;
                }

                var oresToken = planetProperties["ores"];
                var ores = new List<string>();

                if (oresToken != null && oresToken.Type == JTokenType.Array)
                {
                    ores = oresToken.ToObject<List<string>>() ?? new List<string>();
                }

                return new Planet
                {
                    Id = id,
                    Name = name,
                    Ores = ores
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON properties for planet {PlanetId} '{PlanetName}'", id, name);
                return null;
            }
        }
    }
}