using System;
using System.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

public class ConfigService
{
    public MarketBotConfig Config { get; private set; }

    public ConfigService(IOptions<ConfigOptions> options)
    {
        var confText = System.IO.File.ReadAllText(options.Value.ConfigPath);
        Config = JsonConvert.DeserializeObject<MarketBotConfig>(confText);

        // Validate configuration
        ValidateConfiguration();
    }

    private void ValidateConfiguration()
    {
        if (Config.Market?.OperationMarkets == null || Config.Market.OperationMarkets.Count == 0)
        {
            throw new InvalidOperationException("Configuration error: Market.OperationMarkets must be specified in config.json and cannot be empty.");
        }

        if (Config.MarketOverlord?.OperationPlanets == null || Config.MarketOverlord.OperationPlanets.Count == 0)
        {
            throw new InvalidOperationException("Configuration error: MarketOverlord.OperationPlanets must be specified in config.json and cannot be empty.");
        }

        // Validate World Model configuration
        ValidateWorldModelConfiguration();

        Console.WriteLine($"Loaded configuration with {Config.Market.OperationMarkets.Count} operation markets and {Config.MarketOverlord.OperationPlanets.Count} operation planets.");
    }

    private void ValidateWorldModelConfiguration()
    {
        if (Config.WorldModel == null)
        {
            throw new InvalidOperationException("Configuration error: WorldModel section is missing from config.json. " +
                "Please add a WorldModel configuration section with PlanetConfigs, LAISettings, and GenerationSettings. " +
                "See documentation for required structure.");
        }

        if (Config.WorldModel.PlanetConfigs == null || Config.WorldModel.PlanetConfigs.Count == 0)
        {
            throw new InvalidOperationException("Configuration error: WorldModel.PlanetConfigs is missing or empty in config.json. " +
                "At least one planet must be configured for the World Model to function. " +
                "Add planet configurations with ResourceTierBaselines and MarketIds.");
        }

        if (Config.WorldModel.LAISettings == null)
        {
            throw new InvalidOperationException("Configuration error: WorldModel.LAISettings is missing from config.json. " +
                "LAI parameters are required for Local Abundance Index calculations. " +
                "Add LAISettings with MeanReversionStrength, VolatilityFactor, MinLAI, MaxLAI, and InitialLAI.");
        }

        if (Config.WorldModel.GenerationSettings == null)
        {
            throw new InvalidOperationException("Configuration error: WorldModel.GenerationSettings is missing from config.json. " +
                "Resource generation settings are required for supply generation. " +
                "Add GenerationSettings with BaseRate, MaxGenerationPerPlanetPerCycle, and other generation parameters.");
        }

        // Validate individual planet configurations
        foreach (var planetConfig in Config.WorldModel.PlanetConfigs)
        {
            var planetId = planetConfig.Key;
            var planet = planetConfig.Value;

            // ResourceTierBaselines are now optional - defaults will be used if not specified
            if (planet.ResourceTierBaselines != null && planet.ResourceTierBaselines.Count > 0)
            {
                // Validate tier baseline values if they are provided
                foreach (var baseline in planet.ResourceTierBaselines)
                {
                    if (baseline.Value < 0.0 || baseline.Value > 1.0)
                    {
                        throw new InvalidOperationException($"Configuration error: Planet {planetId} tier {baseline.Key} has invalid baseline {baseline.Value}. " +
                            "Resource tier baselines must be between 0.0 and 1.0.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Info: Planet {planetId} will use default tier baselines (no ResourceTierBaselines specified)");
            }

            if (planet.MarketIds == null || planet.MarketIds.Count == 0)
            {
                throw new InvalidOperationException($"Configuration error: Planet {planetId} has no MarketIds configured. " +
                    "Each planet must have at least one market ID defined for resource generation.");
            }


            // Cross-validate with operation planets
            if (Config.MarketOverlord.OperationPlanets != null && 
                !Config.MarketOverlord.OperationPlanets.Contains(planetId))
            {
                Console.WriteLine($"Warning: Planet {planetId} is configured in WorldModel but not in MarketOverlord.OperationPlanets. " +
                    "This planet will generate resources but may not have market operations.");
            }
        }

        // Validate LAI parameters
        var lai = Config.WorldModel.LAISettings;
        if (lai.MinLAI <= 0 || lai.MinLAI >= lai.MaxLAI)
        {
            throw new InvalidOperationException($"Configuration error: LAI MinLAI ({lai.MinLAI}) must be positive and less than MaxLAI ({lai.MaxLAI}).");
        }

        if (lai.InitialLAI < lai.MinLAI || lai.InitialLAI > lai.MaxLAI)
        {
            throw new InvalidOperationException($"Configuration error: LAI InitialLAI ({lai.InitialLAI}) must be between MinLAI ({lai.MinLAI}) and MaxLAI ({lai.MaxLAI}).");
        }

        if (lai.MeanReversionStrength <= 0)
        {
            throw new InvalidOperationException($"Configuration error: LAI MeanReversionStrength ({lai.MeanReversionStrength}) must be positive.");
        }

        // Validate generation settings
        var gen = Config.WorldModel.GenerationSettings;
        if (gen.BaseRate <= 0)
        {
            throw new InvalidOperationException($"Configuration error: GenerationSettings BaseRate ({gen.BaseRate}) must be positive.");
        }

        if (gen.MaxGenerationPerPlanetPerCycle <= 0)
        {
            throw new InvalidOperationException($"Configuration error: GenerationSettings MaxGenerationPerPlanetPerCycle ({gen.MaxGenerationPerPlanetPerCycle}) must be positive.");
        }

        if (gen.MaxGenerationPerResourcePerCycle <= 0)
        {
            throw new InvalidOperationException($"Configuration error: GenerationSettings MaxGenerationPerResourcePerCycle ({gen.MaxGenerationPerResourcePerCycle}) must be positive.");
        }

        var configuredPlanets = Config.WorldModel.PlanetConfigs.Count;
        var totalResourceConfigs = Config.WorldModel.PlanetConfigs.Values.Sum(p => p.ResourceTierBaselines.Count);
        var totalMarkets = Config.WorldModel.PlanetConfigs.Values.Sum(p => p.MarketIds.Count);
        
        Console.WriteLine($"World Model validated: {configuredPlanets} planets, {totalResourceConfigs} resource configurations, {totalMarkets} markets.");
    }
}

public class ConfigOptions
{
    public string ConfigPath { get; set; }
}
