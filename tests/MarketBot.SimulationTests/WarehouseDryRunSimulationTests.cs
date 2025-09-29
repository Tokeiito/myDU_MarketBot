using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Dry-run mode simulation tests - validates that dry-run operations work correctly without persisting to storage
/// Tests critical functionality for safe operation testing and cost estimation without side effects
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Medium")]
public class WarehouseDryRunSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task DryRun_AddAndTakeItems_InMemoryStateOnly()
    {
        // Arrange - create warehouse service in dry-run mode
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(true); // Enable dry-run mode
        var warehouseService = fixture.CreateWarehouseService();

        // Setup empty warehouse initially
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - perform operations in dry-run mode
        var addResult = await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);
        var availableQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);
        var takeResult = await warehouseService.TakeItems(TestItemId, 300, TestMarketId);
        var finalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - operations should work but no storage calls should be made
        addResult.Should().BeTrue("dry-run add should succeed");
        availableQuantity.Should().Be(1000, "dry-run should track in-memory inventory");

        takeResult.Success.Should().BeTrue("dry-run consumption should succeed");
        takeResult.ConsumedQuantity.Should().Be(300, "should consume requested quantity in dry-run");
        takeResult.WeightedAverageCost.Should().Be(100, "should calculate correct cost in dry-run");

        finalQuantity.Should().Be(700, "dry-run should update in-memory inventory after consumption");

        // Verify that storage operations were never called in dry-run mode
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "dry-run should not persist lots to storage");
        fixture.MockLotStorage.Verify(x => x.UpdateLotQuantityAsync(It.IsAny<string>(), It.IsAny<long>()), 
            Times.Never, "dry-run should not update lot quantities in storage");
        fixture.MockLotStorage.Verify(x => x.DeleteLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "dry-run should not delete lots from storage");

        // Note: Events are not emitted in dry-run mode based on current implementation
        // This is expected behavior to avoid side effects during simulation
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 1000, 100, TestMarketId, It.IsAny<System.DateTime?>()), 
            Times.Never, "dry-run mode should not emit events to avoid side effects");
        fixture.MockEventService.Verify(x => x.EmitItemsConsumedAsync(TestItemId, 300, 100, TestMarketId, It.IsAny<System.DateTime?>()), 
            Times.Never, "dry-run should not emit consumption events to avoid side effects");
    }

    [Fact]
    public async Task DryRun_vs_Production_SameOperations_CompareResults()
    {
        // Arrange - create two identical scenarios, one dry-run and one production
        
        // Production mode scenario
        var productionFixture = new WarehouseServiceTestFixture();
        productionFixture.SetDryRunMode(false);
        var productionService = productionFixture.CreateWarehouseService();
        productionFixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Dry-run mode scenario
        var dryRunFixture = new WarehouseServiceTestFixture();
        dryRunFixture.SetDryRunMode(true);
        var dryRunService = dryRunFixture.CreateWarehouseService();
        dryRunFixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - perform identical operations in both modes
        const long quantity = 2000;
        const long unitCost = 150;
        const long consumeQuantity = 800;

        // Add items in both modes
        var productionAddResult = await productionService.AddItems(TestItemId, quantity, unitCost, TestMarketId);
        var dryRunAddResult = await dryRunService.AddItems(TestItemId, quantity, unitCost, TestMarketId);

        // Setup production storage to simulate items being persisted and available
        var productionLot = WarehouseServiceTestFixture.CreateTestLot(
            "production-lot", TestItemId, TestMarketId, quantity, unitCost);
        productionFixture.SetupLotStorageWithLots(TestItemId, TestMarketId, productionLot);

        // Check quantities in both modes
        var productionQuantity = await productionService.GetAvailableQuantity(TestItemId, TestMarketId);
        var dryRunQuantity = await dryRunService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Consume items in both modes
        var productionConsumeResult = await productionService.TakeItems(TestItemId, consumeQuantity, TestMarketId);
        var dryRunConsumeResult = await dryRunService.TakeItems(TestItemId, consumeQuantity, TestMarketId);

        // Update production storage to reflect consumption
        var updatedProductionLot = WarehouseServiceTestFixture.CreateTestLot(
            "production-lot-updated", TestItemId, TestMarketId, quantity - consumeQuantity, unitCost);
        productionFixture.SetupLotStorageWithLots(TestItemId, TestMarketId, updatedProductionLot);

        // Final quantities in both modes
        var productionFinalQuantity = await productionService.GetAvailableQuantity(TestItemId, TestMarketId);
        var dryRunFinalQuantity = await dryRunService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - business logic results should be identical
        productionAddResult.Should().Be(dryRunAddResult, "add results should be identical");
        productionQuantity.Should().Be(dryRunQuantity, "quantities after add should be identical");
        
        productionConsumeResult.Success.Should().Be(dryRunConsumeResult.Success, "consume success should be identical");
        productionConsumeResult.ConsumedQuantity.Should().Be(dryRunConsumeResult.ConsumedQuantity, "consumed quantities should be identical");
        productionConsumeResult.WeightedAverageCost.Should().Be(dryRunConsumeResult.WeightedAverageCost, "weighted average costs should be identical");
        
        productionFinalQuantity.Should().Be(dryRunFinalQuantity, "final quantities should be identical");

        // Verify different storage behavior
        // Production should attempt storage operations (StoreLotAsync is called twice: once in CreateNewLotAsync, once in AddItems)
        productionFixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Exactly(2), "production mode should store lots");
        
        // Dry-run should not attempt storage operations
        dryRunFixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "dry-run mode should not store lots");

        // Production should emit events, but dry-run should not to avoid side effects
        productionFixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, quantity, unitCost, TestMarketId, It.IsAny<System.DateTime?>()), Times.Once);
        dryRunFixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, quantity, unitCost, TestMarketId, It.IsAny<System.DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task DryRun_FIFO_Logic_CorrectInMemoryConsumption()
    {
        // Arrange - setup dry-run mode with multiple lots for FIFO testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(true);
        var warehouseService = fixture.CreateWarehouseService();
        
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - add multiple lots with different costs in dry-run mode
        await warehouseService.AddItems(TestItemId, 1000, 0, TestMarketId);   // Free mined resources
        await warehouseService.AddItems(TestItemId, 800, 100, TestMarketId);  // Cheap purchased
        await warehouseService.AddItems(TestItemId, 600, 200, TestMarketId);  // Expensive crafted

        // Verify total quantity
        var totalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);
        totalQuantity.Should().Be(2400, "should track total quantity across multiple dry-run additions");

        // Consume items using FIFO logic (should consume cheapest first)
        var consumeResult = await warehouseService.TakeItems(TestItemId, 1500, TestMarketId); // Will consume: 1000@0 + 500@100

        // Assert - verify FIFO consumption worked correctly in dry-run
        consumeResult.Success.Should().BeTrue("dry-run FIFO consumption should succeed");
        consumeResult.ConsumedQuantity.Should().Be(1500, "should consume requested quantity");
        
        // Expected weighted average: (1000*0 + 500*100) / 1500 = 50000 / 1500 = 33.33 ≈ 33
        var expectedCost = (1000L * 0 + 500L * 100) / 1500L; // Should be 33
        consumeResult.WeightedAverageCost.Should().Be(expectedCost, 
            "should calculate correct FIFO weighted average in dry-run");

        // Verify remaining quantity
        var remainingQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);
        remainingQuantity.Should().Be(900, "should have correct remaining quantity after dry-run consumption"); // 2400 - 1500 = 900

        // Verify no storage operations occurred
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "dry-run FIFO should not persist to storage");
    }

    [Fact]
    public async Task DryRun_StateIsolation_NoLeakageBetweenModes()
    {
        // Arrange - test that dry-run state doesn't affect production operations
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - perform operations in production mode first
        fixture.SetDryRunMode(false);
        var productionAddResult = await warehouseService.AddItems(TestItemId, 500, 120, TestMarketId);
        
        // Setup production lot to be available
        var productionLot = WarehouseServiceTestFixture.CreateTestLot(
            "production-initial", TestItemId, TestMarketId, 500, 120);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, productionLot);
        
        var productionQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Switch to dry-run mode and perform different operations
        fixture.SetDryRunMode(true);
        var dryRunAddResult = await warehouseService.AddItems(TestItemId, 2000, 80, TestMarketId);
        var dryRunQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Switch back to production mode
        fixture.SetDryRunMode(false);
        
        // Setup the production lot to be available for the final check
        var finalProductionLot = WarehouseServiceTestFixture.CreateTestLot(
            "production-lot", TestItemId, TestMarketId, quantity: 500, unitCost: 120);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, finalProductionLot);
        
        var finalProductionQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - modes should be completely isolated
        productionAddResult.Should().BeTrue("production add should succeed");
        dryRunAddResult.Should().BeTrue("dry-run add should succeed");

        // Dry-run operations should not affect production state
        productionQuantity.Should().Be(500, "initial production quantity should be correct");
        dryRunQuantity.Should().Be(2000, "dry-run should track its own inventory");
        finalProductionQuantity.Should().Be(500, "production quantity should be unchanged by dry-run operations");

        // Verify only production mode called storage (StoreLotAsync is called twice for new lot creation)
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Exactly(2), "only production mode should have stored a lot");
    }


}