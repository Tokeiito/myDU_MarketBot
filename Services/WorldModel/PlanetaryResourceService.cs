using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Domain.WorldModel;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Service providing baseline resource abundance data per planet.
    /// Integrates with configuration system and existing recipe/resource type systems.
    /// </summary>
    public class PlanetaryResourceService : IPlanetaryResourceService
    {
        private readonly IRecipeService _recipeService;
        private readonly ConfigService _configService;
        private readonly ILogger<PlanetaryResourceService> _logger;
        private readonly IPlanetService _planetService;
        private readonly IResourceDefinitionService _resourceDefinitionService;

        // Default baseline abundances for each tier when no specific configuration exists
        private static readonly Dictionary<int, double> DefaultTierBaselines = new Dictionary<int, double>
        {
            { 0, 0.8 },  // T0 (Basic ores) - Most abundant
            { 1, 0.6 },  // T1 (Pure ores) - Common
            { 2, 0.4 },  // T2 (Products) - Less common  
            { 3, 0.3 },  // T3 (Products) - Uncommon
            { 4, 0.2 },  // T4 (Products) - Rare
            { 5, 0.1 }   // T5 (Products) - Very rare
        };

        public PlanetaryResourceService(
            IRecipeService recipeService,
            ConfigService configService,
            ILogger<PlanetaryResourceService> logger,
            IPlanetService planetService,
            IResourceDefinitionService resourceDefinitionService)
        {
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _planetService = planetService ?? throw new ArgumentNullException(nameof(planetService));
            _resourceDefinitionService = resourceDefinitionService ?? throw new ArgumentNullException(nameof(resourceDefinitionService));
        }

        public async Task<double?> GetPlanetResourceBaseline(ulong planetId, ulong resourceId)
        {
            var worldModelSettings = _configService.Config.WorldModel;
            
            // First try to get from calculated baselines (which uses real planet ores)
            var allBaselines = await GetAllResourceBaselines(planetId);
            if (allBaselines.TryGetValue(resourceId, out var calculatedBaseline))
            {
                _logger.LogDebug("Retrieved calculated baseline {Baseline:F3} for resource {ResourceId} on planet {PlanetId}",
                    calculatedBaseline, resourceId, planetId);
                return calculatedBaseline;
            }

            // Fallback: derive from resource tier and planet characteristics
            try
            {
                var tier = await _recipeService.GetTier(resourceId);
                var defaultBaseline = GetDefaultBaselineForTier(tier);

                _logger.LogDebug("Using default baseline {DefaultBaseline:F3} for tier {Tier} resource {ResourceId} on planet {PlanetId}",
                    defaultBaseline, tier, resourceId, planetId);

                return defaultBaseline;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine tier for resource {ResourceId}, returning null baseline", resourceId);
                return null;
            }
        }

        public async Task<Dictionary<ulong, double>> GetAllResourceBaselines(ulong planetId)
        {
            var result = new Dictionary<ulong, double>();
            
            try
            {
                // Get planet data
                var planet = await _planetService.GetPlanetByIdAsync(planetId);
                if (planet?.Ores == null || !planet.Ores.Any())
                {
                    _logger.LogWarning("No ores found for planet {PlanetId}", planetId);
                    return result;
                }
                
                // Get tier baselines from config
                var tierBaselines = GetTierBaselines(planetId);
                if (!tierBaselines.Any())
                {
                    _logger.LogWarning("No tier baselines configured for planet {PlanetId}", planetId);
                    return result;
                }
                
                // Get all ore resource definitions
                var ores = await _resourceDefinitionService.GetOresAsync();
                var oreByStrId = ores.ToDictionary(ore => ore.StrId, ore => ore);
                
                // Map planet ores to resource IDs with tier baselines
                foreach (var oreStrId in planet.Ores)
                {
                    if (oreByStrId.TryGetValue(oreStrId, out var resource))
                    {
                        if (tierBaselines.TryGetValue(resource.Tier, out var baseline))
                        {
                            result[resource.Id] = baseline;
                            _logger.LogTrace("Mapped ore {OreStrId} (tier {Tier}) to resource ID {ResourceId} with baseline {Baseline:F3}",
                                oreStrId, resource.Tier, resource.Id, baseline);
                        }
                        else
                        {
                            _logger.LogDebug("No baseline configured for tier {Tier} of ore {OreStrId} on planet {PlanetId}",
                                resource.Tier, oreStrId, planetId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unknown ore {OreStrId} found on planet {PlanetId}", oreStrId, planetId);
                    }
                }
                
                _logger.LogDebug("Mapped {OreCount} planet ores to {ResourceCount} resource baselines for planet {PlanetId}",
                    planet.Ores.Count, result.Count, planetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get resource baselines for planet {PlanetId}", planetId);
            }
            
            return result;
        }

        public async Task<IEnumerable<ulong>> GetConfiguredPlanets()
        {
            var worldModelSettings = _configService.Config.WorldModel;
            var planetIds = worldModelSettings.PlanetConfigs.Keys.ToList();

            _logger.LogDebug("Found {PlanetCount} configured planets", planetIds.Count);

            return await Task.FromResult(planetIds);
        }

        public async Task<IEnumerable<ulong>> GetConfiguredResourcesForPlanet(ulong planetId)
        {
            var baselines = await GetAllResourceBaselines(planetId);
            var resourceIds = baselines.Keys.ToList();
            
            _logger.LogDebug("Found {ResourceCount} actual resources for planet {PlanetId}", 
                resourceIds.Count, planetId);
                
            return resourceIds;
        }

        public async Task<bool> HasResourceConfiguration(ulong planetId, ulong resourceId)
        {
            var baselines = await GetAllResourceBaselines(planetId);
            bool hasConfig = baselines.ContainsKey(resourceId);

            _logger.LogDebug("Planet {PlanetId} has resource {ResourceId} configuration: {HasConfig}",
                planetId, resourceId, hasConfig);

            return hasConfig;
        }

        public double GetDefaultBaselineForTier(int tier)
        {
            if (DefaultTierBaselines.TryGetValue(tier, out var baseline))
            {
                _logger.LogDebug("Using default baseline {Baseline:F3} for tier {Tier}", baseline, tier);
                return baseline;
            }

            // Fallback for unknown tiers - use T5 (lowest)
            const double fallbackBaseline = 0.1;
            _logger.LogWarning("Unknown tier {Tier}, using fallback baseline {Baseline:F3}", tier, fallbackBaseline);
            return fallbackBaseline;
        }

        /// <summary>
        /// Get tier baselines from configuration for the specified planet.
        /// Uses planet-specific ResourceTierBaselines if configured, otherwise falls back to 
        /// DefaultResourceTierBaselines from WorldModel settings.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Dictionary mapping tier numbers to baseline abundance values</returns>
        private Dictionary<int, double> GetTierBaselines(ulong planetId)
        {
            var worldModelSettings = _configService.Config.WorldModel;
            
            // Try to get planet-specific tier baselines first
            if (worldModelSettings.PlanetConfigs.TryGetValue(planetId, out var planetConfig) &&
                planetConfig.ResourceTierBaselines != null && planetConfig.ResourceTierBaselines.Any())
            {
                _logger.LogDebug("Using planet-specific tier baselines for planet {PlanetId}", planetId);
                return planetConfig.ResourceTierBaselines.ToDictionary(
                    kvp => (int)kvp.Key, // Convert ulong key to int for tier number
                    kvp => kvp.Value);
            }
            
            // Fall back to default tier baselines
            _logger.LogWarning("Planet {PlanetId} using default tier baselines - consider adding ResourceTierBaselines to configuration for this planet", planetId);
            return worldModelSettings.DefaultResourceTierBaselines.ToDictionary(
                kvp => (int)kvp.Key, // Convert ulong key to int for tier number
                kvp => kvp.Value);
        }

        public async Task<string?> GetPlanetName(ulong planetId)
        {
            var worldModelSettings = _configService.Config.WorldModel;
            
            if (worldModelSettings.PlanetConfigs.TryGetValue(planetId, out var planetConfig))
            {
                var planetName = string.IsNullOrWhiteSpace(planetConfig.Name) ? null : planetConfig.Name;
                
                _logger.LogDebug("Planet {PlanetId} name: {PlanetName}", planetId, planetName ?? "null");
                
                return await Task.FromResult(planetName);
            }

            _logger.LogDebug("No configuration found for planet {PlanetId}", planetId);
            return await Task.FromResult<string?>(null);
        }

        public async Task<IEnumerable<ulong>> GetMarketIdsForPlanet(ulong planetId)
        {
            var worldModelSettings = _configService.Config.WorldModel;
            
            if (worldModelSettings.PlanetConfigs.TryGetValue(planetId, out var planetConfig))
            {
                var marketIds = planetConfig.MarketIds.ToList();
                
                _logger.LogDebug("Found {MarketCount} markets for planet {PlanetId}: [{MarketIds}]",
                    marketIds.Count, planetId, string.Join(", ", marketIds));

                return await Task.FromResult(marketIds);
            }

            _logger.LogDebug("No markets configured for planet {PlanetId}", planetId);
            return await Task.FromResult(Enumerable.Empty<ulong>());
        }

        public async Task ReloadConfiguration()
        {
            try
            {
                // The configuration service loads from the source automatically
                // This method exists for potential future caching invalidation
                var worldModelSettings = _configService.Config.WorldModel;
                int planetCount = worldModelSettings.PlanetConfigs.Count;

                _logger.LogDebug("Configuration reloaded successfully. {PlanetCount} planets configured", planetCount);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload planetary resource configuration");
                throw;
            }
        }

        /// <summary>
        /// Get comprehensive planet statistics for debugging and analysis
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Formatted statistics string</returns>
        public async Task<string> GetPlanetStatistics(ulong planetId)
        {
            try
            {
                var planetName = await GetPlanetName(planetId) ?? "Unknown";
                var resourceBaselines = await GetAllResourceBaselines(planetId);
                var marketIds = await GetMarketIdsForPlanet(planetId);

                var stats = $"Planet {planetId} ({planetName}):\n";
                stats += $"  Resources: {resourceBaselines.Count}\n";
                stats += $"  Markets: {marketIds.Count()} [{string.Join(", ", marketIds)}]\n";

                if (resourceBaselines.Any())
                {
                    var avgBaseline = resourceBaselines.Values.Average();
                    var minBaseline = resourceBaselines.Values.Min();
                    var maxBaseline = resourceBaselines.Values.Max();
                    
                    stats += $"  Baseline Abundance - Avg: {avgBaseline:F3}, Min: {minBaseline:F3}, Max: {maxBaseline:F3}\n";
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate statistics for planet {PlanetId}", planetId);
                return $"Planet {planetId}: Statistics unavailable due to error";
            }
        }

        /// <summary>
        /// Validate planet configuration for consistency and completeness
        /// </summary>
        /// <param name="planetId">Planet identifier to validate</param>
        /// <returns>Validation results with any issues found</returns>
        public Task<(bool IsValid, List<string> Issues)> ValidatePlanetConfiguration(ulong planetId)
        {
            var issues = new List<string>();
            bool isValid = true;

            try
            {
                var worldModelSettings = _configService.Config.WorldModel;

                if (!worldModelSettings.PlanetConfigs.TryGetValue(planetId, out var planetConfig))
                {
                    issues.Add($"Planet {planetId} is not configured in world model settings");
                    return Task.FromResult((false, issues));
                }

                // Check tier baselines (optional - defaults will be used if not configured)
                if (planetConfig.ResourceTierBaselines != null && planetConfig.ResourceTierBaselines.Any())
                {
                    // Validate baseline values are within expected range
                    foreach (var kvp in planetConfig.ResourceTierBaselines)
                    {
                        if (kvp.Value < 0.0 || kvp.Value > 1.0)
                        {
                            issues.Add($"Planet {planetId} tier {kvp.Key} baseline {kvp.Value:F3} is outside valid range [0.0, 1.0]");
                            isValid = false;
                        }
                    }
                }
                else
                {
                    // This is just informational - not an error
                    issues.Add($"Planet {planetId} will use default tier baselines (no ResourceTierBaselines configured)");
                }

                // Check if planet has any associated markets
                if (!planetConfig.MarketIds.Any())
                {
                    issues.Add($"Planet {planetId} has no markets configured - resources cannot be generated");
                    isValid = false;
                }

                // Validate configuration parameters
                if (planetConfig.RegionalVariance < 0.0 || planetConfig.RegionalVariance > 1.0)
                {
                    issues.Add($"Planet {planetId} regional variance {planetConfig.RegionalVariance:F3} is outside recommended range [0.0, 1.0]");
                    // This is a warning, not a critical error
                }

                if (Math.Abs(planetConfig.SeasonalStrength) > 0.5)
                {
                    issues.Add($"Planet {planetId} seasonal strength {planetConfig.SeasonalStrength:F3} is very high (>0.5), may cause extreme abundance swings");
                    // This is a warning, not a critical error
                }

                _logger.LogDebug("Planet {PlanetId} configuration validation: {IsValid}, {IssueCount} issues",
                    planetId, isValid, issues.Count);

                return Task.FromResult((isValid, issues));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate configuration for planet {PlanetId}", planetId);
                issues.Add($"Configuration validation failed due to exception: {ex.Message}");
                return Task.FromResult((false, issues));
            }
        }
    }
}