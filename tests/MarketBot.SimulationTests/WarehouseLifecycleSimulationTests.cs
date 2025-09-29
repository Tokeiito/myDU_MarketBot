using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.Domain;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// End-to-end warehouse lifecycle simulation tests - validates complete workflows from creation to cleanup
/// Tests critical business processes for inventory management including multi-step operations and error recovery
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "High")]
public class WarehouseLifecycleSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task CompleteLifecycle_AddConsumeCleanup_WorkflowSucceeds()
    {
        // Arrange - setup warehouse service with multiple lots
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false); // Production mode for full storage interactions
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - Complete lifecycle: Add → Consume → Cleanup
        
        // Phase 1: Add multiple lots with different costs
        var addResult1 = await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId); // Cheap resources
        var addResult2 = await warehouseService.AddItems(TestItemId, 800, 150, TestMarketId);  // Medium cost
        var addResult3 = await warehouseService.AddItems(TestItemId, 600, 200, TestMarketId);  // Expensive crafted

        // Setup storage to simulate lots being persisted
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("lot1", TestItemId, TestMarketId, 1000, 100);
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("lot2", TestItemId, TestMarketId, 800, 150);
        var lot3 = WarehouseServiceTestFixture.CreateTestLot("lot3", TestItemId, TestMarketId, 600, 200);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2, lot3);

        var initialQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 2: Consume all items using FIFO logic
        var consumeResult = await warehouseService.TakeItems(TestItemId, 2400, TestMarketId); // Consume everything

        // Setup empty storage after consumption
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        var finalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 3: Cleanup empty lots
        var cleanupResult = await warehouseService.CleanupEmptyLots(TestItemId, TestMarketId);

        // Assert - Verify complete lifecycle worked correctly
        addResult1.Should().BeTrue("first addition should succeed");
        addResult2.Should().BeTrue("second addition should succeed");
        addResult3.Should().BeTrue("third addition should succeed");

        initialQuantity.Should().Be(2400, "should have total of all added quantities");

        consumeResult.Success.Should().BeTrue("consumption of all items should succeed");
        consumeResult.ConsumedQuantity.Should().Be(2400, "should consume all available items");

        // Expected weighted average: (1000*100 + 800*150 + 600*200) / 2400 = 340000 / 2400 = 141.67 ≈ 141
        var expectedWeightedAverage = (1000L * 100 + 800L * 150 + 600L * 200) / 2400L;
        consumeResult.WeightedAverageCost.Should().Be(expectedWeightedAverage, 
            "weighted average should reflect FIFO consumption across all lots");

        finalQuantity.Should().Be(0, "no items should remain after consuming everything");

        // Verify storage operations occurred as expected
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<WarehouseLot>()), 
            Times.AtLeast(3), "should have stored multiple lots during additions");
        fixture.MockLotStorage.Verify(x => x.CleanupEmptyLotsAsync(TestItemId, TestMarketId), 
            Times.Once, "should have cleaned up empty lots");
    }

    [Fact]
    public async Task PartialConsumption_Lifecycle_LotUpdatesCorrectly()
    {
        // Arrange - setup multiple lots for partial consumption testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - Add items and perform partial consumption
        
        // Phase 1: Add three lots
        await warehouseService.AddItems(TestItemId, 2000, 80, TestMarketId);   // Large cheap lot
        await warehouseService.AddItems(TestItemId, 1000, 120, TestMarketId);  // Medium cost lot
        await warehouseService.AddItems(TestItemId, 500, 180, TestMarketId);   // Small expensive lot

        // Setup initial storage state
        var initialLot1 = WarehouseServiceTestFixture.CreateTestLot("initial1", TestItemId, TestMarketId, 2000, 80);
        var initialLot2 = WarehouseServiceTestFixture.CreateTestLot("initial2", TestItemId, TestMarketId, 1000, 120);
        var initialLot3 = WarehouseServiceTestFixture.CreateTestLot("initial3", TestItemId, TestMarketId, 500, 180);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, initialLot1, initialLot2, initialLot3);

        var initialQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 2: Partial consumption (consume 2500 items - will consume all of lot1 + half of lot2)
        var partialConsumeResult = await warehouseService.TakeItems(TestItemId, 2500, TestMarketId);

        // Setup storage state after partial consumption
        var remainingLot2 = WarehouseServiceTestFixture.CreateTestLot("remaining2", TestItemId, TestMarketId, 500, 120); // 1000 - 500 = 500 remaining
        var remainingLot3 = WarehouseServiceTestFixture.CreateTestLot("remaining3", TestItemId, TestMarketId, 500, 180);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, remainingLot2, remainingLot3);

        var partialQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 3: Consume remaining items
        var finalConsumeResult = await warehouseService.TakeItems(TestItemId, 1000, TestMarketId);

        // Setup final empty state
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        var finalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 4: Cleanup empty lots
        var cleanupResult = await warehouseService.CleanupEmptyLots(TestItemId, TestMarketId);

        // Assert - Verify partial consumption lifecycle
        initialQuantity.Should().Be(3500, "should have total of all added items");

        partialConsumeResult.Success.Should().BeTrue("partial consumption should succeed");
        partialConsumeResult.ConsumedQuantity.Should().Be(2500, "should consume requested partial quantity");

        // Expected weighted average for partial consumption: (2000*80 + 500*120) / 2500 = 220000 / 2500 = 88
        var expectedPartialCost = (2000L * 80 + 500L * 120) / 2500L;
        partialConsumeResult.WeightedAverageCost.Should().Be(expectedPartialCost,
            "should calculate correct weighted average for partial consumption");

        partialQuantity.Should().Be(1000, "should have correct remaining quantity after partial consumption");

        finalConsumeResult.Success.Should().BeTrue("final consumption should succeed");
        finalConsumeResult.ConsumedQuantity.Should().Be(1000, "should consume all remaining items");

        finalQuantity.Should().Be(0, "no items should remain after complete consumption");

        // Verify lot operations occurred
        fixture.MockLotStorage.Verify(x => x.UpdateLotQuantityAsync(It.IsAny<string>(), It.IsAny<long>()), 
            Times.AtLeastOnce, "should have updated lot quantities during partial consumption");
        fixture.MockLotStorage.Verify(x => x.DeleteLotAsync(It.IsAny<WarehouseLot>()), 
            Times.AtLeastOnce, "should have deleted empty lots during consumption");
    }

    [Fact]
    public async Task MultipleItems_MultipleMarkets_ComplexLifecycle()
    {
        // Arrange - setup complex scenario with multiple items and markets
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        const ulong item1 = 1001, item2 = 1002;
        const ulong market1 = 1, market2 = 2;

        fixture.SetupEmptyWarehouse(item1, market1);
        fixture.SetupEmptyWarehouse(item1, market2);
        fixture.SetupEmptyWarehouse(item2, market1);
        fixture.SetupEmptyWarehouse(item2, market2);

        // Act - Complex multi-item, multi-market lifecycle

        // Phase 1: Add items across different markets
        var results = new List<bool>
        {
            await warehouseService.AddItems(item1, 1000, 100, market1),
            await warehouseService.AddItems(item1, 800, 120, market2),
            await warehouseService.AddItems(item2, 600, 150, market1),
            await warehouseService.AddItems(item2, 400, 180, market2),
        };

        // Setup storage for all combinations
        var lot1m1 = WarehouseServiceTestFixture.CreateTestLot("lot1m1", item1, market1, 1000, 100);
        var lot1m2 = WarehouseServiceTestFixture.CreateTestLot("lot1m2", item1, market2, 800, 120);
        var lot2m1 = WarehouseServiceTestFixture.CreateTestLot("lot2m1", item2, market1, 600, 150);
        var lot2m2 = WarehouseServiceTestFixture.CreateTestLot("lot2m2", item2, market2, 400, 180);
        
        fixture.SetupLotStorageWithLots(item1, market1, lot1m1);
        fixture.SetupLotStorageWithLots(item1, market2, lot1m2);
        fixture.SetupLotStorageWithLots(item2, market1, lot2m1);
        fixture.SetupLotStorageWithLots(item2, market2, lot2m2);

        // Verify quantities per market
        var quantities = new Dictionary<string, long>
        {
            ["item1_market1"] = await warehouseService.GetAvailableQuantity(item1, market1),
            ["item1_market2"] = await warehouseService.GetAvailableQuantity(item1, market2),
            ["item2_market1"] = await warehouseService.GetAvailableQuantity(item2, market1),
            ["item2_market2"] = await warehouseService.GetAvailableQuantity(item2, market2),
        };

        // Phase 2: Selective consumption across markets
        var consumption1 = await warehouseService.TakeItems(item1, 500, market1); // Partial from item1/market1
        var consumption2 = await warehouseService.TakeItems(item2, 600, market1); // All from item2/market1

        // Update storage state after consumption
        var updatedLot1m1 = WarehouseServiceTestFixture.CreateTestLot("updated1m1", item1, market1, 500, 100); // 1000 - 500 = 500
        fixture.SetupLotStorageWithLots(item1, market1, updatedLot1m1);
        fixture.SetupEmptyWarehouse(item2, market1); // Consumed completely

        // Verify post-consumption quantities
        var postConsumptionQuantities = new Dictionary<string, long>
        {
            ["item1_market1"] = await warehouseService.GetAvailableQuantity(item1, market1),
            ["item1_market2"] = await warehouseService.GetAvailableQuantity(item1, market2),
            ["item2_market1"] = await warehouseService.GetAvailableQuantity(item2, market1),
            ["item2_market2"] = await warehouseService.GetAvailableQuantity(item2, market2),
        };

        // Phase 3: Cleanup operations per market
        var cleanupResults = new Dictionary<string, int>
        {
            ["item1_market1"] = await warehouseService.CleanupEmptyLots(item1, market1),
            ["item1_market2"] = await warehouseService.CleanupEmptyLots(item1, market2),
            ["item2_market1"] = await warehouseService.CleanupEmptyLots(item2, market1),
            ["item2_market2"] = await warehouseService.CleanupEmptyLots(item2, market2),
        };

        // Assert - Verify complex lifecycle results
        results.Should().AllSatisfy(result => result.Should().BeTrue("all additions should succeed"));

        // Verify initial quantities
        quantities["item1_market1"].Should().Be(1000, "item1 in market1 should have correct initial quantity");
        quantities["item1_market2"].Should().Be(800, "item1 in market2 should have correct initial quantity");
        quantities["item2_market1"].Should().Be(600, "item2 in market1 should have correct initial quantity");
        quantities["item2_market2"].Should().Be(400, "item2 in market2 should have correct initial quantity");

        // Verify consumption results
        consumption1.Success.Should().BeTrue("partial consumption should succeed");
        consumption1.ConsumedQuantity.Should().Be(500, "should consume requested partial quantity");
        consumption1.WeightedAverageCost.Should().Be(100, "single-lot consumption should maintain unit cost");

        consumption2.Success.Should().BeTrue("complete consumption should succeed");
        consumption2.ConsumedQuantity.Should().Be(600, "should consume all available quantity");
        consumption2.WeightedAverageCost.Should().Be(150, "single-lot consumption should maintain unit cost");

        // Verify post-consumption state
        postConsumptionQuantities["item1_market1"].Should().Be(500, "item1/market1 should have reduced quantity");
        postConsumptionQuantities["item1_market2"].Should().Be(800, "item1/market2 should be unchanged");
        postConsumptionQuantities["item2_market1"].Should().Be(0, "item2/market1 should be empty");
        postConsumptionQuantities["item2_market2"].Should().Be(400, "item2/market2 should be unchanged");

        // Verify storage operations for complex scenario
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<WarehouseLot>()), 
            Times.AtLeast(4), "should have stored lots for all item/market combinations");
    }

    [Fact]
    public async Task ErrorRecovery_MidLifecycle_GracefulHandling()
    {
        // Arrange - setup scenario that will encounter errors during lifecycle
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Setup storage to fail on certain operations to simulate errors
        fixture.MockLotStorage.Setup(x => x.UpdateLotQuantityAsync("failing-lot", It.IsAny<long>()))
                            .ReturnsAsync(false); // Simulate update failure

        // Act - Lifecycle with error conditions

        // Phase 1: Normal operations (should succeed)
        var normalAdd = await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);
        
        var normalLot = WarehouseServiceTestFixture.CreateTestLot("normal-lot", TestItemId, TestMarketId, 1000, 100);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, normalLot);

        var initialQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 2: Operations that might encounter errors
        var normalConsume = await warehouseService.TakeItems(TestItemId, 300, TestMarketId);

        // Update storage to reflect consumption
        var updatedLot = WarehouseServiceTestFixture.CreateTestLot("updated-lot", TestItemId, TestMarketId, 700, 100);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, updatedLot);

        var midQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 3: Attempt invalid operations (should fail gracefully)
        var invalidAdd = await warehouseService.AddItems(TestItemId, -100, 50, TestMarketId); // Negative quantity
        var invalidConsume = await warehouseService.TakeItems(TestItemId, -50, TestMarketId); // Negative quantity
        var overconsume = await warehouseService.TakeItems(TestItemId, 10000, TestMarketId); // More than available

        var postErrorQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 4: Recovery operations (should work normally)
        var recoveryAdd = await warehouseService.AddItems(TestItemId, 300, 100, TestMarketId);
        
        // Update storage to reflect recovery - adding 300 to 0 existing = 300 total
        var recoveryLot = WarehouseServiceTestFixture.CreateTestLot("recovery-lot", TestItemId, TestMarketId, 300, 100);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, recoveryLot);

        var finalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - Verify error recovery behavior
        normalAdd.Should().BeTrue("normal addition should succeed");
        initialQuantity.Should().Be(1000, "initial quantity should be correct");

        normalConsume.Success.Should().BeTrue("normal consumption should succeed");
        normalConsume.ConsumedQuantity.Should().Be(300, "should consume requested quantity");
        normalConsume.WeightedAverageCost.Should().Be(100, "should maintain correct cost");
        midQuantity.Should().Be(700, "quantity should be reduced after normal consumption");

        // Verify error conditions are handled gracefully
        invalidAdd.Should().BeFalse("negative quantity addition should fail");
        invalidConsume.Success.Should().BeFalse("negative quantity consumption should fail");
        
        // Over-consumption should return partial success with available quantity
        overconsume.Success.Should().BeTrue("over-consumption should return partial success");
        overconsume.ConsumedQuantity.Should().Be(700, "should consume all available quantity");

        postErrorQuantity.Should().Be(0, "quantity should be zero after over-consumption");

        // Verify recovery operations work
        recoveryAdd.Should().BeTrue("recovery addition should succeed");
        finalQuantity.Should().Be(300, "should recover to expected quantity");

        // Verify appropriate logging occurred for error conditions
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<System.Exception?>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.AtLeast(2), "should log warnings for invalid operations");
    }

    [Fact]
    public async Task DryRun_CompleteLifecycle_NoStorageSideEffects()
    {
        // Arrange - test complete lifecycle in dry-run mode
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(true); // Enable dry-run mode
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - Complete lifecycle in dry-run mode

        // Phase 1: Add items (should work in memory only)
        var addResult1 = await warehouseService.AddItems(TestItemId, 1500, 80, TestMarketId);
        var addResult2 = await warehouseService.AddItems(TestItemId, 1000, 120, TestMarketId);
        
        var initialQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 2: Partial consumption (should work in memory only)
        var consumeResult = await warehouseService.TakeItems(TestItemId, 2000, TestMarketId);
        
        var postConsumeQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 3: Final consumption (should work in memory only)
        var finalConsumeResult = await warehouseService.TakeItems(TestItemId, 500, TestMarketId);
        
        var finalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Phase 4: Cleanup attempt (should be no-op in dry-run)
        var cleanupResult = await warehouseService.CleanupEmptyLots(TestItemId, TestMarketId);

        // Assert - Verify dry-run lifecycle works without storage side effects
        addResult1.Should().BeTrue("dry-run addition should succeed");
        addResult2.Should().BeTrue("dry-run addition should succeed");
        initialQuantity.Should().Be(2500, "dry-run should track in-memory inventory correctly");

        consumeResult.Success.Should().BeTrue("dry-run consumption should succeed");
        consumeResult.ConsumedQuantity.Should().Be(2000, "should consume requested quantity in memory");
        
        // Expected weighted average: (1500*80 + 500*120) / 2000 = 180000 / 2000 = 90
        var expectedCost = (1500L * 80 + 500L * 120) / 2000L;
        consumeResult.WeightedAverageCost.Should().Be(expectedCost, 
            "should calculate correct FIFO cost in dry-run mode");

        postConsumeQuantity.Should().Be(500, "should have correct remaining quantity in memory");

        finalConsumeResult.Success.Should().BeTrue("final dry-run consumption should succeed");
        finalConsumeResult.ConsumedQuantity.Should().Be(500, "should consume all remaining items");
        finalQuantity.Should().Be(0, "should have no items remaining in memory");

        // Most importantly: verify NO storage operations were called for actual data manipulation
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<WarehouseLot>()), 
            Times.Never, "dry-run mode should never call storage operations");
        fixture.MockLotStorage.Verify(x => x.UpdateLotQuantityAsync(It.IsAny<string>(), It.IsAny<long>()), 
            Times.Never, "dry-run mode should never update storage");
        fixture.MockLotStorage.Verify(x => x.DeleteLotAsync(It.IsAny<WarehouseLot>()), 
            Times.Never, "dry-run mode should never delete from storage");
        
        // Note: CleanupEmptyLotsAsync might still be called as it's a direct pass-through method
        // This is acceptable as it doesn't affect in-memory dry-run state
    }
}