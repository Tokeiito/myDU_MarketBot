using System.Collections.Generic;
using MarketBot.Domain.WorldModel;
using Microsoft.Extensions.Logging;

public class MarketBotConfig
{
    public MarketSettings Market { get; set; } = new MarketSettings();

    public MarketOverlordSettings MarketOverlord { get; set;} = new MarketOverlordSettings();

    public DevelopmentSettings Development { get; set; } = new DevelopmentSettings();

    public WorldModelSettings WorldModel { get; set; } = new WorldModelSettings();

    public LoggingSettings Logging { get; set; } = new LoggingSettings();
}

public class MarketSettings
{
    public List<ulong> OperationMarkets { get; set; }
    public int MarketOperationsTickInSeconds { get; set; }

    public int QueueProcessingTickInSeconds { get; set; }
}

public class MarketOverlordSettings
{
    public List<ulong> OperationPlanets { get; set; }
    public int TickInSeconds { get; set; }

    public Dictionary<int, Dictionary<string, MarketOverlordQuantities>> TierSettings { get; set; } = new Dictionary<int, Dictionary<string, MarketOverlordQuantities>>();
    public Dictionary<int, int> OreBaselinePrices { get; set; } = new Dictionary<int, int>();
}

public class MarketOverlordQuantities
{
    public int NumberOfBuyOrders { get; set; }
    public int NumberOfSellOrders { get; set; }
    public int QuantityInInventory { get; set; }
    public int QuantityInSellOrders { get; set; }
    public int QuantityInBuyOrders { get; set; }
}

public class DevelopmentSettings
{
    public bool DryRun { get; set; } = false; // Default to false for production safety
    
    /// <summary>
    /// When true, performs a complete cleanup of existing Redis inventory data on startup.
    /// Creates a timestamped backup before deletion. USE WITH CAUTION - this deletes all inventory data.
    /// This should be set to true once before switching from DryRun=true to DryRun=false to ensure
    /// quantity format consistency with the new fixed-point quantity system.
    /// </summary>
    public bool CleanInventoryData { get; set; } = false;
}

/// <summary>
/// Configuration settings for enhanced logging with console output and per-service log levels
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// Whether to enable console logging output
    /// </summary>
    public bool LogToConsole { get; set; } = false;

    /// <summary>
    /// Default console log level for all services
    /// Supported values: "Trace", "Debug", "Information", "Warning", "Error", "Critical", "Off"
    /// </summary>
    public string ConsoleLogLevel { get; set; } = "Information";

    /// <summary>
    /// Per-service log levels for console output
    /// Key: Full service class name (e.g., "MarketBot.Services.WorldModel.ResourceGenerationService")
    /// Value: Log level ("Trace", "Debug", "Information", "Warning", "Error", "Critical", "Off")
    /// "Off" completely disables console output for that service
    /// </summary>
    public Dictionary<string, string> ServiceLogLevels { get; set; } = new Dictionary<string, string>();
}
