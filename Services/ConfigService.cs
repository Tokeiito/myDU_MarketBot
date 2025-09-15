using System;
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

        Console.WriteLine($"Loaded configuration with {Config.Market.OperationMarkets.Count} operation markets and {Config.MarketOverlord.OperationPlanets.Count} operation planets.");
    }
}

public class ConfigOptions
{
    public string ConfigPath { get; set; }
}
