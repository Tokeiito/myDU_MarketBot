using System.Collections.Generic;
using MarketBot.Domain.WorldModel;
using Microsoft.Extensions.Logging;

public class MarketBotConfig
{
    public MarketSettings Market { get; set; } = new MarketSettings();

    public MarketOverlordSettings MarketOverlord { get; set;} = new MarketOverlordSettings();

    public DevelopmentSettings Development { get; set; } = new DevelopmentSettings();

    public WorldModelSettings WorldModel { get; set; } = new WorldModelSettings();

    public WarehouseSettings Warehouse { get; set; } = new WarehouseSettings();

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

/// <summary>
/// Configuration settings for the warehouse lot management system
/// </summary>
 public class WarehouseSettings
{
    /// <summary>
    /// Maximum number of lots allowed per item/market combination.
    /// When this limit is reached, the system will merge the closest lots by unit cost.
    /// Higher values provide more granular cost tracking but use more memory.
    /// Lower values improve performance but reduce cost tracking precision.
    /// </summary>
    public int MaxLotsPerItem { get; set; } = 10;


    /// <summary>
    /// Cleanup interval for empty lots and maintenance operations (in minutes).
    /// The system will periodically remove empty lots and perform housekeeping.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Cache TTL for expensive cost calculations (in seconds).
    /// Weighted average cost calculations are cached to improve performance.
    /// </summary>
    public int CostCalculationCacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Enable detailed lot operation logging for debugging and auditing.
    /// When true, logs lot creation, merging, and consumption operations.
    /// </summary>
    public bool EnableLotTracking { get; set; } = false;

    /// <summary>
    /// Metrics update interval for warehouse KPIs (in seconds).
    /// Controls how frequently warehouse metrics are refreshed.
    /// </summary>
    public int MetricsUpdateIntervalSeconds { get; set; } = 60;
}
