using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using MarketBot.Domain.WorldModel;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Main World Model service that coordinates all world simulation components.
    /// Implements IWorldModelService interface with tick-based updates and metrics.
    /// </summary>
    public class WorldModelService : IWorldModelService
    {
        private readonly ILogger<WorldModelService> _logger;
        private readonly ILAIStateService _laiStateService;
        private readonly IPlanetaryResourceService _planetaryResourceService;
        private readonly LAIEngine _laiEngine;
        private readonly ResourceGenerationService _resourceGenerationService;
        private readonly WorldModelMetricsService _metricsService;
        private readonly IMetricsService _coreMetricsService;
        private readonly ConfigService _configService;

        private int _tickCounter = 0;
        private readonly object _tickLock = new object();

        /// <summary>
        /// Initializes a new instance of WorldModelService with required dependencies.
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="laiStateService">LAI state persistence service</param>
        /// <param name="planetaryResourceService">Planetary resource baseline service</param>
        /// <param name="laiEngine">LAI calculation engine</param>
        /// <param name="resourceGenerationService">Resource generation service</param>
        /// <param name="metricsService">World model metrics service</param>
        /// <param name="coreMetricsService">Core metrics service for incrementing counters</param>
        /// <param name="configService">Configuration service</param>
        public WorldModelService(
            ILogger<WorldModelService> logger,
            ILAIStateService laiStateService,
            IPlanetaryResourceService planetaryResourceService,
            LAIEngine laiEngine,
            ResourceGenerationService resourceGenerationService,
            WorldModelMetricsService metricsService,
            IMetricsService coreMetricsService,
            ConfigService configService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _laiStateService = laiStateService ?? throw new ArgumentNullException(nameof(laiStateService));
            _planetaryResourceService = planetaryResourceService ?? throw new ArgumentNullException(nameof(planetaryResourceService));
            _laiEngine = laiEngine ?? throw new ArgumentNullException(nameof(laiEngine));
            _resourceGenerationService = resourceGenerationService ?? throw new ArgumentNullException(nameof(resourceGenerationService));
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
            _coreMetricsService = coreMetricsService ?? throw new ArgumentNullException(nameof(coreMetricsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            _logger.LogInformation("WorldModelService initialized with all dependencies");
        }

        #region IWorldModelService Implementation

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation("Initializing World Model Service...");
                
                // Initialize LAI values for configured planets and markets
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                foreach (var planetId in planets)
                {
                    var planetBaselines = await _planetaryResourceService.GetAllResourceBaselines(planetId);
                    var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                    
                    foreach (var marketId in marketIds)
                    {
                        foreach (var kvp in planetBaselines)
                        {
                            var resourceId = kvp.Key;
                            var baseline = kvp.Value;
                            var existingLAI = await _laiStateService.GetLAIValue(planetId, marketId, resourceId);
                            if (!existingLAI.HasValue)
                            {
                                // Initialize LAI with market-specific variation
                                var initialLAI = _laiEngine.InitializeMarketLAI(planetId, marketId, resourceId, baseline);
                                await _laiStateService.SetLAIValue(planetId, marketId, resourceId, initialLAI,
                                    TimeSpan.FromHours(24)); // Default 24 hour expiry
                                    
                                // Update LAI by market metrics for initialization
                                _metricsService.UpdateLAIByMarketMetric(_coreMetricsService, planetId, resourceId, initialLAI);
                            }
                        }
                    }
                }
                
                _logger.LogInformation("World Model Service initialized successfully for {PlanetCount} planets", planets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize World Model Service");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<double?> GetLocalAbundanceIndex(ulong planetId, ulong resourceId)
        {
            try
            {
                // Get the first market on the planet to get LAI value
                // TODO: This interface should be updated to include marketId parameter
                var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                if (!marketIds.Any())
                {
                    _logger.LogWarning("No markets found for planet {PlanetId}", planetId);
                    return null;
                }
                
                var marketId = marketIds.First();
                return await _laiStateService.GetLAIValue(planetId, marketId, resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get LAI for planet {PlanetId}, resource {ResourceId}", planetId, resourceId);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task UpdateLocalAbundanceIndex(ulong planetId, ulong resourceId, double newValue)
        {
            try
            {
                // Update LAI for all markets on the planet
                var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                foreach (var marketId in marketIds)
                {
                    await _laiStateService.SetLAIValue(planetId, marketId, resourceId, newValue,
                        TimeSpan.FromHours(24)); // Default 24 hour expiry
                }
                    
                // Update LAI by market metrics
                _metricsService.UpdateLAIByMarketMetric(_coreMetricsService, planetId, resourceId, newValue);
                    
                _logger.LogTrace("Updated LAI for planet {PlanetId}, resource {ResourceId}: {NewLAI:F4}", 
                    planetId, resourceId, newValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update LAI for planet {PlanetId}, resource {ResourceId}: {NewValue:F4}", 
                    planetId, resourceId, newValue);
            }
        }

        /// <inheritdoc />
        public async Task GenerateResourceSupply()
        {
            _logger.LogDebug("[ENTRY] WorldModelService.GenerateResourceSupply called");
            
            try
            {
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                _logger.LogDebug("WorldModelService: Retrieved {PlanetCount} configured planets for resource supply generation", planets.Count());
                
                foreach (var planetId in planets)
                {
                    _logger.LogDebug("WorldModelService: Calling ResourceGenerationService.GenerateResourcesForPlanet for planet {PlanetId}", planetId);
                    var generated = await _resourceGenerationService.GenerateResourcesForPlanet(planetId);
                    _logger.LogDebug("WorldModelService: ResourceGenerationService returned {Generated} resources for planet {PlanetId}", generated, planetId);
                }
                
                _logger.LogDebug("Generated resource supply for {PlanetCount} planets", planets.Count());
                _logger.LogDebug("[EXIT] WorldModelService.GenerateResourceSupply completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate resource supply");
                throw; // Re-throw to ensure calling code sees the failure
            }
        }

        /// <inheritdoc />
        public async Task<PlanetResourceProfile?> GetPlanetProfile(ulong planetId)
        {
            try
            {
                var baselines = await _planetaryResourceService.GetAllResourceBaselines(planetId);
                if (!baselines.Any())
                {
                    return null;
                }

                var allLaiValues = await _laiStateService.GetAllLAIForPlanet(planetId);
                
                // Aggregate LAI values by resource (average across markets)
                var aggregatedLAI = new Dictionary<ulong, double>();
                var resourceGroups = allLaiValues.GroupBy(kvp => kvp.Key.resourceId);
                
                foreach (var resourceGroup in resourceGroups)
                {
                    var avgLAI = resourceGroup.Average(kvp => kvp.Value);
                    aggregatedLAI[resourceGroup.Key] = avgLAI;
                }
                
                return new PlanetResourceProfile
                {
                    PlanetId = planetId,
                    BaselineAbundance = baselines,
                    CurrentLAI = aggregatedLAI
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get planet profile for planet {PlanetId}", planetId);
                return null;
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<ResourceGenerationEvent>> GetGenerationEvents(ulong? planetId = null, DateTime? since = null)
        {
            try
            {
                var events = _resourceGenerationService.GetGenerationEvents(planetId, since);
                return Task.FromResult(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get generation events for planet {PlanetId}", planetId);
                return Task.FromResult(Enumerable.Empty<ResourceGenerationEvent>());
            }
        }

        /// <inheritdoc />
        public async Task UpdateAllLAIValues()
        {
            try
            {
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                var worldModelSettings = _configService.Config.WorldModel;
                
                // Calculate delta time from LAI update interval (LAIUpdateTicks * 1 second per tick)
                var laiUpdateIntervalSeconds = worldModelSettings.LAIUpdateTicks * 1.0;
                
                var updateTasks = planets.Select(async planetId =>
                {
                    try
                    {
                        await UpdatePlanetLAI(planetId, laiUpdateIntervalSeconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update LAI for planet {PlanetId}", planetId);
                        // Don't rethrow - let other planets continue processing
                    }
                });

                await Task.WhenAll(updateTasks);

                // Increment world model update cycles metric
                _metricsService.IncrementUpdateCycles(_coreMetricsService);

                _logger.LogDebug("LAI update cycle completed for {PlanetCount} planets (deltaTime: {DeltaTime}s) - incremented worldmodel_update_cycles_total metric", 
                    planets.Count(), laiUpdateIntervalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update all LAI values");
            }
        }
        
        /// <summary>
        /// Update LAI for all markets and resources on a specific planet using the conservation-based LAI engine.
        /// Enhanced with comprehensive error handling and graceful degradation.
        /// </summary>
        private async Task UpdatePlanetLAI(ulong planetId, double deltaTime)
        {
            Dictionary<(ulong marketId, ulong resourceId), double> currentLAIValues = null;
            Dictionary<ulong, double> planetBaselines = null;
            
            try
            {
                // Get all current LAI values for this planet with error handling
                try
                {
                    currentLAIValues = await _laiStateService.GetAllLAIForPlanet(planetId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve current LAI values for planet {PlanetId} - aborting update", planetId);
                    return;
                }
                
                if (currentLAIValues == null || !currentLAIValues.Any())
                {
                    _logger.LogDebug("No existing LAI values found for planet {PlanetId}, skipping update", planetId);
                    return;
                }

                // Get planet baselines with error handling
                try
                {
                    planetBaselines = await _planetaryResourceService.GetAllResourceBaselines(planetId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to retrieve planet baselines for planet {PlanetId} - continuing with fallback", planetId);
                    
                    // Create fallback baselines from configured defaults
                    planetBaselines = CreateFallbackBaselines(planetId, currentLAIValues);
                }
                
                if (planetBaselines == null || !planetBaselines.Any())
                {
                    _logger.LogWarning("No resource baselines configured for planet {PlanetId}, creating fallback baselines", planetId);
                    planetBaselines = CreateFallbackBaselines(planetId, currentLAIValues);
                }

                // Use the conservation-based LAI engine for proper updates with error handling
                Dictionary<(ulong marketId, ulong resourceId), double> updatedLAIValues;
                try
                {
                    updatedLAIValues = _laiEngine.UpdateAllLAIForPlanetWithConservation(
                        planetId, 
                        currentLAIValues, 
                        planetBaselines, 
                        deltaTime);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LAI engine failed to calculate updates for planet {PlanetId} - skipping update", planetId);
                    return;
                }

                if (updatedLAIValues == null || updatedLAIValues.Count == 0)
                {
                    _logger.LogWarning("LAI engine returned no updated values for planet {PlanetId}", planetId);
                    return;
                }

                // Save updated LAI values back to storage with retry logic
                int saveFailures = 0;
                var saveTasks = updatedLAIValues.Select(async kvp =>
                {
                    const int maxRetries = 3;
                    int retryCount = 0;
                    
                    while (retryCount < maxRetries)
                    {
                        try
                        {
                            var (marketId, resourceId) = kvp.Key;
                            var newLAI = kvp.Value;
                            
                            // Validate LAI value before saving
                            if (double.IsNaN(newLAI) || double.IsInfinity(newLAI))
                            {
                                _logger.LogWarning("Invalid LAI value ({LAI}) for planet {PlanetId}, market {MarketId}, resource {ResourceId} - skipping save",
                                    newLAI, planetId, marketId, resourceId);
                                Interlocked.Increment(ref saveFailures);
                                return;
                            }
                            
                            await _laiStateService.SetLAIValue(planetId, marketId, resourceId, newLAI, TimeSpan.FromHours(24));
                            return; // Success - exit retry loop
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            if (retryCount >= maxRetries)
                            {
                                _logger.LogWarning(ex, "Failed to save LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId} after {Retries} retries", 
                                    planetId, kvp.Key.marketId, kvp.Key.resourceId, maxRetries);
                                Interlocked.Increment(ref saveFailures);
                            }
                            else
                            {
                                _logger.LogDebug(ex, "Retrying LAI save for planet {PlanetId}, market {MarketId}, resource {ResourceId} (attempt {Retry}/{MaxRetries})",
                                    planetId, kvp.Key.marketId, kvp.Key.resourceId, retryCount, maxRetries);
                                await Task.Delay(100 * retryCount); // Exponential backoff
                            }
                        }
                    }
                });

                await Task.WhenAll(saveTasks);
                
                // Report save failures if any
                if (saveFailures > 0)
                {
                    _logger.LogWarning("{SaveFailures} LAI values failed to save for planet {PlanetId} (out of {TotalValues})", 
                        saveFailures, planetId, updatedLAIValues.Count);
                }

                // Update metrics with error handling - calculate average LAI per resource across markets
                try
                {
                    var resourceGroups = updatedLAIValues.GroupBy(kvp => kvp.Key.resourceId);
                    foreach (var resourceGroup in resourceGroups)
                    {
                        try
                        {
                            var avgLAI = resourceGroup.Average(kvp => kvp.Value);
                            var resourceId = resourceGroup.Key;
                            
                            // Validate average before updating metrics
                            if (double.IsNaN(avgLAI) || double.IsInfinity(avgLAI))
                            {
                                _logger.LogWarning("Invalid average LAI ({AvgLAI}) calculated for resource {ResourceId} on planet {PlanetId} - skipping metrics update",
                                    avgLAI, resourceId, planetId);
                                continue;
                            }
                            
                            // Update LAI by market metrics
                            _metricsService.UpdateLAIByMarketMetric(_coreMetricsService, planetId, resourceId, avgLAI);
                            
                            // Update LAI health monitoring metrics
                            _metricsService.UpdateLAIHealthMetrics(_coreMetricsService, planetId, resourceId, avgLAI);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update metrics for resource {ResourceId} on planet {PlanetId}",
                                resourceGroup.Key, planetId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update metrics for planet {PlanetId}", planetId);
                }

                _logger.LogDebug("Updated {Count} LAI values for planet {PlanetId} using conservation engine with deltaTime {DeltaTime}s", 
                    updatedLAIValues.Count, planetId, deltaTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update LAI for planet {PlanetId} - LAI values preserved", planetId);
                // Don't re-throw - let other planets continue processing
                // The caller already handles per-planet failures gracefully
            }
        }
        
        /// <summary>
        /// Create fallback baselines when planet configuration is missing or corrupted.
        /// Uses default resource tier baselines from configuration.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="currentLAIValues">Current LAI values to determine resource types</param>
        /// <returns>Dictionary of fallback baseline values</returns>
        private Dictionary<ulong, double> CreateFallbackBaselines(ulong planetId, 
            Dictionary<(ulong marketId, ulong resourceId), double> currentLAIValues)
        {
            var fallbackBaselines = new Dictionary<ulong, double>();
            var defaultBaselines = _configService.Config.WorldModel.DefaultResourceTierBaselines;
            
            // Extract unique resource IDs from current LAI values
            var resourceIds = currentLAIValues.Select(kvp => kvp.Key.resourceId).Distinct();
            
            foreach (var resourceId in resourceIds)
            {
                // Try to determine resource tier from resource ID (this is application-specific)
                // For now, use tier 1 as fallback
                var tier = DetermineResourceTier(resourceId);
                
                if (defaultBaselines.TryGetValue(tier, out var baseline))
                {
                    fallbackBaselines[resourceId] = baseline;
                }
                else
                {
                    // Ultimate fallback - use middle value
                    fallbackBaselines[resourceId] = 1.0;
                    _logger.LogWarning("No default baseline found for tier {Tier}, resource {ResourceId} on planet {PlanetId} - using 1.0",
                        tier, resourceId, planetId);
                }
            }
            
            _logger.LogDebug("Created {Count} fallback baselines for planet {PlanetId}", 
                fallbackBaselines.Count, planetId);
                
            return fallbackBaselines;
        }
        
        /// <summary>
        /// Determine resource tier from resource ID.
        /// This is a simplified heuristic - in a real system, this would come from
        /// a resource definition database or configuration.
        /// </summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <returns>Resource tier (1-5)</returns>
        private ulong DetermineResourceTier(ulong resourceId)
        {
            // Simple heuristic based on resource ID ranges
            // This should be replaced with actual resource tier lookup
            if (resourceId < 1000000000) return 1;
            if (resourceId < 2000000000) return 2;
            if (resourceId < 3000000000) return 3;
            if (resourceId < 4000000000) return 4;
            return 5;
        }

        #endregion

        #region ITickable Implementation

        /// <inheritdoc />
        public void Tick()
        {
            // Use lock to prevent concurrent tick processing
            if (!Monitor.TryEnter(_tickLock))
            {
                _logger.LogWarning("WorldModel tick skipped - previous tick still processing");
                return;
            }

            try
            {
                var startTime = DateTime.UtcNow;
                _tickCounter++;

                // Process tick asynchronously but don't wait (fire and forget for performance)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("WorldModel: Starting async ProcessTick for tick {TickCount}", _tickCounter);
                        await ProcessTick();
                        _logger.LogDebug("WorldModel: Completed async ProcessTick for tick {TickCount}", _tickCounter);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during WorldModel tick processing at tick {TickCount}", _tickCounter);
                    }
                });
            }
            finally
            {
                Monitor.Exit(_tickLock);
            }
        }

        /// <summary>
        /// Process a single tick cycle with LAI updates and resource generation.
        /// Enhanced with performance monitoring and graceful error handling.
        /// </summary>
        private async Task ProcessTick()
        {
            var startTime = DateTime.UtcNow;
            var worldModelSettings = _configService.Config.WorldModel;
            
            try
            {
                // Get tick intervals from configuration with fallback defaults
                var laiUpdateTicks = Math.Max(1, worldModelSettings.LAIUpdateTicks); // Ensure minimum 1
                var resourceGenerationTicks = Math.Max(1, worldModelSettings.ResourceGenerationTicks); // Ensure minimum 1
                
                // Update LAI values at configured interval with performance monitoring
                if (_tickCounter % laiUpdateTicks == 0)
                {
                    _logger.LogInformation("WorldModel tick {TickCount}: Triggering LAI update cycle (every {LAITicks} ticks)", 
                        _tickCounter, laiUpdateTicks);
                    
                    var laiStartTime = DateTime.UtcNow;
                    try
                    {
                        await UpdateAllLAIValues();
                        var laiDuration = DateTime.UtcNow - laiStartTime;
                        
                        if (laiDuration.TotalSeconds > 30) // Log if LAI update takes longer than 30 seconds
                        {
                            _logger.LogWarning("LAI update cycle took {Duration:F2} seconds at tick {TickCount} - consider performance optimization",
                                laiDuration.TotalSeconds, _tickCounter);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "LAI update cycle failed at tick {TickCount} - continuing with other operations", _tickCounter);
                    }
                }
                else if (_tickCounter % Math.Max(1, laiUpdateTicks / 6) == 0) // Log progress at 1/6 intervals
                {
                    _logger.LogDebug("WorldModel tick {TickCount}: Next LAI update at tick {NextUpdate}", 
                        _tickCounter, (_tickCounter / laiUpdateTicks + 1) * laiUpdateTicks);
                }

                // Generate resources at configured interval with performance monitoring
                if (_tickCounter % resourceGenerationTicks == 0)
                {
                    _logger.LogInformation("WorldModel tick {TickCount}: Triggering resource generation (every {GenTicks} ticks)", 
                        _tickCounter, resourceGenerationTicks);
                    
                    var genStartTime = DateTime.UtcNow;
                    try
                    {
                        await GenerateResourceSupply();
                        var genDuration = DateTime.UtcNow - genStartTime;
                        
                        if (genDuration.TotalSeconds > 10) // Log if resource generation takes longer than 10 seconds
                        {
                            _logger.LogWarning("Resource generation took {Duration:F2} seconds at tick {TickCount} - consider performance optimization",
                                genDuration.TotalSeconds, _tickCounter);
                        }
                        
                        _logger.LogDebug("WorldModel tick {TickCount}: Resource generation completed successfully in {Duration:F2}s", 
                            _tickCounter, genDuration.TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "WorldModel tick {TickCount}: Resource generation failed", _tickCounter);
                    }
                }
                
                var totalDuration = DateTime.UtcNow - startTime;
                
                if (totalDuration.TotalSeconds > 60) // Log if entire tick takes longer than 1 minute
                {
                    _logger.LogWarning("WorldModel tick {TickCount} took {Duration:F2} seconds - this may impact system performance",
                        _tickCounter, totalDuration.TotalSeconds);
                }

                _logger.LogTrace("WorldModel tick {TickCount} processed successfully in {Duration:F2}s", 
                    _tickCounter, totalDuration.TotalSeconds);
            }
            catch (Exception ex)
            {
                var totalDuration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "WorldModel tick {TickCount} failed after {Duration:F2}s - system will continue", 
                    _tickCounter, totalDuration.TotalSeconds);
            }
        }

        #endregion

        #region IMetricsProvider Implementation

        /// <inheritdoc />
        public string ProviderName => "worldmodel";

        /// <inheritdoc />
        public IEnumerable<string> SimpleMetrics => new[]
        {
            "worldmodel_tick_counter",
            "worldmodel_planets_configured"  // Consolidated from duplicate metrics
        };

        /// <inheritdoc />
        public IEnumerable<string> ComplexMetrics => new string[0]; // Removed low-value system health metric

        /// <inheritdoc />
        public async Task InitializeMetrics(IMetricsService metricsService)
        {
            try
            {
                // Initialize basic counters
                metricsService.SetGauge("worldmodel_tick_counter", _tickCounter);

                // Initialize consolidated planet count (avoiding duplication)
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                metricsService.SetGauge("worldmodel_planets_configured", planets.Count());

                // Initialize metrics from the dedicated metrics service
                await _metricsService.InitializeMetrics(metricsService);

                _logger.LogDebug("WorldModel metrics initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize WorldModel metrics");
            }
        }

        /// <inheritdoc />
        public async Task CalculateComplexMetrics(IMetricsService metricsService)
        {
            try
            {
                // Update basic metrics only
                metricsService.SetGauge("worldmodel_tick_counter", _tickCounter);
                
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                metricsService.SetGauge("worldmodel_planets_configured", planets.Count());

                // Delegate to dedicated metrics service (no complex metrics there either)
                await _metricsService.CalculateComplexMetrics(metricsService);

                _logger.LogTrace("WorldModel metrics updated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update WorldModel metrics");
            }
        }

        #endregion
    }
}
