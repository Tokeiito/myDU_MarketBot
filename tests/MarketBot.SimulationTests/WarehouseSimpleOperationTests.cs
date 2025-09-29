using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Simple warehouse operation simulation tests - validates basic end-to-end workflows
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Medium")]
public class WarehouseSimpleOperationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;
    private const long TestQuantity = 1000;
    private const long TestUnitCost = 100;

    [Fact]
    public async Task AddItems_ThenGetAvailableQuantity_ShouldReturnCorrectQuantity()
    {
        // Arrange - create a WarehouseService with all dependencies mocked
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();

        // Start with empty warehouse
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - add items to the warehouse
        var addResult = await warehouseService.AddItems(TestItemId, TestQuantity, TestUnitCost, TestMarketId);

        // Simulate that after adding, the lot is stored and available
        var testLot = WarehouseServiceTestFixture.CreateTestLot(
            "sim-test-lot", TestItemId, TestMarketId, TestQuantity, TestUnitCost);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, testLot);

        // Get the available quantity
        var availableQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - verify the simulation worked end-to-end
        addResult.Should().BeTrue("items should be added successfully");
        availableQuantity.Should().Be(TestQuantity, "available quantity should match what was added");

        // Verify that the expected service interactions occurred
        fixture.VerifyItemsAdded(TestItemId, TestQuantity, TestUnitCost, TestMarketId);
    }

    [Fact]
    public async Task GetAvailableQuantity_WhenNoItemsInWarehouse_ShouldReturnZero()
    {
        // Arrange - completely empty warehouse
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - check quantity in empty warehouse
        var availableQuantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert
        availableQuantity.Should().Be(0, "empty warehouse should return zero quantity");
    }

    [Fact]
    public async Task AddItems_WithZeroCost_ShouldSucceed()
    {
        // Arrange - test mined resources scenario (zero cost)
        var fixture = new WarehouseServiceTestFixture();
        var warehouseService = fixture.CreateWarehouseService();
        
        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        const long zeroCost = 0L;

        // Act - add zero-cost items (mined resources)
        var addResult = await warehouseService.AddItems(TestItemId, TestQuantity, zeroCost, TestMarketId);

        // Assert
        addResult.Should().BeTrue("zero-cost items should be added successfully");
        fixture.VerifyItemsAdded(TestItemId, TestQuantity, zeroCost, TestMarketId);
    }
}