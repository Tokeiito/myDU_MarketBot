using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Domain.WorldModel;
using MarketBot.Helpers;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services.WorldModel
{
    /// <summary>
    /// Local Abundance Index (LAI) calculation engine implementing Ornstein-Uhlenbeck 
    /// mean reversion dynamics with seasonality and thread safety.
    /// </summary>
    public class LAIEngine
    {
        private readonly Random _random;
        private readonly WorldModelSettings _settings;
        private readonly ILogger<LAIEngine> _logger;
        private readonly MarketBot.Interfaces.ILAIStateService _laiStateService;
        private readonly object _randomLock = new object();

        // Thread-safe cache for LAI update calculations
        private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes;

        public LAIEngine(ConfigService configService, MarketBot.Interfaces.ILAIStateService laiStateService, ILogger<LAIEngine> logger)
        {
            var config = configService ?? throw new ArgumentNullException(nameof(configService));
            _settings = config.Config.WorldModel;
            _laiStateService = laiStateService ?? throw new ArgumentNullException(nameof(laiStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _random = new Random();
            _lastUpdateTimes = new ConcurrentDictionary<string, DateTime>();
        }

        /// <summary>
        /// Update LAI value using Ornstein-Uhlenbeck process with mean reversion,
        /// volatility, and seasonal effects.
        /// </summary>
        /// <param name="currentLAI">Current LAI value</param>
        /// <param name="planetBaseline">Target baseline value for mean reversion</param>
        /// <param name="planetConfig">Planet-specific configuration</param>
        /// <param name="deltaTime">Time elapsed since last update (in seconds)</param>
        /// <returns>New LAI value clamped within configured bounds</returns>
        public double UpdateLAI(double currentLAI, double planetBaseline, PlanetConfig planetConfig, double deltaTime)
        {
            if (deltaTime <= 0)
            {
                _logger.LogDebug("Zero or negative delta time {DeltaTime}, returning current LAI {CurrentLAI}", 
                    deltaTime, currentLAI);
                return currentLAI;
            }

            // Ornstein-Uhlenbeck process: dX = θ(μ - X)dt + σ√dt * dW
            // where θ = mean reversion strength, μ = mean (baseline), σ = volatility, dW = Wiener process

            var laiParams = _settings.LAISettings;

            // Mean reversion component: θ(μ - X)dt
            double meanReversion = laiParams.MeanReversionStrength * (planetBaseline - currentLAI) * deltaTime;

            // Volatility component: σ√dt * dW (where dW is Gaussian random)
            double volatility;
            lock (_randomLock)
            {
                double gaussianNoise = _random.NextGaussian();
                volatility = laiParams.VolatilityFactor * planetConfig.RegionalVariance * 
                           Math.Sqrt(deltaTime) * gaussianNoise;
            }

            // Seasonal component
            double seasonal = CalculateSeasonalFactor(planetConfig.SeasonalStrength);

            // Calculate new LAI value
            double newLAI = currentLAI + meanReversion + volatility + seasonal;

            // Clamp within configured bounds
            newLAI = MathExtensions.Clamp(newLAI, laiParams.MinLAI, laiParams.MaxLAI);

            if (_settings.EnableDetailedLogging)
            {
                _logger.LogDebug("LAI Update: {CurrentLAI:F4} -> {NewLAI:F4} " +
                                "(MeanRev: {MeanReversion:F4}, Vol: {Volatility:F4}, Seasonal: {Seasonal:F4})",
                    currentLAI, newLAI, meanReversion, volatility, seasonal);
            }

            return newLAI;
        }

        /// <summary>
        /// Calculate LAI updates for multiple resources on a planet simultaneously.
        /// This is more efficient than individual updates when updating many resources at once.
        /// </summary>
        /// <param name="planetProfile">Planet resource profile with current LAI values</param>
        /// <param name="deltaTime">Time elapsed since last update (in seconds)</param>
        /// <returns>Updated planet profile with new LAI values</returns>
        public PlanetResourceProfile UpdateAllLAIForPlanet(PlanetResourceProfile planetProfile, double deltaTime)
        {
            if (deltaTime <= 0 || planetProfile == null)
            {
                _logger.LogDebug("Invalid parameters for planet LAI update: deltaTime={DeltaTime}, profile null={IsNull}",
                    deltaTime, planetProfile == null);
                return planetProfile;
            }

            var planetConfig = GetPlanetConfig(planetProfile.PlanetId);
            if (planetConfig == null)
            {
                _logger.LogWarning("No configuration found for planet {PlanetId}, skipping LAI update", 
                    planetProfile.PlanetId);
                return planetProfile;
            }

            int updateCount = 0;
            foreach (var resourceId in planetProfile.GetConfiguredResources())
            {
                if (planetProfile.BaselineAbundance.TryGetValue(resourceId, out var baseline) &&
                    planetProfile.CurrentLAI.TryGetValue(resourceId, out var currentLAI))
                {
                    double newLAI = UpdateLAI(currentLAI, baseline, planetConfig, deltaTime);
                    planetProfile.CurrentLAI[resourceId] = newLAI;
                    updateCount++;
                }
            }

            planetProfile.LastUpdate = DateTime.UtcNow;

            _logger.LogDebug("Updated {UpdateCount} LAI values for planet {PlanetId} ({PlanetName})",
                updateCount, planetProfile.PlanetId, planetProfile.PlanetName);

            return planetProfile;
        }

        /// <summary>
        /// Initialize LAI values for a new planet or resource using configured initial values
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="baseline">Baseline abundance for this resource</param>
        /// <returns>Initial LAI value</returns>
        public double InitializeLAI(ulong planetId, ulong resourceId, double baseline)
        {
            var initialLAI = _settings.LAISettings.InitialLAI;

            // Add small random variation to initial LAI to prevent all resources starting identical
            double variation;
            lock (_randomLock)
            {
                variation = _random.NextGaussian(0, 0.05); // 5% standard deviation
            }

            double initialValue = MathExtensions.Clamp(
                initialLAI + variation, 
                _settings.LAISettings.MinLAI, 
                _settings.LAISettings.MaxLAI);

            _logger.LogDebug("Initialized LAI for planet {PlanetId}, resource {ResourceId}: {InitialLAI:F4} " +
                            "(baseline: {Baseline:F4})", planetId, resourceId, initialValue, baseline);

            return initialValue;
        }

        /// <summary>
        /// Calculate seasonal factor based on current date and planet configuration
        /// </summary>
        /// <param name="seasonalStrength">Strength of seasonal effect from planet config</param>
        /// <returns>Seasonal adjustment factor</returns>
        private double CalculateSeasonalFactor(double seasonalStrength)
        {
            if (Math.Abs(seasonalStrength) < 0.001) // Effectively zero seasonal effect
                return 0.0;

            return MathExtensions.GetCurrentSeasonalFactor(seasonalStrength);
        }

        /// <summary>
        /// Get planet configuration from world model settings
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <returns>Planet configuration or null if not found</returns>
        private PlanetConfig? GetPlanetConfig(ulong planetId)
        {
            _settings.PlanetConfigs.TryGetValue(planetId, out var config);
            return config;
        }

        /// <summary>
        /// Calculate the theoretical equilibrium LAI for a resource given current parameters.
        /// This is useful for analysis and debugging.
        /// </summary>
        /// <param name="planetBaseline">Planet baseline abundance</param>
        /// <param name="seasonalStrength">Seasonal effect strength</param>
        /// <returns>Theoretical equilibrium LAI value</returns>
        public double CalculateEquilibriumLAI(double planetBaseline, double seasonalStrength)
        {
            // In Ornstein-Uhlenbeck process, equilibrium mean is the baseline plus seasonal offset
            double seasonalOffset = CalculateSeasonalFactor(seasonalStrength);
            return MathExtensions.Clamp(
                planetBaseline + seasonalOffset,
                _settings.LAISettings.MinLAI,
                _settings.LAISettings.MaxLAI);
        }

        /// <summary>
        /// Update LAI values for all markets on a planet with hard conservation.
        /// This ensures that the sum of LAI values per resource across all markets remains constant.
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketLAIValues">Current LAI values per (marketId, resourceId)</param>
        /// <param name="planetBaselines">Planet baseline values per resource</param>
        /// <param name="deltaTime">Time elapsed since last update (in seconds)</param>
        /// <returns>Updated LAI values with conservation applied</returns>
        public Dictionary<(ulong marketId, ulong resourceId), double> UpdateAllLAIForPlanetWithConservation(
            ulong planetId, 
            Dictionary<(ulong marketId, ulong resourceId), double> marketLAIValues,
            Dictionary<ulong, double> planetBaselines,
            double deltaTime)
        {
            // Enhanced parameter validation with comprehensive fallbacks
            if (marketLAIValues == null)
            {
                _logger.LogWarning("Null marketLAIValues provided for planet {PlanetId} - returning empty dictionary", planetId);
                return new Dictionary<(ulong marketId, ulong resourceId), double>();
            }
            
            if (planetBaselines == null)
            {
                _logger.LogWarning("Null planetBaselines provided for planet {PlanetId} - continuing with current LAI values", planetId);
                return new Dictionary<(ulong marketId, ulong resourceId), double>(marketLAIValues);
            }
            
            if (deltaTime <= 0)
            {
                _logger.LogDebug("Invalid deltaTime={DeltaTime} for planet {PlanetId} - returning current LAI values", deltaTime, planetId);
                return new Dictionary<(ulong marketId, ulong resourceId), double>(marketLAIValues);
            }
            
            if (!marketLAIValues.Any())
            {
                _logger.LogDebug("Empty marketLAIValues for planet {PlanetId} - nothing to update", planetId);
                return marketLAIValues;
            }
            
            // Performance safeguard for very large datasets
            if (marketLAIValues.Count > 10000) // Arbitrary threshold, adjust based on performance testing
            {
                _logger.LogWarning("Very large LAI dataset ({Count} values) for planet {PlanetId} - processing may be slow", 
                    marketLAIValues.Count, planetId);
            }

            // Get planet config with fallback mechanism
            var planetConfig = GetPlanetConfig(planetId);
            if (planetConfig == null)
            {
                _logger.LogWarning("No configuration found for planet {PlanetId}, using default configuration", planetId);
                
                // Create fallback configuration instead of skipping
                planetConfig = new PlanetConfig
                {
                    PlanetId = planetId,
                    Name = $"Planet_{planetId}",
                    RegionalVariance = 0.05, // Conservative default
                    SeasonalStrength = 0.0,  // No seasonal effect by default
                    MarketIds = marketLAIValues.Select(kvp => kvp.Key.marketId).Distinct().ToList()
                };
                
                _logger.LogDebug("Created fallback configuration for planet {PlanetId} with {MarketCount} markets",
                    planetId, planetConfig.MarketIds.Count);
            }

            var updatedLAI = new Dictionary<(ulong marketId, ulong resourceId), double>();
            
            // Group by resource to apply conservation per resource
            var resourceGroups = marketLAIValues.GroupBy(kvp => kvp.Key.resourceId);
            
            foreach (var resourceGroup in resourceGroups)
            {
                var resourceId = resourceGroup.Key;
                if (!planetBaselines.TryGetValue(resourceId, out var baseline))
                {
                    // If no baseline, keep current values
                    foreach (var kvp in resourceGroup)
                    {
                        updatedLAI[kvp.Key] = kvp.Value;
                    }
                    continue;
                }

                // Calculate original sum for this resource
                var originalSum = resourceGroup.Sum(kvp => kvp.Value);
                
                // Update each market's LAI using OU process
                var tempUpdates = new List<((ulong marketId, ulong resourceId) key, double oldLAI, double newLAI)>();
                
                foreach (var kvp in resourceGroup)
                {
                    var marketKey = kvp.Key;
                    var currentLAI = kvp.Value;
                    
                    // Apply OU process
                    var newLAI = UpdateLAI(currentLAI, baseline, planetConfig, deltaTime);
                    tempUpdates.Add((marketKey, currentLAI, newLAI));
                }
                
                // Apply hard conservation: normalize so sum remains the same
                var newSum = tempUpdates.Sum(x => x.newLAI);
                double conservationFactor;
                
                // Handle division by zero or very small values
                if (newSum < 0.00001)
                {
                    _logger.LogWarning("Conservation calculation error: newSum is zero or very small ({NewSum}) for resource {ResourceId} on planet {PlanetId}. Using fallback values.",
                        newSum, resourceId, planetId);
                    conservationFactor = 1.0; // Fallback to no adjustment
                }
                else
                {
                    conservationFactor = originalSum / newSum;
                    
                    // Validate conservation factor for suspicious values
                    if (conservationFactor < 0.5 || conservationFactor > 2.0)
                    {
                        _logger.LogWarning("Conservation factor {Factor:F4} outside expected range for resource {ResourceId} on planet {PlanetId}. Original sum: {OriginalSum:F4}, New sum: {NewSum:F4}",
                            conservationFactor, resourceId, planetId, originalSum, newSum);
                            
                        // Clamp conservation factor to prevent extreme adjustments
                        conservationFactor = MathExtensions.Clamp(conservationFactor, 0.5, 2.0);
                    }
                    else if (Math.Abs(conservationFactor - 1.0) > 0.0001) // Only log if significant adjustment
                    {
                        _logger.LogTrace("Applying conservation factor {Factor:F4} for resource {ResourceId} on planet {PlanetId}", 
                            conservationFactor, resourceId, planetId);
                    }
                }
                
                // Apply conservation factor and clamp within bounds
                foreach (var update in tempUpdates)
                {
                    var conservedLAI = update.newLAI * conservationFactor;
                    
                    // Get bounds for clamping
                    double minLAI = _settings.LAISettings.MinLAI;
                    double maxLAI = _settings.LAISettings.MaxLAI;
                    
                    // Check if clamping is needed and log if significant
                    if (conservedLAI < minLAI)
                    {
                        _logger.LogDebug("LAI value {LAI:F4} below minimum bound {Min:F4} for resource {ResourceId}, market {MarketId} on planet {PlanetId} - clamping",
                            conservedLAI, minLAI, update.key.resourceId, update.key.marketId, planetId);
                    }
                    else if (conservedLAI > maxLAI)
                    {
                        _logger.LogDebug("LAI value {LAI:F4} above maximum bound {Max:F4} for resource {ResourceId}, market {MarketId} on planet {PlanetId} - clamping",
                            conservedLAI, maxLAI, update.key.resourceId, update.key.marketId, planetId);
                    }
                    
                    var clampedLAI = MathExtensions.Clamp(conservedLAI, minLAI, maxLAI);
                    updatedLAI[update.key] = clampedLAI;
                }
            }

            if (_settings.EnableDetailedLogging)
            {
                var totalUpdates = updatedLAI.Count;
                var resourceCount = resourceGroups.Count();
                _logger.LogDebug("Updated {UpdateCount} LAI values for {ResourceCount} resources on planet {PlanetId} with conservation",
                    totalUpdates, resourceCount, planetId);
            }

            return updatedLAI;
        }

        /// <summary>
        /// Initialize LAI values for a market with small random variations
        /// </summary>
        /// <param name="planetId">Planet identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="baseline">Baseline abundance for this resource</param>
        /// <returns>Initial LAI value</returns>
        public double InitializeMarketLAI(ulong planetId, ulong marketId, ulong resourceId, double baseline)
        {
            var initialLAI = _settings.LAISettings.InitialLAI;

            // Add small random variation to initial LAI to create market differences
            double variation;
            lock (_randomLock)
            {
                // Use market ID as seed modifier for consistent but different variations per market
                var marketSeed = (int)(marketId % int.MaxValue);
                var marketRandom = new Random(marketSeed + (int)(planetId % int.MaxValue));
                variation = marketRandom.NextGaussian(0, 0.1); // 10% standard deviation for market differentiation
            }

            double initialValue = MathExtensions.Clamp(
                initialLAI + variation, 
                _settings.LAISettings.MinLAI, 
                _settings.LAISettings.MaxLAI);

            _logger.LogDebug("Initialized LAI for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {InitialLAI:F4} " +
                            "(baseline: {Baseline:F4}, variation: {Variation:F4})", 
                            planetId, marketId, resourceId, initialValue, baseline, variation);

            return initialValue;
        }


        /// <summary>
        /// Get diagnostic information about LAI calculation parameters
        /// </summary>
        /// <returns>Formatted diagnostic string</returns>
        public string GetDiagnosticInfo()
        {
            var lai = _settings.LAISettings;
            return $"LAI Engine - MeanRev: {lai.MeanReversionStrength:F3}, " +
                   $"Volatility: {lai.VolatilityFactor:F3}, " +
                   $"Bounds: [{lai.MinLAI:F2}, {lai.MaxLAI:F2}], " +
                   $"Initial: {lai.InitialLAI:F2}";
        }
    }
}