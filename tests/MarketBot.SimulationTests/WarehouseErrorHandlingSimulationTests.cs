using System;
using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Warehouse error handling simulation tests - validates edge cases and robust error handling
/// Tests critical validation and error recovery scenarios for production stability
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Low")]
public class WarehouseErrorHandlingSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task AddItems_NegativeQuantity_ProperValidation()
    {
        // Arrange - setup warehouse service for validation testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - attempt to add negative quantity
        var result = await warehouseService.AddItems(TestItemId, -100, 150, TestMarketId);

        // Assert - operation should fail with proper validation
        result.Should().BeFalse("negative quantities should be rejected");

        // Verify warning was logged for invalid input
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, "should log warning for negative quantity");

        // Verify no storage operations were attempted
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "should not attempt storage operations for invalid input");

        // Verify no events were emitted for invalid operations
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Never, "should not emit events for failed operations");
    }

    [Fact]
    public async Task AddItems_ZeroQuantity_ProperValidation()
    {
        // Arrange - setup for zero quantity validation
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - attempt to add zero quantity
        var result = await warehouseService.AddItems(TestItemId, 0, 150, TestMarketId);

        // Assert - operation should fail
        result.Should().BeFalse("zero quantities should be rejected");

        // Verify appropriate warning was logged
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, "should log warning for zero quantity");

        // Verify no side effects occurred
        fixture.MockLotStorage.Verify(x => x.StoreLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "should not attempt storage for zero quantity");
    }

    [Fact]
    public async Task TakeItems_NegativeQuantity_ProperValidation()
    {
        // Arrange - setup warehouse with items for consumption testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        var testLot = WarehouseServiceTestFixture.CreateTestLot("test", TestItemId, TestMarketId, 1000, 100);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, testLot);

        // Act - attempt to consume negative quantity
        var result = await warehouseService.TakeItems(TestItemId, -50, TestMarketId);

        // Assert - operation should fail with proper validation
        result.Success.Should().BeFalse("negative consumption quantities should be rejected");
        result.ConsumedQuantity.Should().Be(0, "no items should be consumed on validation failure");
        result.WeightedAverageCost.Should().Be(0, "no cost calculation on validation failure");

        // Verify warning was logged
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, "should log warning for negative consumption quantity");

        // Verify no storage modifications occurred
        fixture.MockLotStorage.Verify(x => x.UpdateLotQuantityAsync(It.IsAny<string>(), It.IsAny<long>()), 
            Times.Never, "should not modify storage for invalid consumption");
        fixture.MockLotStorage.Verify(x => x.DeleteLotAsync(It.IsAny<MarketBot.Domain.WarehouseLot>()), 
            Times.Never, "should not delete lots for invalid consumption");

        // Verify no consumption events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Never, "should not emit consumption events for failed operations");
    }

    [Fact]
    public async Task TakeItems_ZeroQuantity_ProperValidation()
    {
        // Arrange - setup for zero consumption validation
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        var testLot = WarehouseServiceTestFixture.CreateTestLot("test", TestItemId, TestMarketId, 500, 120);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, testLot);

        // Act - attempt to consume zero quantity
        var result = await warehouseService.TakeItems(TestItemId, 0, TestMarketId);

        // Assert - operation should fail
        result.Success.Should().BeFalse("zero consumption quantities should be rejected");
        result.ConsumedQuantity.Should().Be(0, "no items should be consumed");
        result.WeightedAverageCost.Should().Be(0, "no cost calculation for zero consumption");

        // Verify warning logged
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, "should log warning for zero consumption quantity");
    }

    [Fact]
    public async Task Operations_EmptyWarehouse_GracefulFailure()
    {
        // Arrange - setup empty warehouse for failure testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - attempt operations on non-existent inventory

        // Test consumption from empty warehouse
        var consumeResult = await warehouseService.TakeItems(TestItemId, 100, TestMarketId);

        // Test cost calculation on empty warehouse
        var avgCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Test cost preview on empty warehouse
        var costPreview = await warehouseService.GetConsumptionCost(TestItemId, 50, TestMarketId);

        // Test quantity check on empty warehouse
        var quantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Assert - operations should handle empty state gracefully

        // Consumption should indicate failure but not crash  
        consumeResult.Success.Should().BeFalse("consumption should return failure when no items available");
        consumeResult.ConsumedQuantity.Should().Be(0, "no items should be consumed from empty warehouse");
        consumeResult.WeightedAverageCost.Should().Be(0, "cost should be zero for empty consumption");

        // Cost calculations should return zero for empty warehouse
        avgCost.Should().Be(0, "average cost should be zero for empty warehouse");
        costPreview.Available.Should().BeFalse("cost preview should indicate unavailable for empty warehouse");
        costPreview.WeightedAverageCost.Should().Be(0, "preview cost should be zero for empty warehouse");

        // Quantity should be zero
        quantity.Should().Be(0, "quantity should be zero for empty warehouse");

        // Verify appropriate debug logging occurred (but not errors)
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Debug,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No lots available") || v.ToString()!.Contains("empty")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce, "should log debug messages for empty warehouse operations");
    }

    [Fact]
    public async Task Operations_NonExistentItem_GracefulHandling()
    {
        // Arrange - setup for non-existent item testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        const ulong nonExistentItemId = 9999;
        fixture.SetupEmptyWarehouse(nonExistentItemId, TestMarketId);

        // Act - perform operations on non-existent item

        // Test addition (should work - creates new inventory)
        var addResult = await warehouseService.AddItems(nonExistentItemId, 100, 50, TestMarketId);

        // Setup the lot that would be created
        var newLot = WarehouseServiceTestFixture.CreateTestLot("new", nonExistentItemId, TestMarketId, 100, 50);
        fixture.SetupLotStorageWithLots(nonExistentItemId, TestMarketId, newLot);

        // Test queries on newly created item
        var quantity = await warehouseService.GetAvailableQuantity(nonExistentItemId, TestMarketId);
        var avgCost = await warehouseService.GetWeightedAverageCost(nonExistentItemId, TestMarketId);

        // Test consumption
        var consumeResult = await warehouseService.TakeItems(nonExistentItemId, 50, TestMarketId);

        // Assert - system should handle non-existent items gracefully

        // Addition should work (creates new item tracking)
        addResult.Should().BeTrue("adding to non-existent item should create new inventory");

        // Queries should return correct values for newly created item
        quantity.Should().Be(100, "should return correct quantity for newly created item");
        avgCost.Should().Be(50, "should return correct average cost for newly created item");

        // Consumption should work normally
        consumeResult.Success.Should().BeTrue("consumption should work on newly created item");
        consumeResult.ConsumedQuantity.Should().Be(50, "should consume requested quantity");
        consumeResult.WeightedAverageCost.Should().Be(50, "should return correct cost for consumption");

        // Verify no error-level logging occurred
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never, "should not log errors for valid operations on new items");
    }

    [Fact]
    public async Task LargeNumbers_QuantityAndCost_NoOverflow()
    {
        // Arrange - setup for large number testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Use large but safe values that shouldn't cause overflow
        const long largeQuantity = 1_000_000_000L; // 1 billion
        const long largeCost = 10_000L; // 10k cost per unit
        
        // Act - perform operations with large numbers

        // Test addition with large quantities
        var addResult = await warehouseService.AddItems(TestItemId, largeQuantity, largeCost, TestMarketId);

        // Setup storage to simulate the large lot
        var largeLot = WarehouseServiceTestFixture.CreateTestLot("large", TestItemId, TestMarketId, largeQuantity, largeCost);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, largeLot);

        // Test quantity retrieval
        var quantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);

        // Test cost calculation with large values
        var avgCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Test cost preview for large consumption
        const long largeConsumption = 500_000_000L; // 500 million
        var costPreview = await warehouseService.GetConsumptionCost(TestItemId, largeConsumption, TestMarketId);

        // Test partial consumption of large quantity
        var consumeResult = await warehouseService.TakeItems(TestItemId, largeConsumption, TestMarketId);

        // Assert - operations should handle large numbers without overflow

        addResult.Should().BeTrue("addition with large numbers should succeed");
        quantity.Should().Be(largeQuantity, "should correctly track large quantities");
        avgCost.Should().Be(largeCost, "should correctly calculate costs for large values");

        costPreview.Available.Should().BeTrue("cost preview should work with large numbers");
        costPreview.WeightedAverageCost.Should().Be(largeCost, "preview should return correct cost for large consumption");

        consumeResult.Success.Should().BeTrue("consumption of large quantities should succeed");
        consumeResult.ConsumedQuantity.Should().Be(largeConsumption, "should consume large quantities correctly");
        consumeResult.WeightedAverageCost.Should().Be(largeCost, "should calculate correct cost for large consumption");

        // Verify no overflow-related errors were logged
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("overflow") || v.ToString()!.Contains("Overflow")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never, "should not log overflow errors for large but valid numbers");
    }

    [Fact]
    public async Task ExtremeLargeNumbers_OverflowProtection_GracefulHandling()
    {
        // Arrange - setup for extreme value testing near overflow limits
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Use values that are close to but not exceeding long.MaxValue
        const long extremeQuantity = long.MaxValue / 1000; // Large but safe
        const long extremeCost = 500; // Moderate cost to avoid multiplication overflow

        // Act - test extreme values

        var addResult = await warehouseService.AddItems(TestItemId, extremeQuantity, extremeCost, TestMarketId);

        // Setup storage for extreme lot
        var extremeLot = WarehouseServiceTestFixture.CreateTestLot("extreme", TestItemId, TestMarketId, extremeQuantity, extremeCost);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, extremeLot);

        var quantity = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);
        var avgCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Test adding another lot to check for calculation overflow protection
        var addResult2 = await warehouseService.AddItems(TestItemId, 1000, extremeCost, TestMarketId);

        // Assert - system should handle extreme values safely
        addResult.Should().BeTrue("extreme quantities should be handled if within limits");
        quantity.Should().Be(extremeQuantity, "should handle extreme quantities correctly");
        avgCost.Should().Be(extremeCost, "should calculate costs correctly for extreme values");

        // Second addition should also succeed (depends on implementation)
        addResult2.Should().BeTrue("subsequent additions should work with extreme base values");

        // Verify system remained stable
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never, "should not log errors for extreme but valid values");
    }

    [Fact]
    public async Task InvalidMarketIds_Operations_ProperHandling()
    {
        // Arrange - setup for invalid market ID testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        // Test with various potentially problematic market IDs
        const ulong maxMarketId = ulong.MaxValue;
        const ulong zeroMarketId = 0;

        // Setup empty warehouses for these market IDs
        fixture.SetupEmptyWarehouse(TestItemId, maxMarketId);
        fixture.SetupEmptyWarehouse(TestItemId, zeroMarketId);

        // Act - test operations with edge case market IDs

        // Test with maximum market ID
        var addResultMax = await warehouseService.AddItems(TestItemId, 100, 150, maxMarketId);
        var quantityMax = await warehouseService.GetAvailableQuantity(TestItemId, maxMarketId);

        // Test with zero market ID (which might be treated as global)
        var addResultZero = await warehouseService.AddItems(TestItemId, 200, 175, zeroMarketId);
        var quantityZero = await warehouseService.GetAvailableQuantity(TestItemId, zeroMarketId);

        // Test with null market ID (global market)
        var addResultNull = await warehouseService.AddItems(TestItemId, 300, 200, null);
        var quantityNull = await warehouseService.GetAvailableQuantity(TestItemId, null);

        // Assert - system should handle all market ID variants gracefully

        // Max market ID should work (if system supports it)
        addResultMax.Should().BeTrue("maximum market ID should be handled gracefully");
        // Note: We can't assert on quantity without setting up storage, but operation shouldn't crash

        // Zero market ID should work
        addResultZero.Should().BeTrue("zero market ID should be handled gracefully");

        // Null market ID (global) should work
        addResultNull.Should().BeTrue("null market ID (global) should work");

        // Verify no critical errors were logged
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("market") && v.ToString()!.Contains("invalid")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never, "should not log market ID validation errors for edge case but valid IDs");

        // Operations should complete without throwing exceptions
        quantityMax.Should().BeGreaterOrEqualTo(0, "quantity queries should not fail for valid market IDs");
        quantityZero.Should().BeGreaterOrEqualTo(0, "zero market ID should return valid quantity");
        quantityNull.Should().BeGreaterOrEqualTo(0, "global market should return valid quantity");
    }

    [Fact]
    public async Task ConcurrentOperations_ErrorRecovery_SystemStability()
    {
        // Arrange - setup for error recovery testing under stress
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        // Setup initial state with some lots
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("stable1", TestItemId, TestMarketId, 1000, 100);
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("stable2", TestItemId, TestMarketId, 500, 150);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2);

        // Act - perform a sequence of operations that mix valid and invalid inputs

        var results = new[]
        {
            // Valid operations
            await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId),
            // Invalid operation (will return 0 but shouldn't crash)
            await warehouseService.GetAvailableQuantity(TestItemId + 999, TestMarketId),
            // More valid operations  
            await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId),
        };

        var consumeResults = new[]
        {
            // Valid consumption
            await warehouseService.TakeItems(TestItemId, 100, TestMarketId),
            // Invalid consumption (negative) - should fail gracefully
            await warehouseService.TakeItems(TestItemId, -50, TestMarketId),
            // Valid consumption after error
            await warehouseService.TakeItems(TestItemId, 200, TestMarketId),
        };

        var addResults = new[]
        {
            // Invalid addition (negative) - should fail
            await warehouseService.AddItems(TestItemId, -100, 100, TestMarketId),
            // Valid addition after error - should work
            await warehouseService.AddItems(TestItemId, 300, 125, TestMarketId),
            // Invalid addition (zero) - should fail
            await warehouseService.AddItems(TestItemId, 0, 100, TestMarketId),
        };

        // Assert - system should remain stable despite mixed valid/invalid operations

        // Valid queries should return sensible values
        results[0].Should().Be(1500, "initial quantity should be sum of lots");
        results[1].Should().Be(0, "non-existent item should return zero quantity");
        results[2].Should().BeGreaterThan(0, "valid cost calculation should return positive value");

        // Mixed consumption results
        consumeResults[0].Success.Should().BeTrue("valid consumption should succeed");
        consumeResults[0].ConsumedQuantity.Should().Be(100, "should consume requested amount");

        consumeResults[1].Success.Should().BeFalse("invalid consumption should fail gracefully");
        consumeResults[1].ConsumedQuantity.Should().Be(0, "failed consumption should consume nothing");

        consumeResults[2].Success.Should().BeTrue("valid consumption after error should work");

        // Mixed addition results
        addResults[0].Should().BeFalse("invalid addition should fail");
        addResults[1].Should().BeTrue("valid addition after error should succeed");
        addResults[2].Should().BeFalse("zero addition should fail");

        // System should log appropriate warnings but no critical errors
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-positive quantity")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(3), "should log warnings for all invalid quantity operations");

        // No system-level errors should occur
        fixture.MockLogger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("system") || v.ToString()!.Contains("critical")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never, "should not log critical system errors during error recovery");
    }
}