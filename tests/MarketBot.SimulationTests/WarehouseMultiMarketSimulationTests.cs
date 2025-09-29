using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Multi-market inventory isolation simulation tests - validates that inventory is properly isolated between game markets
/// Tests critical business requirement: items in different markets must not cross-contaminate
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Medium")]
public class WarehouseMultiMarketSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong Market1 = 1;
    private const ulong Market2 = 2;
    private const ulong Market3 = 3;

    [Fact]
    public async Task AddItems_DifferentMarkets_ShouldIsolateInventory()
    {
        // Arrange - create warehouse service
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        // Start with empty warehouses in all markets
        fixture.SetupEmptyWarehouse(TestItemId, Market1);
        fixture.SetupEmptyWarehouse(TestItemId, Market2);
        fixture.SetupEmptyWarehouse(TestItemId, null); // Global market

        // Act - add same item to different markets with different quantities and costs
        var market1Result = await warehouseService.AddItems(TestItemId, 1000, 100, Market1);
        var market2Result = await warehouseService.AddItems(TestItemId, 2000, 150, Market2);
        var globalResult = await warehouseService.AddItems(TestItemId, 500, 200, null); // Global market

        // Simulate the lots being available after adding
        var market1Lot = WarehouseServiceTestFixture.CreateTestLot("m1-lot", TestItemId, Market1, 1000, 100);
        var market2Lot = WarehouseServiceTestFixture.CreateTestLot("m2-lot", TestItemId, Market2, 2000, 150);
        var globalLot = WarehouseServiceTestFixture.CreateTestLot("global-lot", TestItemId, null, 500, 200);
        
        fixture.SetupLotStorageWithLots(TestItemId, Market1, market1Lot);
        fixture.SetupLotStorageWithLots(TestItemId, Market2, market2Lot);
        fixture.SetupLotStorageWithLots(TestItemId, null, globalLot);

        // Act - query each market's inventory
        var market1Quantity = await warehouseService.GetAvailableQuantity(TestItemId, Market1);
        var market2Quantity = await warehouseService.GetAvailableQuantity(TestItemId, Market2);
        var globalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, null);

        // Assert - each market should show only its own inventory
        market1Result.Should().BeTrue("market 1 addition should succeed");
        market2Result.Should().BeTrue("market 2 addition should succeed");
        globalResult.Should().BeTrue("global market addition should succeed");

        market1Quantity.Should().Be(1000, "market 1 should show only its inventory");
        market2Quantity.Should().Be(2000, "market 2 should show only its inventory");
        globalQuantity.Should().Be(500, "global market should show only its inventory");

        // Verify separate events were emitted for each market - check individually since we made 3 operations
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 1000, 100, Market1, It.IsAny<System.DateTime?>()), Times.Once);
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 2000, 150, Market2, It.IsAny<System.DateTime?>()), Times.Once);
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 500, 200, null, It.IsAny<System.DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task TakeItems_PerMarket_ShouldNotAffectOtherMarkets()
    {
        // Arrange - setup same item in multiple markets
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup lots in different markets
        var market1Lot = WarehouseServiceTestFixture.CreateTestLot("m1-lot", TestItemId, Market1, 1500, 80);
        var market2Lot = WarehouseServiceTestFixture.CreateTestLot("m2-lot", TestItemId, Market2, 1200, 120);
        var globalLot = WarehouseServiceTestFixture.CreateTestLot("global-lot", TestItemId, null, 800, 200);
        
        fixture.SetupLotStorageWithLots(TestItemId, Market1, market1Lot);
        fixture.SetupLotStorageWithLots(TestItemId, Market2, market2Lot);
        fixture.SetupLotStorageWithLots(TestItemId, null, globalLot);

        // Act - consume items from Market1 only
        var consumptionResult = await warehouseService.TakeItems(TestItemId, 500, Market1);

        // Simulate the updated state after consumption (Market1 lot reduced by 500)
        var updatedMarket1Lot = WarehouseServiceTestFixture.CreateTestLot("m1-lot-updated", TestItemId, Market1, 1000, 80);
        fixture.SetupLotStorageWithLots(TestItemId, Market1, updatedMarket1Lot);

        // Act - verify quantities in all markets
        var market1Quantity = await warehouseService.GetAvailableQuantity(TestItemId, Market1);
        var market2Quantity = await warehouseService.GetAvailableQuantity(TestItemId, Market2);
        var globalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, null);

        // Assert - only Market1 should be affected
        consumptionResult.Success.Should().BeTrue("consumption from market 1 should succeed");
        consumptionResult.ConsumedQuantity.Should().Be(500, "should consume requested quantity");
        consumptionResult.WeightedAverageCost.Should().Be(80, "should return market 1 lot cost");

        market1Quantity.Should().Be(1000, "market 1 should show reduced quantity after consumption");
        market2Quantity.Should().Be(1200, "market 2 should be unaffected by market 1 consumption");
        globalQuantity.Should().Be(800, "global market should be unaffected by market 1 consumption");

        // Verify consumption event was only emitted for Market1
        fixture.VerifyItemsConsumed(TestItemId, 500, 80, Market1);
    }

    [Fact]
    public async Task GetAvailableQuantity_PerMarket_AccurateIsolation()
    {
        // Arrange - complex multi-market scenario
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup multiple lots per market to test aggregation within markets
        var market1Lot1 = WarehouseServiceTestFixture.CreateTestLot("m1-lot1", TestItemId, Market1, 300, 50);
        var market1Lot2 = WarehouseServiceTestFixture.CreateTestLot("m1-lot2", TestItemId, Market1, 700, 75);
        
        var market2Lot1 = WarehouseServiceTestFixture.CreateTestLot("m2-lot1", TestItemId, Market2, 400, 100);
        var market2Lot2 = WarehouseServiceTestFixture.CreateTestLot("m2-lot2", TestItemId, Market2, 600, 125);
        var market2Lot3 = WarehouseServiceTestFixture.CreateTestLot("m2-lot3", TestItemId, Market2, 500, 150);

        var globalLot = WarehouseServiceTestFixture.CreateTestLot("global-lot", TestItemId, null, 250, 200);
        
        // Setup different lot configurations per market
        fixture.SetupLotStorageWithLots(TestItemId, Market1, market1Lot1, market1Lot2);
        fixture.SetupLotStorageWithLots(TestItemId, Market2, market2Lot1, market2Lot2, market2Lot3);
        fixture.SetupLotStorageWithLots(TestItemId, null, globalLot);

        // Act - query quantities for each market
        var market1Total = await warehouseService.GetAvailableQuantity(TestItemId, Market1);
        var market2Total = await warehouseService.GetAvailableQuantity(TestItemId, Market2);
        var globalTotal = await warehouseService.GetAvailableQuantity(TestItemId, null);

        // Assert - verify correct aggregation within each market
        market1Total.Should().Be(1000, "market 1 should aggregate its 2 lots correctly (300 + 700)");
        market2Total.Should().Be(1500, "market 2 should aggregate its 3 lots correctly (400 + 600 + 500)");
        globalTotal.Should().Be(250, "global market should show its single lot quantity");
    }

    [Fact]
    public async Task CrossMarket_Operations_CompleteIsolationTest()
    {
        // Arrange - comprehensive cross-market scenario with multiple items
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        const ulong item1 = 1001;
        const ulong item2 = 1002;

        // Setup Item1 in Market1 and Market2
        var item1Market1Lot = WarehouseServiceTestFixture.CreateTestLot("i1m1", item1, Market1, 1000, 100);
        var item1Market2Lot = WarehouseServiceTestFixture.CreateTestLot("i1m2", item1, Market2, 800, 150);
        
        // Setup Item2 in Market2 and Global
        var item2Market2Lot = WarehouseServiceTestFixture.CreateTestLot("i2m2", item2, Market2, 600, 200);
        var item2GlobalLot = WarehouseServiceTestFixture.CreateTestLot("i2global", item2, null, 400, 250);

        fixture.SetupLotStorageWithLots(item1, Market1, item1Market1Lot);
        fixture.SetupLotStorageWithLots(item1, Market2, item1Market2Lot);
        fixture.SetupLotStorageWithLots(item2, Market2, item2Market2Lot);
        fixture.SetupLotStorageWithLots(item2, null, item2GlobalLot);

        // Act & Assert - perform operations and verify isolation

        // 1. Consume Item1 from Market1 - should not affect Item1 in Market2
        var item1Market1Consumption = await warehouseService.TakeItems(item1, 300, Market1);
        item1Market1Consumption.Success.Should().BeTrue();
        item1Market1Consumption.ConsumedQuantity.Should().Be(300);

        // Update the fixture to reflect consumption
        var item1Market1Updated = WarehouseServiceTestFixture.CreateTestLot("i1m1updated", item1, Market1, 700, 100);
        fixture.SetupLotStorageWithLots(item1, Market1, item1Market1Updated);

        // Verify other markets/items unaffected
        var item1Market2Quantity = await warehouseService.GetAvailableQuantity(item1, Market2);
        var item2Market2Quantity = await warehouseService.GetAvailableQuantity(item2, Market2);
        
        item1Market2Quantity.Should().Be(800, "Item1 in Market2 should be unaffected by Market1 consumption");
        item2Market2Quantity.Should().Be(600, "Item2 in Market2 should be unaffected by Item1 operations");

        // 2. Consume Item2 from Global - should not affect Market2
        var item2GlobalConsumption = await warehouseService.TakeItems(item2, 200, null);
        item2GlobalConsumption.Success.Should().BeTrue();
        item2GlobalConsumption.ConsumedQuantity.Should().Be(200);

        // Update fixture for global consumption
        var item2GlobalUpdated = WarehouseServiceTestFixture.CreateTestLot("i2globalupdated", item2, null, 200, 250);
        fixture.SetupLotStorageWithLots(item2, null, item2GlobalUpdated);

        // Verify Market2 Item2 still unchanged
        var item2Market2QuantityAfter = await warehouseService.GetAvailableQuantity(item2, Market2);
        item2Market2QuantityAfter.Should().Be(600, "Item2 in Market2 should remain unchanged after global consumption");

        // 3. Final state verification - all quantities correct
        var finalItem1Market1 = await warehouseService.GetAvailableQuantity(item1, Market1);
        var finalItem1Market2 = await warehouseService.GetAvailableQuantity(item1, Market2);
        var finalItem2Market2 = await warehouseService.GetAvailableQuantity(item2, Market2);
        var finalItem2Global = await warehouseService.GetAvailableQuantity(item2, null);

        finalItem1Market1.Should().Be(700, "Item1 Market1 final state correct");
        finalItem1Market2.Should().Be(800, "Item1 Market2 unchanged throughout test");
        finalItem2Market2.Should().Be(600, "Item2 Market2 unchanged throughout test");
        finalItem2Global.Should().Be(200, "Item2 Global final state correct");

        // Verify consumption events were properly isolated - check individually since we made 2 consumption operations
        fixture.MockEventService.Verify(x => x.EmitItemsConsumedAsync(item1, 300, 100, Market1, It.IsAny<System.DateTime?>()), Times.Once);
        fixture.MockEventService.Verify(x => x.EmitItemsConsumedAsync(item2, 200, 250, null, It.IsAny<System.DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task GetWeightedAverageCost_PerMarket_IndependentCalculations()
    {
        // Arrange - same item with different cost structures in different markets
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Market1: Mix of cheap and expensive lots
        var market1CheapLot = WarehouseServiceTestFixture.CreateTestLot("m1-cheap", TestItemId, Market1, 800, 50);
        var market1ExpensiveLot = WarehouseServiceTestFixture.CreateTestLot("m1-expensive", TestItemId, Market1, 200, 250);
        
        // Market2: All medium-cost lots
        var market2MediumLot1 = WarehouseServiceTestFixture.CreateTestLot("m2-med1", TestItemId, Market2, 500, 100);
        var market2MediumLot2 = WarehouseServiceTestFixture.CreateTestLot("m2-med2", TestItemId, Market2, 500, 120);
        
        fixture.SetupLotStorageWithLots(TestItemId, Market1, market1CheapLot, market1ExpensiveLot);
        fixture.SetupLotStorageWithLots(TestItemId, Market2, market2MediumLot1, market2MediumLot2);

        // Act - get weighted average costs for each market
        var market1AvgCost = await warehouseService.GetWeightedAverageCost(TestItemId, Market1);
        var market2AvgCost = await warehouseService.GetWeightedAverageCost(TestItemId, Market2);

        // Assert - verify independent cost calculations
        // Market1: (800*50 + 200*250) / (800+200) = (40000 + 50000) / 1000 = 90
        var expectedMarket1Cost = (800L * 50 + 200L * 250) / (800L + 200L); // Should be 90
        market1AvgCost.Should().Be(expectedMarket1Cost, "Market1 weighted average should be calculated independently");

        // Market2: (500*100 + 500*120) / (500+500) = (50000 + 60000) / 1000 = 110
        var expectedMarket2Cost = (500L * 100 + 500L * 120) / (500L + 500L); // Should be 110
        market2AvgCost.Should().Be(expectedMarket2Cost, "Market2 weighted average should be calculated independently");

        // Verify costs are different, proving independence
        market1AvgCost.Should().NotBe(market2AvgCost, "Different markets should have different weighted averages");
    }

    [Fact]
    public async Task AddItems_GlobalMarket_SeparateFromNumberedMarkets()
    {
        // Arrange - test global market (null) vs numbered markets isolation
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, Market1);
        fixture.SetupEmptyWarehouse(TestItemId, null); // Global market

        // Act - add items to global market and Market1
        var globalResult = await warehouseService.AddItems(TestItemId, 1500, 300, null); // Global
        var market1Result = await warehouseService.AddItems(TestItemId, 1000, 400, Market1); // Market1

        // Simulate post-addition state
        var globalLot = WarehouseServiceTestFixture.CreateTestLot("global", TestItemId, null, 1500, 300);
        var market1Lot = WarehouseServiceTestFixture.CreateTestLot("m1", TestItemId, Market1, 1000, 400);
        
        fixture.SetupLotStorageWithLots(TestItemId, null, globalLot);
        fixture.SetupLotStorageWithLots(TestItemId, Market1, market1Lot);

        // Act - query both markets
        var globalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, null);
        var market1Quantity = await warehouseService.GetAvailableQuantity(TestItemId, Market1);

        // Assert - global and numbered markets are completely separate
        globalResult.Should().BeTrue("global market addition should succeed");
        market1Result.Should().BeTrue("market 1 addition should succeed");

        globalQuantity.Should().Be(1500, "global market should show its own quantity");
        market1Quantity.Should().Be(1000, "market 1 should show its own quantity");

        // Verify separate operations were recorded - check individually since we made 2 operations
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 1500, 300, null, It.IsAny<System.DateTime?>()), Times.Once);
        fixture.MockEventService.Verify(x => x.EmitItemsAddedAsync(TestItemId, 1000, 400, Market1, It.IsAny<System.DateTime?>()), Times.Once);
    }
}