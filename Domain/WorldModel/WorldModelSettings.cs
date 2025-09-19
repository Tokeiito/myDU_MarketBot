using System.Collections.Generic;

namespace MarketBot.Domain.WorldModel
{
    /// <summary>
    /// Configuration settings for the World Model & Supply Generator system.
    /// Defines planetary resource baselines, LAI parameters, and generation settings.
    /// </summary>
    public class WorldModelSettings
    {
        /// <summary>
        /// Dictionary of planet configurations keyed by planet ID
        /// </summary>
        public Dictionary<ulong, PlanetConfig> PlanetConfigs { get; set; } = new Dictionary<ulong, PlanetConfig>();

        /// <summary>
        /// Default tier baselines used when a planet doesn't have specific ResourceTierBaselines configured.
        /// Key: Resource Tier (1-5), Value: Baseline abundance [0.0, 1.0]
        /// These provide sensible defaults so planets don't need explicit tier configuration.
        /// </summary>
        public Dictionary<ulong, double> DefaultResourceTierBaselines { get; set; } = new Dictionary<ulong, double>
        {
            { 1, 1.0 }, // Tier 1 (Pure ores) - Most abundant
            { 2, 0.8 }, // Tier 2 (Products) - Common
            { 3, 0.6 }, // Tier 3 (Products) - Less common
            { 4, 0.4 }, // Tier 4 (Products) - Uncommon
            { 5, 0.2 }  // Tier 5 (Products) - Rare
        };

        /// <summary>
        /// Parameters controlling Local Abundance Index (LAI) dynamics
        /// </summary>
        public LAIParameters LAISettings { get; set; } = new LAIParameters();

        /// <summary>
        /// Settings controlling resource generation rates and limits
        /// </summary>
        public ResourceGenerationSettings GenerationSettings { get; set; } = new ResourceGenerationSettings();

        /// <summary>
        /// How often (in ticks) the world model updates LAI values.
        /// Default: 60 ticks (1 minute at 1 tick per second)
        /// </summary>
        public int LAIUpdateTicks { get; set; } = 60;
        
        /// <summary>
        /// How often (in ticks) the world model generates resources.
        /// Default: 30 ticks (30 seconds at 1 tick per second)
        /// </summary>
        public int ResourceGenerationTicks { get; set; } = 30;

        /// <summary>
        /// Maximum number of generation events to keep in memory for analysis.
        /// Older events are automatically purged. Default: 1000
        /// </summary>
        public int MaxGenerationEventsHistory { get; set; } = 1000;

        /// <summary>
        /// Whether to enable detailed logging of LAI updates and resource generation
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;
    }

    /// <summary>
    /// Configuration for a specific planet's resource characteristics
    /// </summary>
    public class PlanetConfig
    {
        /// <summary>
        /// Unique identifier for this planet
        /// </summary>
        public ulong PlanetId { get; set; }

        /// <summary>
        /// Human-readable name of the planet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Tier-based resource abundance baselines for this planet.
        /// Key: Resource Tier (1-5), Value: Baseline abundance [0.0, 1.0]
        /// Higher values mean resources of that tier are more abundant on this planet.
        /// Applied to actual planet ores based on their tier classification.
        /// </summary>
        public Dictionary<ulong, double> ResourceTierBaselines { get; set; } = new Dictionary<ulong, double>();

        /// <summary>
        /// Regional variance factor affecting LAI volatility on this planet.
        /// Higher values create more dramatic abundance swings.
        /// Default: 0.1 (10% variance)
        /// </summary>
        public double RegionalVariance { get; set; } = 0.1;

        /// <summary>
        /// Seasonal strength factor for this planet.
        /// Controls how much seasonal cycles affect resource abundance.
        /// Default: 0.05 (5% seasonal effect)
        /// </summary>
        public double SeasonalStrength { get; set; } = 0.05;

        /// <summary>
        /// Market IDs associated with this planet where resources can be generated
        /// </summary>
        public List<ulong> MarketIds { get; set; } = new List<ulong>();
    }

    /// <summary>
    /// Parameters controlling Local Abundance Index (LAI) calculation dynamics
    /// </summary>
    public class LAIParameters
    {
        /// <summary>
        /// Mean reversion strength parameter (θ in Ornstein-Uhlenbeck process).
        /// Higher values make LAI return to baseline faster.
        /// Default: 0.1
        /// </summary>
        public double MeanReversionStrength { get; set; } = 0.1;

        /// <summary>
        /// Volatility factor (σ in Ornstein-Uhlenbeck process).
        /// Controls random fluctuation magnitude.
        /// Default: 0.02 (2% volatility)
        /// </summary>
        public double VolatilityFactor { get; set; } = 0.02;

        /// <summary>
        /// Minimum allowed LAI value across all resources and planets.
        /// Prevents resources from becoming completely unavailable.
        /// Default: 0.1 (10% of baseline)
        /// </summary>
        public double MinLAI { get; set; } = 0.1;

        /// <summary>
        /// Maximum allowed LAI value across all resources and planets.
        /// Prevents unrealistic abundance spikes.
        /// Default: 2.0 (200% of baseline)
        /// </summary>
        public double MaxLAI { get; set; } = 2.0;

        /// <summary>
        /// Initial LAI value for new resources or planets.
        /// Should be between MinLAI and MaxLAI.
        /// Default: 1.0 (equal to baseline)
        /// </summary>
        public double InitialLAI { get; set; } = 1.0;
    }

    /// <summary>
    /// Settings controlling resource generation rates and limits
    /// </summary>
    public class ResourceGenerationSettings
    {
        /// <summary>
        /// Base generation rate multiplier applied to all resource generation.
        /// Higher values increase overall resource availability.
        /// Default: 1.0
        /// </summary>
        public double BaseRate { get; set; } = 1.0;

        /// <summary>
        /// Maximum resources that can be generated per planet per update cycle.
        /// Prevents excessive resource flooding.
        /// Default: 10000
        /// </summary>
        public long MaxGenerationPerPlanetPerCycle { get; set; } = 10000;

        /// <summary>
        /// Maximum resources that can be generated for a single resource type 
        /// per planet per update cycle.
        /// Default: 1000
        /// </summary>
        public long MaxGenerationPerResourcePerCycle { get; set; } = 1000;

        /// <summary>
        /// Resource-specific generation multipliers.
        /// Key: Resource ID, Value: Multiplier applied to that resource's generation.
        /// Allows fine-tuning of specific resource availability.
        /// </summary>
        public Dictionary<ulong, double> ResourceMultipliers { get; set; } = new Dictionary<ulong, double>();

        /// <summary>
        /// Market-specific generation multipliers for fine-tuning resource generation per market.
        /// Key: (PlanetId, MarketId, ResourceId) as string "planetId_marketId_resourceId", 
        /// Value: Multiplier applied to that market's resource generation.
        /// Default: Empty (no market-specific multipliers)
        /// </summary>
        public Dictionary<string, double> MarketMultipliers { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Maximum locally generated resources per resource type per market.
        /// Prevents infinite resource accumulation from generation.
        /// -1 = unlimited, 0 = no generation, positive values = limit in display units
        /// Value must be specified in configuration (no default)
        /// </summary>
        public long LocalResourceCapacity { get; set; }
    }
}
