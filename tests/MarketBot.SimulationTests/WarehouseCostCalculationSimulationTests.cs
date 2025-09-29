using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Cost calculation simulation tests - validates complex cost calculation scenarios end-to-end
/// Tests mathematical accuracy of weighted average calculations, consumption previews, and overflow protection
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Medium")]
public class WarehouseCostCalculationSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task GetWeightedAverageCost_MixedResourceTypes_CorrectCalculation()
    {
        // Arrange - realistic scenario with different resource acquisition methods
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup mixed resource types common in game economy
        var minedLot = WarehouseServiceTestFixture.CreateTestLot(
            "mined-resources", TestItemId, TestMarketId, quantity: 2000, unitCost: 0); // Free mined resources
        var purchasedLot = WarehouseServiceTestFixture.CreateTestLot(
            "purchased-resources", TestItemId, TestMarketId, quantity: 1000, unitCost: 150); // Market purchase
        var craftedLot = WarehouseServiceTestFixture.CreateTestLot(
            "crafted-resources", TestItemId, TestMarketId, quantity: 500, unitCost: 300); // High-cost crafted

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, minedLot, purchasedLot, craftedLot);

        // Act - get weighted average cost
        var weightedAverageCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Assert - verify mathematical accuracy
        // Expected: (2000*0 + 1000*150 + 500*300) / (2000+1000+500) = (0 + 150000 + 150000) / 3500 = 85.71 ≈ 85
        var expectedCost = (2000L * 0 + 1000L * 150 + 500L * 300) / (2000L + 1000L + 500L); // Should be 85
        weightedAverageCost.Should().Be(expectedCost, 
            "should calculate correct weighted average across mixed resource types");
    }

    [Fact]
    public async Task GetConsumptionCost_PreviewVsActual_ShouldMatch()
    {
        // Arrange - setup scenario for consumption preview vs actual comparison
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup multiple lots with different costs for consumption testing
        var cheapLot = WarehouseServiceTestFixture.CreateTestLot(
            "cheap-lot", TestItemId, TestMarketId, quantity: 800, unitCost: 75);
        var mediumLot = WarehouseServiceTestFixture.CreateTestLot(
            "medium-lot", TestItemId, TestMarketId, quantity: 600, unitCost: 125);
        var expensiveLot = WarehouseServiceTestFixture.CreateTestLot(
            "expensive-lot", TestItemId, TestMarketId, quantity: 400, unitCost: 200);

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, cheapLot, mediumLot, expensiveLot);

        const long consumptionQuantity = 1200; // Will consume all cheap (800) + some medium (400)

        // Act - get consumption cost preview
        var previewResult = await warehouseService.GetConsumptionCost(TestItemId, consumptionQuantity, TestMarketId);

        // Act - perform actual consumption (without changing fixture state between calls)
        var actualResult = await warehouseService.TakeItems(TestItemId, consumptionQuantity, TestMarketId);

        // Assert - preview and actual should match
        previewResult.Available.Should().BeTrue("should have sufficient inventory for full consumption");
        actualResult.Success.Should().BeTrue("actual consumption should succeed");

        // Expected cost: (800*75 + 400*125) / 1200 = (60000 + 50000) / 1200 = 91.67 ≈ 91
        var expectedCost = (800L * 75 + 400L * 125) / 1200L; // Should be 91
        previewResult.WeightedAverageCost.Should().Be(expectedCost, 
            "preview should calculate correct weighted average cost");
        actualResult.WeightedAverageCost.Should().Be(expectedCost, 
            "actual consumption should match preview cost");
        
        previewResult.WeightedAverageCost.Should().Be(actualResult.WeightedAverageCost,
            "preview cost should exactly match actual consumption cost");

        actualResult.ConsumedQuantity.Should().Be(consumptionQuantity,
            "should consume the exact requested quantity");
    }

    [Fact]
    public async Task LargeQuantities_CostCalculation_NoOverflow()
    {
        // Arrange - test with large but realistic values to ensure no integer overflow
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        const long largeQuantity = 1_000_000_000; // 1 billion units
        const long largeCost = 1_000; // 1000 cost per unit
        const long expectedTotalValue = 1_000_000_000_000; // 1 trillion total value

        var largeLot = WarehouseServiceTestFixture.CreateTestLot(
            "large-lot", TestItemId, TestMarketId, quantity: largeQuantity, unitCost: largeCost);

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, largeLot);

        // Act - get weighted average cost for large values
        var weightedAverageCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);
        var availableQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - should handle large values without overflow
        weightedAverageCost.Should().Be(largeCost, 
            "weighted average should equal unit cost for single lot");
        availableQuantity.Should().Be(largeQuantity, 
            "should handle large quantities correctly");

        // Verify the lot's total value calculation doesn't overflow
        largeLot.TotalValue.Should().Be(expectedTotalValue, 
            "should calculate large total values without overflow");
        largeLot.TotalValue.Should().BePositive("total value should remain positive");
    }

    [Fact]
    public async Task ZeroCostLots_WeightedAverage_HandleGracefully()
    {
        // Arrange - mix of zero-cost and paid lots (common scenario)
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup scenario with significant zero-cost resources
        var largeFreeResources = WarehouseServiceTestFixture.CreateTestLot(
            "free-resources", TestItemId, TestMarketId, quantity: 5000, unitCost: 0); // Large free lot
        var smallPaidResources = WarehouseServiceTestFixture.CreateTestLot(
            "paid-resources", TestItemId, TestMarketId, quantity: 1000, unitCost: 300); // Smaller paid lot

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, largeFreeResources, smallPaidResources);

        // Act - calculate weighted average with zero costs
        var weightedAverageCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Assert - should handle zero costs gracefully
        // Expected: (5000*0 + 1000*300) / (5000+1000) = 300000 / 6000 = 50
        var expectedCost = (5000L * 0 + 1000L * 300) / (5000L + 1000L); // Should be 50
        weightedAverageCost.Should().Be(expectedCost, 
            "should correctly calculate weighted average when zero-cost lots dominate");

        weightedAverageCost.Should().BeGreaterThan(0, 
            "weighted average should be positive due to paid resources");
        weightedAverageCost.Should().BeLessThan(300, 
            "weighted average should be less than highest unit cost due to free resources");
    }

    [Fact]
    public async Task GetConsumptionCost_InsufficientInventory_PartialPreview()
    {
        // Arrange - scenario where requested quantity exceeds available inventory
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup limited inventory
        var availableLot = WarehouseServiceTestFixture.CreateTestLot(
            "available-lot", TestItemId, TestMarketId, quantity: 500, unitCost: 180);

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, availableLot);

        const long requestedQuantity = 1000; // More than available (500)

        // Act - preview consumption of more than available
        var previewResult = await warehouseService.GetConsumptionCost(TestItemId, requestedQuantity, TestMarketId);

        // Assert - should indicate insufficient inventory but provide cost for what's available
        previewResult.Available.Should().BeFalse(
            "should indicate insufficient inventory for full request");
        previewResult.WeightedAverageCost.Should().Be(180, 
            "should provide cost for available quantity");

        // Verify actual consumption matches preview behavior
        var actualResult = await warehouseService.TakeItems(TestItemId, requestedQuantity, TestMarketId);
        actualResult.Success.Should().BeTrue("should succeed with partial consumption");
        actualResult.ConsumedQuantity.Should().Be(500, "should consume only what's available");
        actualResult.WeightedAverageCost.Should().Be(180, "should match preview cost");
    }

    [Fact]
    public async Task GetWeightedAverageCost_EmptyWarehouse_ReturnsZero()
    {
        // Arrange - completely empty warehouse
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - get weighted average cost for empty warehouse
        var weightedAverageCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Assert - should return zero for empty warehouse (avoid division by zero)
        weightedAverageCost.Should().Be(0, 
            "empty warehouse should return zero weighted average cost");
    }

    [Fact]
    public async Task GetConsumptionCost_EmptyWarehouse_ReturnsUnavailable()
    {
        // Arrange - completely empty warehouse
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - preview consumption from empty warehouse
        var previewResult = await warehouseService.GetConsumptionCost(TestItemId, 100, TestMarketId);

        // Assert - should indicate unavailable
        previewResult.Available.Should().BeFalse(
            "should indicate items not available in empty warehouse");
        previewResult.WeightedAverageCost.Should().Be(0, 
            "should return zero cost when nothing available");
    }

    [Fact]
    public async Task ComplexCostCalculation_MultipleLotsAndQuantities_AccurateResults()
    {
        // Arrange - complex scenario with varying lot sizes and costs
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup complex lot structure
        var smallExpensiveLot = WarehouseServiceTestFixture.CreateTestLot(
            "small-expensive", TestItemId, TestMarketId, quantity: 100, unitCost: 500);
        var mediumCheapLot = WarehouseServiceTestFixture.CreateTestLot(
            "medium-cheap", TestItemId, TestMarketId, quantity: 2000, unitCost: 50);
        var largeMediumLot = WarehouseServiceTestFixture.CreateTestLot(
            "large-medium", TestItemId, TestMarketId, quantity: 3000, unitCost: 150);
        var tinyFreeLot = WarehouseServiceTestFixture.CreateTestLot(
            "tiny-free", TestItemId, TestMarketId, quantity: 10, unitCost: 0);

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, 
            smallExpensiveLot, mediumCheapLot, largeMediumLot, tinyFreeLot);

        // Act - calculate weighted average across complex lot structure
        var weightedAverageCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);
        var totalQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - verify complex calculation
        const long expectedTotalQuantity = 100 + 2000 + 3000 + 10; // 5110
        totalQuantity.Should().Be(expectedTotalQuantity, "should aggregate all lots correctly");

        // Expected weighted average: (100*500 + 2000*50 + 3000*150 + 10*0) / 5110
        // = (50000 + 100000 + 450000 + 0) / 5110 = 600000 / 5110 ≈ 117
        var expectedCost = (100L * 500 + 2000L * 50 + 3000L * 150 + 10L * 0) / expectedTotalQuantity;
        weightedAverageCost.Should().Be(expectedCost, 
            "should calculate correct weighted average for complex lot structure");
    }

    [Fact]
    public async Task GetConsumptionCost_ComplexFIFOPreview_CorrectCalculation()
    {
        // Arrange - test consumption preview with complex FIFO ordering
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Setup lots that will test FIFO ordering in preview
        var freeLot = WarehouseServiceTestFixture.CreateTestLot(
            "free-lot", TestItemId, TestMarketId, quantity: 300, unitCost: 0); // Consumed first
        var cheapLot = WarehouseServiceTestFixture.CreateTestLot(
            "cheap-lot", TestItemId, TestMarketId, quantity: 500, unitCost: 100); // Consumed second
        var expensiveLot = WarehouseServiceTestFixture.CreateTestLot(
            "expensive-lot", TestItemId, TestMarketId, quantity: 700, unitCost: 200); // Partially consumed

        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, freeLot, cheapLot, expensiveLot);

        const long consumptionQuantity = 1000; // Will consume: 300@0 + 500@100 + 200@200

        // Act - preview FIFO consumption
        var previewResult = await warehouseService.GetConsumptionCost(TestItemId, consumptionQuantity, TestMarketId);

        // Assert - verify FIFO preview calculation
        previewResult.Available.Should().BeTrue("should have sufficient inventory");

        // Expected cost: (300*0 + 500*100 + 200*200) / 1000 = (0 + 50000 + 40000) / 1000 = 90
        var expectedCost = (300L * 0 + 500L * 100 + 200L * 200) / 1000L; // Should be 90
        previewResult.WeightedAverageCost.Should().Be(expectedCost, 
            "should preview correct FIFO consumption cost");
    }
}