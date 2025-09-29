using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using MarketBot.Domain.WorldModel;
using MarketBot.Interfaces;
using MarketBot.Services;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Service responsible for calculating and generating resource supply quantities
    /// based on market-specific LAI values and planetary baselines, with warehouse integration.
    /// </summary>
    public class ResourceGenerationService
    {
    private readonly IWarehouseService _inventoryService;
    private readonly IPlanetaryResourceService _planetaryService;
    private readonly ILAIStateService _laiStateService;
    private readonly LAIEngine _laiEngine;
    private readonly ConfigService _configService;
    private readonly ILogger<ResourceGenerationService> _logger;
    private readonly WorldModelMetricsService _metricsService;
    private readonly IMetricsService _coreMetricsService;
    private readonly IGameplayBank _gameplayBank;
    private readonly IWarehouseCapacityService _capacityService;

        // Thread-safe cache for generation events
        private readonly ConcurrentQueue<ResourceGenerationEvent> _generationEvents;
        private readonly object _generationLock = new object();

    public ResourceGenerationService(
        IWarehouseService inventoryService,
        IPlanetaryResourceService planetaryService,
        ILAIStateService laiStateService,
        LAIEngine laiEngine,
        ConfigService configService,
        ILogger<ResourceGenerationService> logger,
        WorldModelMetricsService metricsService,
        IMetricsService coreMetricsService,
        IGameplayBank gameplayBank,
        IWarehouseCapacityService capacityService)
        {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _planetaryService = planetaryService ?? throw new ArgumentNullException(nameof(planetaryService));
        _laiStateService = laiStateService ?? throw new ArgumentNullException(nameof(laiStateService));
        _laiEngine = laiEngine ?? throw new ArgumentNullException(nameof(laiEngine));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _coreMetricsService = coreMetricsService ?? throw new ArgumentNullException(nameof(coreMetricsService));
        _gameplayBank = gameplayBank ?? throw new ArgumentNullException(nameof(gameplayBank));
        _capacityService = capacityService ?? throw new ArgumentNullException(nameof(capacityService));

            _generationEvents = new ConcurrentQueue<ResourceGenerationEvent>();
        }

        /// <summary>
        /// Generate resources for all configured planets based on current LAI values
        /// and planetary baselines.
        /// </summary>
        /// <returns>Total number of resources generated across all planets</returns>
        public async Task<long> GenerateResourcesForAllPlanets()
        {
            _logger.LogDebug("[ENTRY] GenerateResourcesForAllPlanets called");
            
            var configuredPlanets = await _planetaryService.GetConfiguredPlanets();
            _logger.LogDebug("Retrieved {PlanetCount} configured planets for resource generation", configuredPlanets.Count());
            
            long totalGenerated = 0;

            var generationTasks = configuredPlanets.Select(async planetId =>
            {
                try
                {
                    return await GenerateResourcesForPlanet(planetId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate resources for planet {PlanetId}", planetId);
                    return 0L;
                }
            });

            var results = await Task.WhenAll(generationTasks);
            totalGenerated = results.Sum();

            _logger.LogDebug("Generated {TotalGenerated} resources across {PlanetCount} planets",
                totalGenerated, configuredPlanets.Count());
            
            _logger.LogDebug("[EXIT] GenerateResourcesForAllPlanets completed with total: {TotalGenerated}", totalGenerated);

            return totalGenerated;
        }

        /// <summary>
        /// Generate resources for a specific planet based on market-specific LAI values and baselines.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Total resources generated for this planet</returns>
        public async Task<long> GenerateResourcesForPlanet(ulong planetId)
        {
            _logger.LogDebug("[ENTRY] GenerateResourcesForPlanet called for planet {PlanetId}", planetId);

            var resourceBaselines = await _planetaryService.GetAllResourceBaselines(planetId);
            _logger.LogDebug("Retrieved {ResourceCount} resource baselines for planet {PlanetId}", resourceBaselines.Count, planetId);
            
            if (!resourceBaselines.Any())
            {
                _logger.LogDebug("No resource baselines configured for planet {PlanetId}, skipping generation", planetId);
                return 0;
            }

            var marketIds = await _planetaryService.GetMarketIdsForPlanet(planetId);
            _logger.LogDebug("Retrieved {MarketCount} markets for planet {PlanetId}: [{MarketIds}]", 
                marketIds.Count(), planetId, string.Join(", ", marketIds));
            
            if (!marketIds.Any())
            {
                _logger.LogWarning("No markets configured for planet {PlanetId}, cannot generate resources", planetId);
                return 0;
            }

            long planetTotalGenerated = 0;
            var settings = _configService.Config.WorldModel;
            
            _logger.LogDebug("Starting resource generation for planet {PlanetId}: {MarketCount} markets, {ResourceCount} resources", 
                planetId, marketIds.Count(), resourceBaselines.Count);

            // Generate resources for each market directly using market-specific LAI
            foreach (var marketId in marketIds)
            {
                _logger.LogTrace("Processing market {MarketId} for planet {PlanetId}", marketId, planetId);
                foreach (var (resourceId, baseline) in resourceBaselines)
                {
                    try
                    {
                        // Get market-specific LAI value
                        var marketLAI = await _laiStateService.GetLAIValue(planetId, marketId, resourceId);
                        _logger.LogTrace("Retrieved LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {LAI}", 
                            planetId, marketId, resourceId, marketLAI?.ToString("F4") ?? "null");
                        
                        if (!marketLAI.HasValue)
                        {
                            // Initialize if missing
                            marketLAI = _laiEngine.InitializeMarketLAI(planetId, marketId, resourceId, baseline);
                            _logger.LogDebug("Initialized missing LAI for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {LAI:F4}", 
                                planetId, marketId, resourceId, marketLAI.Value);
                            await _laiStateService.SetLAIValue(planetId, marketId, resourceId, marketLAI.Value, TimeSpan.FromHours(24));
                        }

                        // Calculate market-specific generation quantity
                        var generatedQuantity = CalculateMarketGenerationQuantity(planetId, marketId, resourceId, baseline, marketLAI.Value);
                        _logger.LogTrace("Calculated generation quantity for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {Quantity} (baseline: {Baseline:F4}, LAI: {LAI:F4})", 
                            planetId, marketId, resourceId, generatedQuantity, baseline, marketLAI.Value);

                        if (generatedQuantity > 0)
                        {
                            _logger.LogDebug("Attempting to add {Quantity} of resource {ResourceId} to market {MarketId} on planet {PlanetId}", 
                                generatedQuantity, resourceId, marketId, planetId);
                            
                            var actualGenerated = await AddResourceToMarket(
                                planetId, resourceId, marketId, generatedQuantity, baseline, marketLAI.Value);
                            
                            _logger.LogDebug("Actually added {ActualGenerated} of resource {ResourceId} to market {MarketId} on planet {PlanetId}", 
                                actualGenerated, resourceId, marketId, planetId);
                            
                            planetTotalGenerated += actualGenerated;
                        }
                        else
                        {
                            _logger.LogTrace("No resources to generate (quantity = 0) for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                                planetId, marketId, resourceId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to generate resource {ResourceId} for planet {PlanetId}, market {MarketId}", 
                            resourceId, planetId, marketId);
                    }
                }
            }

            // Apply planet-wide generation limits
            if (planetTotalGenerated > settings.GenerationSettings.MaxGenerationPerPlanetPerCycle)
            {
                _logger.LogWarning("Planet {PlanetId} generation {Generated} exceeded limit {Limit}, this indicates configuration issues",
                    planetId, planetTotalGenerated, settings.GenerationSettings.MaxGenerationPerPlanetPerCycle);
            }

            var planetName = await _planetaryService.GetPlanetName(planetId) ?? "Unknown";
            _logger.LogDebug("Generated {Generated} resources for planet {PlanetId} ({PlanetName}) across {MarketCount} markets",
                planetTotalGenerated, planetId, planetName, marketIds.Count());
            
            _logger.LogDebug("[EXIT] GenerateResourcesForPlanet completed for planet {PlanetId} with total: {Generated}", planetId, planetTotalGenerated);

            return planetTotalGenerated;
        }

        /// <summary>
        /// Calculate the quantity of resources to generate for a specific market based on baseline and LAI.
        /// Includes optional market multipliers from configuration.
        /// 
        /// Formula: displayGeneration = baseline * lai * baseRate
        /// Where:
        /// - baseline: Planetary resource abundance tier baseline [0.0, 1.0]
        /// - lai: Market-specific Local Abundance Index (dynamic multiplier)
        /// - baseRate: Global generation scaling factor from configuration
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="baseline">Planetary baseline abundance [0.0, 1.0]</param>
        /// <param name="lai">Market-specific Local Abundance Index</param>
        /// <returns>Display quantity to generate (human-readable units)</returns>
        private long CalculateMarketGenerationQuantity(ulong planetId, ulong marketId, ulong resourceId, double baseline, double lai)
        {
            var settings = _configService.Config.WorldModel.GenerationSettings;
            
            // Base generation = baseline abundance * LAI multiplier * base rate
            double rawGeneration = baseline * lai * settings.BaseRate;
            
            // Apply market-specific multiplier if configured
            var marketKey = $"{planetId}_{marketId}_{resourceId}";
            if (settings.MarketMultipliers.TryGetValue(marketKey, out var marketMultiplier))
            {
                rawGeneration *= marketMultiplier;
                _logger.LogTrace("Applied market multiplier {Multiplier:F2} for planet {PlanetId}, market {MarketId}, resource {ResourceId}", 
                    marketMultiplier, planetId, marketId, resourceId);
            }
            
            // Apply per-resource limit
            long quantity = Math.Min((long)rawGeneration, settings.MaxGenerationPerResourcePerCycle);
            
            // Ensure non-negative
            var finalQuantity = Math.Max(0, quantity);
            return finalQuantity;
        }

        /// <summary>
        /// Add resources to a specific market's inventory.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource type identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="quantity">Quantity to add</param>
        /// <param name="baseline">Planetary baseline for event logging</param>
        /// <param name="lai">Current LAI for event logging</param>
        /// <returns>Actual quantity added</returns>
    private async Task<long> AddResourceToMarket(
        ulong planetId, ulong resourceId, ulong marketId, long quantity,
        double baseline, double lai)
    {
        _logger.LogDebug("[ENTRY] AddResourceToMarket called: planet {PlanetId}, resource {ResourceId}, market {MarketId}, quantity {Quantity}", 
            planetId, resourceId, marketId, quantity);
        
        if (quantity <= 0)
        {
            _logger.LogTrace("AddResourceToMarket: quantity <= 0, returning 0");
            return 0;
        }

        // Check local resource capacity before generation
        if (!await _capacityService.CanGenerateResource(resourceId, quantity, marketId))
        {
            // Get current local quantity and capacity for event logging
            var throttledCurrentQuantity = await _inventoryService.GetLocalResourceDisplay(resourceId, marketId);
            var throttledCapacityLimit = _capacityService.GetLocalResourceCapacityLimit();
            
            _logger.LogDebug("Generation throttled due to local resource capacity for resource {ResourceId} in market {MarketId} (attempted: {Quantity}, current: {Current}, limit: {Limit})", 
                resourceId, marketId, quantity, throttledCurrentQuantity, throttledCapacityLimit);
            
            // Create throttled generation event with capacity information
            var throttledEvent = new ResourceGenerationEvent
            {
                PlanetId = planetId,
                MarketId = marketId,
                ResourceId = resourceId,
                GeneratedQuantity = 0,
                LAIValue = lai,
                PlanetaryBaseline = baseline,
                EffectiveAbundance = baseline * lai,
                BaseGenerationRate = _configService.Config.WorldModel.GenerationSettings.BaseRate,
                IsDryRun = _configService.Config.Development.DryRun,
                Timestamp = DateTime.UtcNow,
                Notes = "Generation throttled - local resource capacity reached",
                IsThrottledByCapacity = true,
                CurrentLocalQuantity = throttledCurrentQuantity,
                CapacityLimit = throttledCapacityLimit
            };
            
            RecordGenerationEvent(throttledEvent);
            
            // Record throttled generation metrics
            _metricsService.RecordThrottledGeneration(_coreMetricsService, planetId, marketId, resourceId);
            
            return 0; // No resources generated
        }

            var isDryRun = _configService.Config.Development.DryRun;
            _logger.LogDebug("AddResourceToMarket: DryRun mode is {DryRun}", isDryRun);

            // Get capacity information for event logging
            var currentLocalQuantity = await _inventoryService.GetLocalResourceDisplay(resourceId, marketId);
            var capacityLimit = _capacityService.GetLocalResourceCapacityLimit();

            // Create generation event for tracking
            var generationEvent = new ResourceGenerationEvent
            {
                PlanetId = planetId,
                MarketId = marketId,
                ResourceId = resourceId,
                GeneratedQuantity = quantity,
                LAIValue = lai,
                PlanetaryBaseline = baseline,
                EffectiveAbundance = baseline * lai,
                BaseGenerationRate = _configService.Config.WorldModel.GenerationSettings.BaseRate,
                IsDryRun = isDryRun,
                Timestamp = DateTime.UtcNow,
                IsThrottledByCapacity = false,
                CurrentLocalQuantity = currentLocalQuantity,
                CapacityLimit = capacityLimit
            };

            if (isDryRun)
            {
                _logger.LogDebug("[DRY-RUN] Would generate {Quantity} of resource {ResourceId} for planet {PlanetId} market {MarketId} (LAI: {LAI:F3})",
                    quantity, resourceId, planetId, marketId, lai);
                
                // In dry-run mode, still update local resource tracking for capacity testing
                // but skip total inventory updates
                try
                {
                    await _inventoryService.AddUpdateFromDisplayLocal(resourceId, quantity, marketId);
                    
                    if (_configService.Config.WorldModel.EnableDetailedLogging)
                    {
                        _logger.LogDebug("[DRY-RUN] Updated local resource tracking for capacity testing: {Quantity} of resource {ResourceId} in market {MarketId}",
                            quantity, resourceId, marketId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DRY-RUN] Failed to update local resource tracking for resource {ResourceId} in market {MarketId}",
                        resourceId, marketId);
                    // Continue in dry-run mode even if local tracking fails
                }
            }
            else
            {
                try
                {
                    // Add to both total inventory and local resource tracking
                    await _inventoryService.AddUpdateFromDisplay(resourceId, quantity, marketId);
                    await _inventoryService.AddUpdateFromDisplayLocal(resourceId, quantity, marketId);
                    
                    if (_configService.Config.WorldModel.EnableDetailedLogging)
                    {
                        _logger.LogDebug("Added {Quantity} display units of resource {ResourceId} to market {MarketId} inventory (LAI: {LAI:F3})",
                            quantity, resourceId, marketId, lai);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add {Quantity} of resource {ResourceId} to market {MarketId} inventory",
                        quantity, resourceId, marketId);
                    
                    generationEvent.Notes = $"Inventory update failed: {ex.Message}";
                    generationEvent.GeneratedQuantity = 0; // Mark as failed
                    RecordGenerationEvent(generationEvent);
                    return 0;
                }
            }

            RecordGenerationEvent(generationEvent);
            
            // Record generation metrics (tracks what WorldModel wants to generate, regardless of DryRun mode)
            _metricsService.RecordResourceGeneration(_coreMetricsService, planetId, marketId, resourceId, quantity);
            
            // Update capacity metrics after successful generation
            await _metricsService.UpdateCapacityMetrics(_coreMetricsService, planetId, marketId, resourceId);
            
            _logger.LogDebug("AddResourceToMarket: Recorded metrics for {Quantity} resources", quantity);
            
            _logger.LogDebug("[EXIT] AddResourceToMarket returning {Quantity}", quantity);
            return quantity;
        }


        /// <summary>
        /// Record a generation event for tracking and analysis.
        /// </summary>
        /// <param name="generationEvent">Event to record</param>
        private void RecordGenerationEvent(ResourceGenerationEvent generationEvent)
        {
            lock (_generationLock)
            {
                _generationEvents.Enqueue(generationEvent);

                // Maintain event history size limit
                var maxEvents = _configService.Config.WorldModel.MaxGenerationEventsHistory;
                while (_generationEvents.Count > maxEvents)
                {
                    if (_generationEvents.TryDequeue(out var _))
                    {
                        // Event removed to maintain limit
                    }
                }
            }

            if (_configService.Config.WorldModel.EnableDetailedLogging)
            {
                _logger.LogTrace("Recorded generation event: {Event}", generationEvent.ToString());
            }
        }

        /// <summary>
        /// Get recent generation events for analysis.
        /// </summary>
        /// <param name="planetId">Optional planet filter</param>
        /// <param name="since">Optional time filter</param>
        /// <returns>Collection of matching generation events</returns>
        public IEnumerable<ResourceGenerationEvent> GetGenerationEvents(ulong? planetId = null, DateTime? since = null)
        {
            lock (_generationLock)
            {
                var events = _generationEvents.ToArray();
                
                if (planetId.HasValue)
                {
                    events = events.Where(e => e.PlanetId == planetId.Value).ToArray();
                }

                if (since.HasValue)
                {
                    events = events.Where(e => e.Timestamp >= since.Value).ToArray();
                }

                return events;
            }
        }

        /// <summary>
        /// Get generation statistics for analysis and debugging.
        /// </summary>
        /// <param name="planetId">Optional planet filter</param>
        /// <param name="hoursBack">Hours of history to analyze</param>
        /// <returns>Formatted statistics string</returns>
        public Task<string> GetGenerationStatistics(ulong? planetId = null, int hoursBack = 24)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-hoursBack);
                var events = GetGenerationEvents(planetId, since).ToList();

                if (!events.Any())
                {
                    return Task.FromResult(planetId.HasValue 
                        ? $"No generation events found for planet {planetId} in last {hoursBack} hours"
                        : $"No generation events found in last {hoursBack} hours");
                }

                var totalGenerated = events.Sum(e => e.GeneratedQuantity);
                var dryRunEvents = events.Count(e => e.IsDryRun);
                var uniqueResources = events.Select(e => e.ResourceId).Distinct().Count();
                var uniquePlanets = events.Select(e => e.PlanetId).Distinct().Count();
                var averageLAI = events.Average(e => e.LAIValue);

                var stats = $"Generation Statistics ({hoursBack}h):\n";
                stats += $"  Events: {events.Count} ({dryRunEvents} dry-run)\n";
                stats += $"  Total Generated: {totalGenerated:N0} resources\n";
                stats += $"  Unique Resources: {uniqueResources}\n";
                stats += $"  Unique Planets: {uniquePlanets}\n";
                stats += $"  Average LAI: {averageLAI:F3}\n";

                if (events.Any())
                {
                    var minLAI = events.Min(e => e.LAIValue);
                    var maxLAI = events.Max(e => e.LAIValue);
                    stats += $"  LAI Range: {minLAI:F3} - {maxLAI:F3}\n";
                }

                return Task.FromResult(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate statistics for planet {PlanetId}", planetId);
                return Task.FromResult($"Statistics generation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear generation event history.
        /// </summary>
        public void ClearEventHistory()
        {
            lock (_generationLock)
            {
                while (_generationEvents.TryDequeue(out var _))
                {
                    // Clear all events
                }
            }

            _logger.LogDebug("Generation event history cleared");
        }

        /// <summary>
        /// Retrieves the last N generation events for diagnostics.
        /// </summary>
        /// <param name="count">Number of recent events to retrieve</param>
        /// <returns>List of recent generation events</returns>
        public List<ResourceGenerationEvent> GetRecentGenerationEvents(int count = 100)
        {
            lock (_generationLock)
            {
                return _generationEvents.Reverse().Take(count).ToList();
            }
        }

        /// <summary>
        /// Logs the current capacity utilization status for all resources in a given market.
        /// </summary>
        /// <param name="marketId">Identifier for the market</param>
        /// <returns>Task</returns>
        public async Task LogCapacityStatusForMarket(ulong marketId)
        {
            // Note: Since we don't have a direct method to get resources by market,
            // we'll need to find which planet this market belongs to first
            var planets = await _planetaryService.GetConfiguredPlanets();
            ulong? targetPlanet = null;
            
            foreach (var planetId in planets)
            {
                var marketIds = await _planetaryService.GetMarketIdsForPlanet(planetId);
                if (marketIds.Contains(marketId))
                {
                    targetPlanet = planetId;
                    break;
                }
            }
            
            if (!targetPlanet.HasValue)
            {
                _logger.LogWarning("Could not find planet for market {MarketId}", marketId);
                return;
            }
            
            var resourceIds = await _planetaryService.GetConfiguredResourcesForPlanet(targetPlanet.Value);
            foreach (var resourceId in resourceIds)
            {
                var utilization = await _capacityService.GetLocalResourceUtilization(resourceId, marketId);
                var capacityLimit = _capacityService.GetLocalResourceCapacityLimit();
                _logger.LogInformation("Market {MarketId} Resource {ResourceId}: Utilization {Utilization:F2} / Limit {CapacityLimit}", marketId, resourceId, utilization, capacityLimit);
            }
        }

        /// <summary>
        /// Get capacity-related statistics for analysis and debugging.
        /// </summary>
        /// <param name="planetId">Optional planet filter</param>
        /// <param name="hoursBack">Hours of history to analyze</param>
        /// <returns>Formatted capacity statistics</returns>
        public string GetCapacityStatistics(ulong? planetId = null, int hoursBack = 24)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-hoursBack);
                var events = GetGenerationEvents(planetId, since).ToList();
                
                if (!events.Any())
                {
                    return planetId.HasValue 
                        ? $"No generation events found for planet {planetId} in last {hoursBack} hours"
                        : $"No generation events found in last {hoursBack} hours";
                }
                
                var throttledEvents = events.Where(e => e.IsThrottledByCapacity).ToList();
                var successfulEvents = events.Where(e => !e.IsThrottledByCapacity && e.GeneratedQuantity > 0).ToList();
                var capacityLimit = _capacityService.GetLocalResourceCapacityLimit();
                
                var stats = $"Capacity Statistics ({hoursBack}h):\n";
                stats += $"  Total Events: {events.Count}\n";
                stats += $"  Successful Generations: {successfulEvents.Count}\n";
                stats += $"  Throttled by Capacity: {throttledEvents.Count} ({(double)throttledEvents.Count / events.Count * 100:F1}%)\n";
                stats += $"  Capacity Limit: {(capacityLimit == -1 ? "Unlimited" : capacityLimit.ToString())}\n";
                
                if (throttledEvents.Any())
                {
                    var throttledByResource = throttledEvents
                        .GroupBy(e => e.ResourceId)
                        .Select(g => new { ResourceId = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(5);
                    
                    stats += "  Most Throttled Resources:\n";
                    foreach (var item in throttledByResource)
                    {
                        stats += $"    Resource {item.ResourceId}: {item.Count} times\n";
                    }
                }
                
                if (successfulEvents.Any())
                {
                    var totalGenerated = successfulEvents.Sum(e => e.GeneratedQuantity);
                    var avgGeneration = successfulEvents.Average(e => e.GeneratedQuantity);
                    stats += $"  Total Generated: {totalGenerated:N0} resources\n";
                    stats += $"  Average per Event: {avgGeneration:F1} resources\n";
                }
                
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate capacity statistics for planet {PlanetId}", planetId);
                return $"Capacity statistics generation failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Get current capacity status for all markets and resources.
        /// </summary>
        /// <param name="planetId">Optional planet filter</param>
        /// <returns>Formatted capacity status</returns>
        public async Task<string> GetCapacityStatus(ulong? planetId = null)
        {
            try
            {
                var capacity = _capacityService.GetLocalResourceCapacityLimit();
                var status = $"Capacity Status:\n";
                status += $"  Global Limit: {(capacity == -1 ? "Unlimited" : capacity.ToString())}\n";
                status += $"  Enabled: {_capacityService.IsCapacityLimitEnabled()}\n\n";
                
                var planets = planetId.HasValue 
                    ? new[] { planetId.Value }
                    : await _planetaryService.GetConfiguredPlanets();
                
                foreach (var pId in planets)
                {
                    var planetName = await _planetaryService.GetPlanetName(pId) ?? "Unknown";
                    var marketIds = await _planetaryService.GetMarketIdsForPlanet(pId);
                    
                    status += $"  Planet {pId} ({planetName}):\n";
                    
                    foreach (var marketId in marketIds)
                    {
                        status += $"    Market {marketId}:\n";
                        var resourceIds = await _planetaryService.GetConfiguredResourcesForPlanet(pId);
                        
                        foreach (var resourceId in resourceIds)
                        {
                            var currentQuantity = await _inventoryService.GetLocalResourceDisplay(resourceId, marketId);
                            var utilization = await _capacityService.GetLocalResourceUtilization(resourceId, marketId);
                            
                            if (capacity == -1)
                            {
                                status += $"      Resource {resourceId}: {currentQuantity} (unlimited)\n";
                            }
                            else
                            {
                                status += $"      Resource {resourceId}: {currentQuantity}/{capacity} ({utilization * 100:F1}%)\n";
                            }
                        }
                    }
                }
                
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get capacity status for planet {PlanetId}", planetId);
                return $"Capacity status retrieval failed: {ex.Message}";
            }
        }
    }
}