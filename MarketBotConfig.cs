using System.Collections.Generic;

public class MarketBotConfig
{
    public MarketSettings Market { get; set; } = new MarketSettings();

    public MarketOverlordSettings MarketOverlord { get; set;} = new MarketOverlordSettings();

    public DevelopmentSettings Development { get; set; } = new DevelopmentSettings();
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
}
