using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Domain;
using MarketBot.Interfaces;
using MarketBot.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Backend;

namespace MarketBot.TestUtilities.Fixtures;

/// <summary>
/// Enum to define different lot scenario types for multi-lot testing
/// </summary>
public enum MultiLotScenarioType
{
    /// <summary>Realistic mix of mined, purchased, and crafted resources</summary>
    Realistic,
    /// <summary>Only mined resources with zero or very low costs</summary>
    Mined,
    /// <summary>Only purchased resources at various market prices</summary>
    Purchased,
    /// <summary>Only crafted resources with high component costs</summary>
    Crafted,
    /// <summary>Complex scenario with many lots and edge cases</summary>
    Complex
}

/// <summary>
/// Test fixture for WarehouseService simulation tests with pre-configured mocks
/// </summary>
public class WarehouseServiceTestFixture
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;
    
    // Mocked dependencies - using nullable to fix compiler warnings
    public Mock<IDatabase> MockDatabase { get; private set; } = null!;
    public Mock<ILogger<IWarehouseService>> MockLogger { get; private set; } = null!;
    public Mock<IMetricsService> MockMetricsService { get; private set; } = null!;
    public Mock<IWarehouseEventService> MockEventService { get; private set; } = null!;
    public Mock<IWarehouseLotStorage> MockLotStorage { get; private set; } = null!;
    public Mock<IConfigService> MockConfigService { get; private set; } = null!;
    public Mock<IGameplayBank> MockGameplayBank { get; private set; } = null!;

    public WarehouseServiceTestFixture()
    {
        SetupMocks();
        ConfigureDefaultBehavior();
    }

    private void SetupMocks()
    {
        MockDatabase = new Mock<IDatabase>();
        MockLogger = new Mock<ILogger<IWarehouseService>>();
        MockMetricsService = new Mock<IMetricsService>();
        MockEventService = new Mock<IWarehouseEventService>();
        MockLotStorage = new Mock<IWarehouseLotStorage>();
        MockConfigService = new Mock<IConfigService>();
        MockGameplayBank = new Mock<IGameplayBank>();
    }

    private void ConfigureDefaultBehavior()
    {
        // Configure ConfigService mock with default warehouse settings
        var defaultConfig = new MarketBotConfig
        {
            Development = new DevelopmentSettings { DryRun = false },
            Warehouse = new WarehouseSettings
            {
                MaxLotsPerItem = 10,
                EnableLotTracking = false
            }
        };
        
        MockConfigService.Setup(x => x.Config).Returns(defaultConfig);

        // Configure LotStorage mock for basic operations
        MockLotStorage.Setup(x => x.GetLotsForItemAsync(It.IsAny<ulong>(), It.IsAny<ulong?>()))
            .ReturnsAsync(new List<WarehouseLot>());

        MockLotStorage.Setup(x => x.StoreLotAsync(It.IsAny<WarehouseLot>()))
            .ReturnsAsync(true);

        MockLotStorage.Setup(x => x.StoreItemSummaryAsync(It.IsAny<WarehouseLotSummary>()))
            .ReturnsAsync(true);

        MockLotStorage.Setup(x => x.GenerateLotId())
            .Returns(() => Guid.NewGuid().ToString("N")[..12]); // Match the real implementation

        // Configure EventService mock - all async methods return completed tasks
        // Explicitly provide all parameters including optional timestamp to avoid expression tree issues
        MockEventService.Setup(x => x.EmitItemsAddedAsync(
                It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()))
            .Returns(Task.CompletedTask);

        MockEventService.Setup(x => x.EmitItemsConsumedAsync(
                It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()))
            .Returns(Task.CompletedTask);

        // Configure MetricsService mock - all void methods do nothing by default  
        // Note: Using explicit overload setups to avoid optional parameter expression tree issues
        
        // Set up Increment overload with labels
        MockMetricsService.Setup(x => x.Increment(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<double>()))
            .Callback<string, string[], double>((metric, tags, value) => { /* No-op */ });
            
        // Set up Increment overload without labels (with default value parameter)
        MockMetricsService.Setup(x => x.Increment(It.IsAny<string>(), It.IsAny<double>()))
            .Callback<string, double>((metric, value) => { /* No-op */ });
            
        // Set up SetGauge overload with labels
        MockMetricsService.Setup(x => x.SetGauge(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<double>()))
            .Callback<string, string[], double>((metric, tags, value) => { /* No-op */ });
            
        // Set up SetGauge overload without labels
        MockMetricsService.Setup(x => x.SetGauge(It.IsAny<string>(), It.IsAny<double>()))
            .Callback<string, double>((metric, value) => { /* No-op */ });
    }

    /// <summary>
    /// Create a WarehouseService instance with all mocked dependencies
    /// </summary>
    public WarehouseService CreateWarehouseService()
    {
        return new WarehouseService(
            MockLotStorage.Object,
            MockDatabase.Object,
            MockLogger.Object,
            MockMetricsService.Object,
            MockGameplayBank.Object,
            MockConfigService.Object,
            MockEventService.Object);
    }

    /// <summary>
    /// Set up the lot storage mock to return specific lots for an item
    /// </summary>
    public void SetupLotStorageWithLots(ulong itemId, ulong? marketId, params WarehouseLot[] lots)
    {
        MockLotStorage.Setup(x => x.GetLotsForItemAsync(itemId, marketId))
            .ReturnsAsync(new List<WarehouseLot>(lots));
    }

    /// <summary>
    /// Set up the lot storage to simulate empty warehouse (no lots)
    /// </summary>
    public void SetupEmptyWarehouse(ulong itemId, ulong? marketId)
    {
        MockLotStorage.Setup(x => x.GetLotsForItemAsync(itemId, marketId))
            .ReturnsAsync(new List<WarehouseLot>());
    }

    /// <summary>
    /// Configure dry run mode for testing
    /// </summary>
    public void SetDryRunMode(bool dryRun)
    {
        var config = MockConfigService.Object.Config;
        config.Development.DryRun = dryRun;
    }

    /// <summary>
    /// Enable lot tracking for detailed logging verification
    /// </summary>
    public void EnableLotTracking(bool enabled = true)
    {
        var config = MockConfigService.Object.Config;
        config.Warehouse.EnableLotTracking = enabled;
    }

    /// <summary>
    /// Helper method to create a test lot with default values
    /// </summary>
    public static WarehouseLot CreateTestLot(
        string lotId = "test-lot", 
        ulong itemId = TestItemId, 
        ulong? marketId = TestMarketId,
        long quantity = 1000, 
        long unitCost = 100)
    {
        return new WarehouseLot(lotId, itemId, marketId, quantity, unitCost);
    }

    /// <summary>
    /// Verify that items were added correctly by checking mock calls
    /// </summary>
    public void VerifyItemsAdded(ulong itemId, long quantity, long unitCost, ulong? marketId)
    {
        MockEventService.Verify(x => x.EmitItemsAddedAsync(itemId, quantity, unitCost, marketId, It.IsAny<DateTime?>()), Times.Once);
        MockMetricsService.Verify(x => x.Increment(
            It.Is<string>(s => s == "warehouse_operations_total"), 
            It.IsAny<string[]>(),
            It.IsAny<double>()), Times.Once);
    }

    /// <summary>
    /// Verify that items were consumed correctly by checking mock calls
    /// </summary>
    public void VerifyItemsConsumed(ulong itemId, long consumedQuantity, long weightedAverageCost, ulong? marketId)
    {
        MockEventService.Verify(x => x.EmitItemsConsumedAsync(itemId, consumedQuantity, weightedAverageCost, marketId, It.IsAny<DateTime?>()), Times.Once);
        MockMetricsService.Verify(x => x.Increment(
            It.Is<string>(s => s == "warehouse_consumption_operations_total"), 
            It.IsAny<string[]>(),
            It.IsAny<double>()), Times.Once);
    }

    /// <summary>
    /// Reset all mocks to their default state
    /// </summary>
    public void Reset()
    {
        MockDatabase.Reset();
        MockLogger.Reset();
        MockMetricsService.Reset();
        MockEventService.Reset();
        MockLotStorage.Reset();
        MockConfigService.Reset();
        MockGameplayBank.Reset();
        
        ConfigureDefaultBehavior();
    }

    #region Additional Simulation Helper Methods

    /// <summary>
    /// Verify that empty lots were deleted correctly by checking storage calls and metric updates
    /// </summary>
    public void VerifyLotsDeleted(ulong itemId, ulong? marketId, int expectedDeletedLots = 1)
    {
        // Verify DeleteLotAsync was called for empty lots
        MockLotStorage.Verify(
            x => x.DeleteLotAsync(It.Is<WarehouseLot>(lot => lot.ItemId == itemId && lot.MarketId == marketId && lot.IsEmpty)),
            Times.AtLeast(expectedDeletedLots), 
            $"should have deleted at least {expectedDeletedLots} empty lots for item {itemId} in market {marketId?.ToString() ?? "global"}");

        // Verify cleanup operations were called
        MockLotStorage.Verify(
            x => x.CleanupEmptyLotsAsync(itemId, marketId),
            Times.AtLeastOnce(),
            "should have called cleanup operations for empty lots");

        // Verify metrics were updated for cleanup operations (if applicable)
        MockMetricsService.Verify(
            x => x.Increment("warehouse_lot_operations_total", It.IsAny<string[]>(), It.IsAny<double>()),
            Times.AtLeastOnce(),
            "should have tracked lot operations during cleanup");
    }

    /// <summary>
    /// Set up a complex multi-lot scenario with realistic resource mixes easily
    /// </summary>
    public void SetupMultiLotScenario(ulong itemId, ulong? marketId, MultiLotScenarioType scenarioType = MultiLotScenarioType.Realistic)
    {
        var lots = scenarioType switch
        {
            MultiLotScenarioType.Mined => CreateMinedResourceMix(itemId, marketId),
            MultiLotScenarioType.Purchased => CreatePurchasedResourceMix(itemId, marketId),
            MultiLotScenarioType.Crafted => CreateCraftedResourceMix(itemId, marketId),
            MultiLotScenarioType.Realistic => CreateRealisticResourceMix(itemId, marketId),
            MultiLotScenarioType.Complex => CreateComplexResourceMix(itemId, marketId),
            _ => throw new ArgumentException($"Unknown scenario type: {scenarioType}")
        };

        SetupLotStorageWithLots(itemId, marketId, lots);
    }

    /// <summary>
    /// Verify dry-run operations don't call storage and validate in-memory state consistency
    /// </summary>
    public void VerifyDryRunState(ulong itemId, ulong? marketId)
    {
        // Verify no actual storage operations were performed
        MockLotStorage.Verify(
            x => x.StoreLotAsync(It.IsAny<WarehouseLot>()),
            Times.Never,
            "dry-run mode should never call storage operations");

        MockLotStorage.Verify(
            x => x.UpdateLotQuantityAsync(It.IsAny<string>(), It.IsAny<long>()),
            Times.Never,
            "dry-run mode should never update lot quantities in storage");

        MockLotStorage.Verify(
            x => x.DeleteLotAsync(It.IsAny<WarehouseLot>()),
            Times.Never,
            "dry-run mode should never delete lots from storage");

        // Verify no events were emitted to avoid side effects
        MockEventService.Verify(
            x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Never,
            "dry-run mode should not emit item addition events");

        MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Never,
            "dry-run mode should not emit consumption events");

        // Verify no metrics were emitted to avoid observability side effects
        MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:dry_run" }, It.IsAny<double>()),
            Times.Never,
            "dry-run mode should not emit metrics to avoid side effects");
    }

    /// <summary>
    /// Generate test data that matches real game scenarios with mixed resource types
    /// </summary>
    public WarehouseLot[] CreateRealisticResourceMix(ulong itemId, ulong? marketId)
    {
        // Realistic mix: some mined (free), some purchased (market price), some crafted (high cost)
        return new[]
        {
            // Mined resources (free or very low cost)
            CreateTestLot($"mined-1-{itemId}", itemId, marketId, 5000, 0),     // Free mined
            CreateTestLot($"mined-2-{itemId}", itemId, marketId, 3000, 5),     // Minimal extraction cost
            
            // Purchased resources (market prices)
            CreateTestLot($"bought-1-{itemId}", itemId, marketId, 2000, 150),  // Market purchase
            CreateTestLot($"bought-2-{itemId}", itemId, marketId, 1500, 180),  // Higher market price
            
            // Crafted resources (high cost due to materials + time)
            CreateTestLot($"crafted-1-{itemId}", itemId, marketId, 800, 300),  // Standard craft cost
            CreateTestLot($"crafted-2-{itemId}", itemId, marketId, 400, 450),  // Premium craft cost
            
            // Shipped resources (original cost + freight)
            CreateTestLot($"shipped-{itemId}", itemId, marketId, 600, 250),    // Shipped from another market
        };
    }

    /// <summary>
    /// Create a scenario with only mined resources (zero or very low cost)
    /// </summary>
    public WarehouseLot[] CreateMinedResourceMix(ulong itemId, ulong? marketId)
    {
        return new[]
        {
            CreateTestLot($"mined-rich-{itemId}", itemId, marketId, 10000, 0),   // Rich vein, no cost
            CreateTestLot($"mined-poor-{itemId}", itemId, marketId, 5000, 2),    // Poor vein, extraction cost
            CreateTestLot($"mined-deep-{itemId}", itemId, marketId, 3000, 8),    // Deep vein, higher extraction
        };
    }

    /// <summary>
    /// Create a scenario with purchased resources at various market prices
    /// </summary>
    public WarehouseLot[] CreatePurchasedResourceMix(ulong itemId, ulong? marketId)
    {
        return new[]
        {
            CreateTestLot($"market-cheap-{itemId}", itemId, marketId, 2500, 120),  // Cheap market buy
            CreateTestLot($"market-avg-{itemId}", itemId, marketId, 2000, 160),    // Average market price
            CreateTestLot($"market-exp-{itemId}", itemId, marketId, 1500, 220),    // Expensive market buy
            CreateTestLot($"market-rush-{itemId}", itemId, marketId, 800, 300),    // Rush purchase premium
        };
    }

    /// <summary>
    /// Create a scenario with crafted resources (high costs due to component prices)
    /// </summary>
    public WarehouseLot[] CreateCraftedResourceMix(ulong itemId, ulong? marketId)
    {
        return new[]
        {
            CreateTestLot($"craft-basic-{itemId}", itemId, marketId, 1000, 280),   // Basic craft recipe
            CreateTestLot($"craft-adv-{itemId}", itemId, marketId, 600, 420),      // Advanced recipe
            CreateTestLot($"craft-rare-{itemId}", itemId, marketId, 200, 650),     // Rare component recipe
            CreateTestLot($"craft-mass-{itemId}", itemId, marketId, 1500, 250),    // Mass production batch
        };
    }

    /// <summary>
    /// Create a complex scenario with many lots for testing edge cases and performance
    /// </summary>
    public WarehouseLot[] CreateComplexResourceMix(ulong itemId, ulong? marketId)
    {
        var lots = new List<WarehouseLot>();
        
        // Add realistic mix
        lots.AddRange(CreateRealisticResourceMix(itemId, marketId));
        
        // Add some edge cases
        lots.Add(CreateTestLot($"micro-lot-{itemId}", itemId, marketId, 1, 1000));      // Very small quantity, high cost
        lots.Add(CreateTestLot($"huge-lot-{itemId}", itemId, marketId, 100000, 50));    // Very large quantity, low cost
        lots.Add(CreateTestLot($"zero-cost-{itemId}", itemId, marketId, 5000, 0));      // Zero cost lot
        lots.Add(CreateTestLot($"premium-{itemId}", itemId, marketId, 100, 2000));      // Premium cost lot
        
        return lots.ToArray();
    }

    /// <summary>
    /// Verify comprehensive metrics tracking for complex operations
    /// </summary>
    public void VerifyComprehensiveMetrics(int expectedOperations, int expectedConsumptions = 0, int expectedLotOps = 0)
    {
        // Verify general warehouse operations were tracked
        MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", It.IsAny<string[]>(), It.IsAny<double>()),
            Times.AtLeast(expectedOperations),
            $"should have tracked at least {expectedOperations} warehouse operations");

        if (expectedConsumptions > 0)
        {
            // Verify consumption operations were tracked
            MockMetricsService.Verify(
                x => x.Increment("warehouse_consumption_operations_total", It.IsAny<string[]>(), It.IsAny<double>()),
                Times.AtLeast(expectedConsumptions),
                $"should have tracked at least {expectedConsumptions} consumption operations");
        }

        if (expectedLotOps > 0)
        {
            // Verify lot operations were tracked
            MockMetricsService.Verify(
                x => x.Increment("warehouse_lot_operations_total", It.IsAny<string[]>(), It.IsAny<double>()),
                Times.AtLeast(expectedLotOps),
                $"should have tracked at least {expectedLotOps} lot operations");
        }
    }

    /// <summary>
    /// Verify event emissions for comprehensive operations
    /// </summary>
    public void VerifyComprehensiveEvents(int expectedAdditions, int expectedConsumptions = 0, int expectedLotMerges = 0)
    {
        if (expectedAdditions > 0)
        {
            MockEventService.Verify(
                x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
                Times.Exactly(expectedAdditions),
                $"should have emitted exactly {expectedAdditions} items_added events");
        }

        if (expectedConsumptions > 0)
        {
            MockEventService.Verify(
                x => x.EmitItemsConsumedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
                Times.Exactly(expectedConsumptions),
                $"should have emitted exactly {expectedConsumptions} items_consumed events");
        }

        if (expectedLotMerges > 0)
        {
            MockEventService.Verify(
                x => x.EmitLotMergedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ulong>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
                Times.AtLeast(expectedLotMerges),
                $"should have emitted at least {expectedLotMerges} lot_merged events");
        }
    }

    /// <summary>
    /// Set up a scenario for testing lot merging behavior
    /// </summary>
    public void SetupLotMergingScenario(ulong itemId, ulong? marketId, int maxLotsPerItem = 3)
    {
        // Configure low max lots to force merging
        var config = MockConfigService.Object.Config;
        config.Warehouse.MaxLotsPerItem = maxLotsPerItem;

        // Create lots with similar costs to encourage merging
        var lots = new[]
        {
            CreateTestLot($"merge-candidate-1-{itemId}", itemId, marketId, 1000, 100),
            CreateTestLot($"merge-candidate-2-{itemId}", itemId, marketId, 800, 105),
            CreateTestLot($"merge-candidate-3-{itemId}", itemId, marketId, 600, 110),
        };

        SetupLotStorageWithLots(itemId, marketId, lots);
    }

    /// <summary>
    /// Verify that storage operations maintain data consistency
    /// </summary>
    public void VerifyStorageConsistency(ulong itemId, ulong? marketId)
    {
        // Verify that item summaries were updated after storage operations
        MockLotStorage.Verify(
            x => x.StoreItemSummaryAsync(It.Is<WarehouseLotSummary>(s => s.ItemId == itemId && s.MarketId == marketId)),
            Times.AtLeastOnce(),
            "should update item summaries to maintain consistency");

        // Verify no orphaned operations (lots stored but not tracked)
        MockLotStorage.Verify(
            x => x.StoreLotAsync(It.IsAny<WarehouseLot>()),
            Times.AtLeastOnce());
    }

    #endregion
}
