using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Domain.WorldModel;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Metrics collection service for World Model & Supply Generator system
    /// following IMetricsProvider pattern with Prometheus integration.
    /// </summary>
    public class WorldModelMetricsService : IMetricsProvider
    {
    private readonly ILAIStateService _laiStateService;
    private readonly IPlanetaryResourceService _planetaryResourceService;
    private readonly ConfigService _configService;
    private readonly ILogger<WorldModelMetricsService> _logger;
    private readonly LAIEngine _laiEngine;
    private readonly IInventoryService _inventoryService;
    private readonly IInventoryCapacityService _capacityService;
        
        // Counters for metrics (since we use gauge, track the counts manually)
        private long _updateCyclesCount = 0;
        
        // Enhanced tracking counters - generation per market per resource
        private readonly ConcurrentDictionary<string, long> _generationByMarket = new();
        
        // Enhanced tracking counters - generation per planet per resource (aggregated)
        private readonly ConcurrentDictionary<string, long> _generationByPlanet = new();
        
    // Enhanced tracking counters - generation events per market
    private readonly ConcurrentDictionary<string, long> _generationEventsByMarket = new();
    
    // Capacity tracking counters
    private readonly ConcurrentDictionary<string, long> _throttledGenerationCount = new();

    public WorldModelMetricsService(
        ILAIStateService laiStateService,
        IPlanetaryResourceService planetaryResourceService,
        ConfigService configService,
        LAIEngine laiEngine,
        ILogger<WorldModelMetricsService> logger,
        IInventoryService inventoryService,
        IInventoryCapacityService capacityService)
        {
        _laiStateService = laiStateService ?? throw new ArgumentNullException(nameof(laiStateService));
        _planetaryResourceService = planetaryResourceService ?? throw new ArgumentNullException(nameof(planetaryResourceService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _laiEngine = laiEngine ?? throw new ArgumentNullException(nameof(laiEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _capacityService = capacityService ?? throw new ArgumentNullException(nameof(capacityService));
        }

        #region IMetricsProvider Implementation

        public string ProviderName => "worldmodel";

        public IEnumerable<string> SimpleMetrics => new[]
        {
            // System health metrics
            "worldmodel_update_cycles_total",          // Number of LAI update cycles completed (system health)
            
            // LAI engine configuration metrics
            "worldmodel_lai_mean_reversion_strength",  // LAI mean reversion strength parameter
            "worldmodel_lai_volatility_factor",        // LAI volatility factor parameter
            "worldmodel_lai_min_value",                // LAI minimum allowed value
            "worldmodel_lai_max_value",                // LAI maximum allowed value
            "worldmodel_lai_initial_value",            // LAI initial value for new resources
            
            // LAI tracking per market
            "worldmodel_lai_by_market",               // LAI values per planet/market/resource
            
            // LAI health monitoring
            "worldmodel_lai_equilibrium_deviation",   // Deviation from theoretical equilibrium per planet/resource
            "worldmodel_lai_equilibrium_value",       // Theoretical equilibrium LAI per planet/resource
            
            // Resource generation tracking
            "worldmodel_generation_by_market",        // Resource generation per planet/market/resource
            "worldmodel_generation_by_planet",        // Resource generation per planet/resource (aggregated)
            "worldmodel_generation_events_by_market", // Generation events per planet/market
            
            // Local resource capacity tracking
            "worldmodel_local_resource_capacity_utilization", // Capacity utilization per planet/market/resource
            "worldmodel_generation_throttled_total",          // Count of throttled generations per planet/market/resource
            "worldmodel_local_resource_quantity",             // Current local resource quantities per planet/market/resource
        };

        public IEnumerable<string> ComplexMetrics => new string[0]; // All complex metrics removed for performance

        public async Task InitializeMetrics(IMetricsService metricsService)
        {
            try
            {
                _logger.LogDebug("Initializing World Model metrics...");

                // Initialize simple metrics
                await InitializeSimpleMetrics(metricsService);

                _logger.LogDebug("World Model metrics initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize World Model metrics");
            }
        }

        public async Task CalculateComplexMetrics(IMetricsService metricsService)
        {
            // No complex metrics to calculate - all removed for performance optimization
            await Task.CompletedTask;
        }

        #endregion

        #region Simple Metrics Initialization

        private void InitializeLAIDiagnosticMetrics(IMetricsService metricsService)
        {
            try
            {
                var laiSettings = _configService.Config.WorldModel.LAISettings;
                
                // Initialize LAI engine configuration parameters as static metrics
                metricsService.SetGauge("worldmodel_lai_mean_reversion_strength", laiSettings.MeanReversionStrength);
                metricsService.SetGauge("worldmodel_lai_volatility_factor", laiSettings.VolatilityFactor);
                metricsService.SetGauge("worldmodel_lai_min_value", laiSettings.MinLAI);
                metricsService.SetGauge("worldmodel_lai_max_value", laiSettings.MaxLAI);
                metricsService.SetGauge("worldmodel_lai_initial_value", laiSettings.InitialLAI);
                
                _logger.LogDebug("Initialized LAI diagnostic metrics - MeanRev: {MeanRev}, Volatility: {Vol}, Bounds: [{Min}, {Max}], Initial: {Init}",
                    laiSettings.MeanReversionStrength, laiSettings.VolatilityFactor, laiSettings.MinLAI, laiSettings.MaxLAI, laiSettings.InitialLAI);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize LAI diagnostic metrics");
            }
        }

        private async Task InitializeSimpleMetrics(IMetricsService metricsService)
        {
            // Initialize LAI diagnostic metrics
            InitializeLAIDiagnosticMetrics(metricsService);

            // Initialize LAI metrics per market
            await InitializeLAIByMarketMetrics(metricsService);

            // Initialize generation metrics
            await InitializeGenerationMetrics(metricsService);

            // Initialize system health counters
            metricsService.SetGauge("worldmodel_update_cycles_total", 0);
        }

        private async Task InitializeLAIByMarketMetrics(IMetricsService metricsService)
        {
            try
            {
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                int totalLAIByMarketValues = 0;

                foreach (var planetId in planets)
                {
                    var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                    var laiValues = await _laiStateService.GetAllLAIForPlanet(planetId);

                    foreach (var marketId in marketIds)
                    {
                        foreach (var (resourceId, laiValue) in laiValues)
                        {
                            // Set LAI gauge with planet, market, and resource labels
                            metricsService.SetGauge("worldmodel_lai_by_market", 
                                new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, laiValue);
                            totalLAIByMarketValues++;
                        }
                    }
                }

                _logger.LogDebug("Initialized {LAICount} LAI by market metrics across {PlanetCount} planets", 
                    totalLAIByMarketValues, planets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize LAI by market metrics");
            }
        }


        private async Task InitializeGenerationMetrics(IMetricsService metricsService)
        {
            try
            {
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                
                foreach (var planetId in planets)
                {
                    var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                    var resourceIds = await _planetaryResourceService.GetConfiguredResourcesForPlanet(planetId);

                    // Initialize generation by market metrics (start at 0)
                    foreach (var marketId in marketIds)
                    {
                        // Initialize generation events by market
                        var eventKey = $"{planetId}_{marketId}";
                        _generationEventsByMarket.TryAdd(eventKey, 0);
                        metricsService.SetGauge("worldmodel_generation_events_by_market", 
                            new[] { planetId.ToString(), marketId.ToString() }, 0);

                        // Initialize generation by market per resource
                        foreach (var resourceId in resourceIds)
                        {
                            var marketKey = $"{planetId}_{marketId}_{resourceId}";
                            _generationByMarket.TryAdd(marketKey, 0);
                            metricsService.SetGauge("worldmodel_generation_by_market", 
                                new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, 0);
                        }
                    }

                    // Initialize generation by planet metrics (aggregated)
                    foreach (var resourceId in resourceIds)
                    {
                        var planetKey = $"{planetId}_{resourceId}";
                        _generationByPlanet.TryAdd(planetKey, 0);
                        metricsService.SetGauge("worldmodel_generation_by_planet", 
                            new[] { planetId.ToString(), resourceId.ToString() }, 0);
                    }
                }
                
                // Initialize capacity metrics for all markets and resources
                await InitializeCapacityMetrics(metricsService);

                _logger.LogDebug("Initialized generation metrics for {PlanetCount} planets", planets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize generation metrics");
            }
        }

        private async Task InitializeCapacityMetrics(IMetricsService metricsService)
        {
            try
            {
                if (!_capacityService.IsCapacityLimitEnabled())
                {
                    _logger.LogDebug("Capacity limits disabled, skipping capacity metrics initialization");
                    return;
                }
                
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                
                foreach (var planetId in planets)
                {
                    var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                    var resourceIds = await _planetaryResourceService.GetConfiguredResourcesForPlanet(planetId);
                    
                    foreach (var marketId in marketIds)
                    {
                        foreach (var resourceId in resourceIds)
                        {
                            // Initialize capacity utilization metrics
                            var utilization = await _capacityService.GetLocalResourceUtilization(resourceId, marketId);
                            metricsService.SetGauge("worldmodel_local_resource_capacity_utilization",
                                new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, utilization);
                            
                            // Initialize local resource quantity metrics
                            var localQuantity = await _inventoryService.GetLocalResourceDisplay(resourceId, marketId);
                            metricsService.SetGauge("worldmodel_local_resource_quantity",
                                new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, localQuantity);
                            
                            // Initialize throttled generation counters
                            var throttledKey = $"{planetId}_{marketId}_{resourceId}";
                            _throttledGenerationCount.TryAdd(throttledKey, 0);
                            metricsService.SetGauge("worldmodel_generation_throttled_total",
                                new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, 0);
                        }
                    }
                }
                
                _logger.LogDebug("Initialized capacity metrics for {PlanetCount} planets", planets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize capacity metrics");
            }
        }

        #endregion

        #region Public Update Methods

        /// <summary>
        /// Update LAI values per market for a specific planet and resource.
        /// Called when LAI values are updated during tick processing.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="laiValue">New LAI value</param>
        public async void UpdateLAIByMarketMetric(IMetricsService metricsService, ulong planetId, ulong resourceId, double laiValue)
        {
            try
            {
                // Update LAI for all markets on the planet (since LAI is planet-wide)
                var marketIds = await _planetaryResourceService.GetMarketIdsForPlanet(planetId);
                
                foreach (var marketId in marketIds)
                {
                    metricsService.SetGauge("worldmodel_lai_by_market", 
                        new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, laiValue);
                }

                _logger.LogTrace("Updated LAI by market metrics for planet {PlanetId}, resource {ResourceId}: {LAI:F4} (across {MarketCount} markets)", 
                    planetId, resourceId, laiValue, marketIds.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update LAI by market metrics for planet {PlanetId}, resource {ResourceId}", 
                    planetId, resourceId);
            }
        }

        /// <summary>
        /// Record resource generation for a specific market and planet.
        /// Called when resources are generated.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="quantity">Quantity of resources generated</param>
        public void RecordResourceGeneration(IMetricsService metricsService, ulong planetId, ulong marketId, ulong resourceId, long quantity)
        {
            try
            {
                // Update generation by market
                var marketKey = $"{planetId}_{marketId}_{resourceId}";
                var newMarketTotal = _generationByMarket.AddOrUpdate(marketKey, quantity, (key, oldValue) => oldValue + quantity);
                metricsService.SetGauge("worldmodel_generation_by_market", 
                    new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, newMarketTotal);

                // Update generation by planet (aggregated)
                var planetKey = $"{planetId}_{resourceId}";
                var newPlanetTotal = _generationByPlanet.AddOrUpdate(planetKey, quantity, (key, oldValue) => oldValue + quantity);
                metricsService.SetGauge("worldmodel_generation_by_planet", 
                    new[] { planetId.ToString(), resourceId.ToString() }, newPlanetTotal);

                // Update generation events by market
                var eventKey = $"{planetId}_{marketId}";
                var newEventCount = _generationEventsByMarket.AddOrUpdate(eventKey, 1, (key, oldValue) => oldValue + 1);
                metricsService.SetGauge("worldmodel_generation_events_by_market", 
                    new[] { planetId.ToString(), marketId.ToString() }, newEventCount);

                _logger.LogTrace("Recorded resource generation: Planet {PlanetId}, Market {MarketId}, Resource {ResourceId}, Quantity {Quantity}", 
                    planetId, marketId, resourceId, quantity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record resource generation for Planet {PlanetId}, Market {MarketId}, Resource {ResourceId}", 
                    planetId, marketId, resourceId);
            }
        }

        /// <summary>
        /// Increment LAI update cycle counter.
        /// Called when LAI update cycles are completed.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        public void IncrementUpdateCycles(IMetricsService metricsService)
        {
            try
            {
                // Increment our internal counter and update the gauge
                System.Threading.Interlocked.Increment(ref _updateCyclesCount);
                metricsService.SetGauge("worldmodel_update_cycles_total", _updateCyclesCount);

                _logger.LogTrace("Incremented LAI update cycles counter to {Count}", _updateCyclesCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to increment update cycles counter");
            }
        }

        /// <summary>
        /// Records a throttled generation event and updates capacity metrics.
        /// Called when resource generation is blocked due to capacity limits.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        public async void RecordThrottledGeneration(IMetricsService metricsService, ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                // Update throttled generation counter
                var throttledKey = $"{planetId}_{marketId}_{resourceId}";
                var newCount = _throttledGenerationCount.AddOrUpdate(throttledKey, 1, (key, oldValue) => oldValue + 1);
                metricsService.SetGauge("worldmodel_generation_throttled_total",
                    new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, newCount);
                
                // Update capacity utilization and local quantity metrics
                await UpdateCapacityMetrics(metricsService, planetId, marketId, resourceId);
                
                _logger.LogTrace("Recorded throttled generation for planet {PlanetId}, market {MarketId}, resource {ResourceId} (count: {Count})",
                    planetId, marketId, resourceId, newCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record throttled generation for planet {PlanetId}, market {MarketId}, resource {ResourceId}",
                    planetId, marketId, resourceId);
            }
        }

        /// <summary>
        /// Updates capacity-related metrics for a specific resource.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        public async Task UpdateCapacityMetrics(IMetricsService metricsService, ulong planetId, ulong marketId, ulong resourceId)
        {
            try
            {
                // Update capacity utilization
                var utilization = await _capacityService.GetLocalResourceUtilization(resourceId, marketId);
                metricsService.SetGauge("worldmodel_local_resource_capacity_utilization",
                    new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, utilization);
                
                // Update local resource quantity
                var localQuantity = await _inventoryService.GetLocalResourceDisplay(resourceId, marketId);
                metricsService.SetGauge("worldmodel_local_resource_quantity",
                    new[] { planetId.ToString(), marketId.ToString(), resourceId.ToString() }, localQuantity);
                
                _logger.LogTrace("Updated capacity metrics for planet {PlanetId}, market {MarketId}, resource {ResourceId}: utilization={Utilization:F3}, quantity={Quantity}",
                    planetId, marketId, resourceId, utilization, localQuantity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update capacity metrics for planet {PlanetId}, market {MarketId}, resource {ResourceId}",
                    planetId, marketId, resourceId);
            }
        }

        /// <summary>
        /// Update LAI health monitoring metrics that compare current LAI values to theoretical equilibrium.
        /// Called after LAI updates to monitor system health.
        /// </summary>
        /// <param name="metricsService">Metrics service instance</param>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="currentLAI">Current average LAI value across markets</param>
        public void UpdateLAIHealthMetrics(IMetricsService metricsService, ulong planetId, ulong resourceId, double currentLAI)
        {
            try
            {
                // Get planet configuration for equilibrium calculation
                var planetConfig = _configService.Config.WorldModel.PlanetConfigs.GetValueOrDefault(planetId);
                if (planetConfig == null)
                {
                    _logger.LogTrace("No planet config found for {PlanetId}, using default seasonal strength for equilibrium calculation", planetId);
                }

                var seasonalStrength = planetConfig?.SeasonalStrength ?? 0.05; // Default from WorldModelSettings
                
                // Get baseline for this resource on this planet
                var baselines = _planetaryResourceService.GetAllResourceBaselines(planetId).Result;
                if (!baselines.TryGetValue(resourceId, out var baseline))
                {
                    _logger.LogTrace("No baseline found for planet {PlanetId}, resource {ResourceId}, skipping health metrics", planetId, resourceId);
                    return;
                }

                // Calculate theoretical equilibrium LAI
                var equilibriumLAI = _laiEngine.CalculateEquilibriumLAI(baseline, seasonalStrength);
                
                // Calculate deviation from equilibrium
                var deviation = Math.Abs(currentLAI - equilibriumLAI);
                
                // Update metrics with planet and resource labels
                metricsService.SetGauge("worldmodel_lai_equilibrium_value", 
                    new[] { planetId.ToString(), resourceId.ToString() }, equilibriumLAI);
                metricsService.SetGauge("worldmodel_lai_equilibrium_deviation", 
                    new[] { planetId.ToString(), resourceId.ToString() }, deviation);
                
                _logger.LogTrace("Updated LAI health metrics for planet {PlanetId}, resource {ResourceId}: Current={Current:F4}, Equilibrium={Equilibrium:F4}, Deviation={Deviation:F4}",
                    planetId, resourceId, currentLAI, equilibriumLAI, deviation);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update LAI health metrics for planet {PlanetId}, resource {ResourceId}", planetId, resourceId);
            }
        }


        #endregion

        #region Diagnostic Methods

        /// <summary>
        /// Get comprehensive World Model metrics summary for debugging.
        /// </summary>
        /// <returns>Formatted metrics summary</returns>
        public async Task<string> GetMetricsSummary()
        {
            try
            {
                var planets = await _planetaryResourceService.GetConfiguredPlanets();
                var storageStats = await _laiStateService.GetStorageStatistics();

                var summary = "World Model Metrics Summary:\n";
                summary += $"  Configured Planets: {planets.Count()}\n";
                summary += $"  LAI Storage Keys: {storageStats.TotalLAIKeys}\n";
                summary += $"  Storage Memory: ~{storageStats.EstimatedMemoryUsage:N0} bytes\n";
                summary += $"  Generation by Market Metrics: {_generationByMarket.Count}\n";
                summary += $"  Generation by Planet Metrics: {_generationByPlanet.Count}\n";
                summary += $"  Generation Events by Market: {_generationEventsByMarket.Count}\n";

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate metrics summary");
                return $"Metrics summary unavailable: {ex.Message}";
            }
        }

        #endregion
    }
}