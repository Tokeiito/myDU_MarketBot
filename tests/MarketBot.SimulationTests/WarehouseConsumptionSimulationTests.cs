using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// FIFO consumption simulation tests - validates critical consumption business logic end-to-end
/// Tests the core inventory consumption workflow: cheapest lots consumed first with correct cost calculations
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Medium")]
public class WarehouseConsumptionSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task TakeItems_FIFOConsumption_ShouldConsumeCheapestLotsFirst()
    {
        // Arrange - create warehouse service with multiple lots at different costs
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        // Setup multiple lots with different costs (simulating mined, purchased, crafted resources)
        var minedLot = WarehouseServiceTestFixture.CreateTestLot(
            "mined-lot", TestItemId, TestMarketId, quantity: 1000, unitCost: 0); // Free mined resources
        var purchasedLot = WarehouseServiceTestFixture.CreateTestLot(
            "purchased-lot", TestItemId, TestMarketId, quantity: 500, unitCost: 100); // Market purchased
        var craftedLot = WarehouseServiceTestFixture.CreateTestLot(
            "crafted-lot", TestItemId, TestMarketId, quantity: 300, unitCost: 200); // High-cost crafted
        
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, minedLot, purchasedLot, craftedLot);

        // Act - consume 1200 items (should take all mined + 200 from purchased)
        var result = await warehouseService.TakeItems(TestItemId, 1200, TestMarketId);

        // Assert - verify FIFO consumption and weighted average cost
        result.Success.Should().BeTrue("consumption should succeed with sufficient inventory");
        result.ConsumedQuantity.Should().Be(1200, "should consume exactly the requested quantity");
        
        // Expected weighted average: (1000 * 0 + 200 * 100) / 1200 = 20000 / 1200 = 16.67 (rounded to 16)
        var expectedWeightedCost = (1000L * 0 + 200L * 100) / 1200L; // Should be 16
        result.WeightedAverageCost.Should().Be(expectedWeightedCost, 
            "should calculate correct weighted average cost for FIFO consumption");

        // Verify the expected service interactions occurred
        fixture.VerifyItemsConsumed(TestItemId, 1200, expectedWeightedCost, TestMarketId);
    }

    [Fact]
    public async Task TakeItems_PartialConsumption_WhenInsufficientInventory()
    {
        // Arrange - create warehouse with limited inventory
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        // Setup only 1000 items available
        var availableLot = WarehouseServiceTestFixture.CreateTestLot(
            "limited-lot", TestItemId, TestMarketId, quantity: 1000, unitCost: 150);
        
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, availableLot);

        // Act - try to consume 1500 items (more than available)
        var result = await warehouseService.TakeItems(TestItemId, 1500, TestMarketId);

        // Assert - should partially consume what's available
        result.Success.Should().BeTrue("should succeed even with partial consumption");
        result.ConsumedQuantity.Should().Be(1000, "should consume only what's available");
        result.WeightedAverageCost.Should().Be(150, "should return correct cost for consumed items");

        // Verify consumption was recorded correctly
        fixture.VerifyItemsConsumed(TestItemId, 1000, 150, TestMarketId);
    }

    [Fact]
    public async Task TakeItems_ComplexFIFOScenario_MultipleLotsAndCosts()
    {
        // Arrange - create complex scenario with 5 lots at different costs and times
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        // Setup lots with same costs but different creation times to test secondary sort
        var earlierTime = System.DateTime.UtcNow.AddMinutes(-20);
        var laterTime = System.DateTime.UtcNow.AddMinutes(-10);
        
        // Lots with different costs and creation times
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("lot1", TestItemId, TestMarketId, 500, 0);   // Cheapest
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("lot2", TestItemId, TestMarketId, 300, 50);  // Second cheapest  
        var lot3 = WarehouseServiceTestFixture.CreateTestLot("lot3", TestItemId, TestMarketId, 400, 100); // Mid cost
        var lot4 = WarehouseServiceTestFixture.CreateTestLot("lot4", TestItemId, TestMarketId, 200, 100); // Same cost as lot3
        var lot5 = WarehouseServiceTestFixture.CreateTestLot("lot5", TestItemId, TestMarketId, 600, 200); // Most expensive
        
        // Set creation times to test secondary sorting (by time when costs are equal)
        lot3.CreatedAt = earlierTime; // Should be consumed before lot4 (same cost, earlier time)
        lot4.CreatedAt = laterTime;
        
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2, lot3, lot4, lot5);

        // Act - consume 1100 items (should consume lot1=500, lot2=300, lot3=300 of 400)
        var result = await warehouseService.TakeItems(TestItemId, 1100, TestMarketId);

        // Assert - verify correct FIFO consumption order and cost calculation
        result.Success.Should().BeTrue("consumption should succeed");
        result.ConsumedQuantity.Should().Be(1100, "should consume requested quantity");
        
        // Expected cost: (500*0 + 300*50 + 300*100) / 1100 = (0 + 15000 + 30000) / 1100 = 45000/1100 = 40.9 ≈ 40
        var expectedCost = (500L * 0 + 300L * 50 + 300L * 100) / 1100L; // Should be 40
        result.WeightedAverageCost.Should().Be(expectedCost, 
            "should calculate weighted average correctly for complex FIFO scenario");

        fixture.VerifyItemsConsumed(TestItemId, 1100, expectedCost, TestMarketId);
    }

    [Fact]
    public async Task TakeItems_EmptyWarehouse_ShouldReturnFailure()
    {
        // Arrange - completely empty warehouse
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - try to consume items from empty warehouse
        var result = await warehouseService.TakeItems(TestItemId, 100, TestMarketId);

        // Assert - should return meaningful failure indicators
        result.Success.Should().BeFalse("should fail when no inventory available");
        result.ConsumedQuantity.Should().Be(0, "should consume nothing from empty warehouse");
        result.WeightedAverageCost.Should().Be(0, "should return zero cost when nothing consumed");

        // Verify no consumption event was emitted
        fixture.MockEventService.Verify(x => x.EmitItemsConsumedAsync(
            It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<System.DateTime?>()), 
            Times.Never, "should not emit consumption event when nothing consumed");
    }

    [Fact]
    public async Task TakeItems_ZeroCostAndPaidLots_CorrectFIFOOrder()
    {
        // Arrange - mixed zero-cost and paid lots (common real-world scenario)
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        var freeLot = WarehouseServiceTestFixture.CreateTestLot(
            "free-mined", TestItemId, TestMarketId, quantity: 800, unitCost: 0); // Mined resources
        var expensiveLot = WarehouseServiceTestFixture.CreateTestLot(
            "expensive-crafted", TestItemId, TestMarketId, quantity: 400, unitCost: 500); // Crafted items
        var cheapLot = WarehouseServiceTestFixture.CreateTestLot(
            "cheap-purchased", TestItemId, TestMarketId, quantity: 300, unitCost: 75); // Market purchase
        
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, freeLot, expensiveLot, cheapLot);

        // Act - consume 1000 items (should take: 800 free + 200 from cheap lot)
        var result = await warehouseService.TakeItems(TestItemId, 1000, TestMarketId);

        // Assert - verify zero-cost items consumed first, then cheapest paid
        result.Success.Should().BeTrue("consumption should succeed");
        result.ConsumedQuantity.Should().Be(1000, "should consume requested quantity");
        
        // Expected cost: (800*0 + 200*75) / 1000 = 15000 / 1000 = 15
        var expectedCost = (800L * 0 + 200L * 75) / 1000L; // Should be 15
        result.WeightedAverageCost.Should().Be(expectedCost, 
            "should prioritize zero-cost items then cheapest paid items");

        fixture.VerifyItemsConsumed(TestItemId, 1000, expectedCost, TestMarketId);
    }

    [Fact]
    public async Task TakeItems_SingleLot_FullConsumption()
    {
        // Arrange - single lot scenario
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        const long lotQuantity = 2000;
        const long unitCost = 125;
        
        var singleLot = WarehouseServiceTestFixture.CreateTestLot(
            "single-lot", TestItemId, TestMarketId, quantity: lotQuantity, unitCost: unitCost);
        
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, singleLot);

        // Act - consume entire lot
        var result = await warehouseService.TakeItems(TestItemId, lotQuantity, TestMarketId);

        // Assert - should consume entire lot at its unit cost
        result.Success.Should().BeTrue("should successfully consume entire lot");
        result.ConsumedQuantity.Should().Be(lotQuantity, "should consume full lot quantity");
        result.WeightedAverageCost.Should().Be(unitCost, 
            "weighted average should equal unit cost for single lot");

        fixture.VerifyItemsConsumed(TestItemId, lotQuantity, unitCost, TestMarketId);
    }
}