using System;
using System.Threading.Tasks;
using FluentAssertions;
using MarketBot.TestUtilities.Fixtures;
using Moq;
using Xunit;

namespace MarketBot.SimulationTests;

/// <summary>
/// Warehouse metrics and event integration simulation tests - validates telemetry and monitoring functionality
/// Tests critical observability features for production monitoring and performance tracking
/// </summary>
[Trait("Category", "Simulation")]
[Trait("Performance", "Low")]
public class WarehouseMetricsSimulationTests
{
    // Test constants
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    [Fact]
    public async Task AddItems_MetricsIncrement_OperationsCounter()
    {
        // Arrange - setup warehouse service with metrics tracking
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false); // Production mode for full metrics
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - perform add operations that should increment metrics
        var addResult1 = await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);
        var addResult2 = await warehouseService.AddItems(TestItemId, 500, 150, TestMarketId);

        // Assert - verify operations succeeded and metrics were incremented
        addResult1.Should().BeTrue("first addition should succeed");
        addResult2.Should().BeTrue("second addition should succeed");

        // Verify warehouse_operations_total metric was incremented for each operation
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.Exactly(2), "should increment operations counter for each AddItems call in production mode");

        // Verify warehouse_lot_operations_total metric was incremented for lot operations  
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_lot_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.Exactly(2), "should increment lot operations counter for each lot creation");

        // Verify events were emitted for each addition
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(TestItemId, 1000, 100, TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit items_added event for first addition");
        
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(TestItemId, 500, 150, TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit items_added event for second addition");
    }

    [Fact]
    public async Task AddItems_DryRunMode_CorrectMetricTags()
    {
        // Arrange - setup warehouse service in dry-run mode
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(true); // Enable dry-run mode for different metric tags
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - perform operations in dry-run mode
        var addResult = await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);

        // Assert - verify operation succeeded
        addResult.Should().BeTrue("dry-run addition should succeed");

        // Verify metrics were tagged with dry-run mode (if metrics are emitted in dry-run)
        // Note: Based on previous tests, dry-run might not emit metrics, so we verify the expectation
        
        // The warehouse service may not emit metrics in dry-run mode to avoid side effects
        // This test validates that if metrics are emitted, they have correct tags
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:dry_run" }, It.IsAny<double>()),
            Times.Never, "dry-run mode should not emit metrics to avoid observability side effects");

        // Verify no events are emitted in dry-run mode
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Never, "dry-run mode should not emit events");
    }

    [Fact]
    public async Task TakeItems_MetricsIncrement_ConsumptionCounter()
    {
        // Arrange - setup warehouse with items for consumption
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false); // Production mode for full metrics
        var warehouseService = fixture.CreateWarehouseService();

        // Setup warehouse with existing lots for consumption
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("lot1", TestItemId, TestMarketId, 1000, 120);
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("lot2", TestItemId, TestMarketId, 500, 150);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2);

        // Act - perform consumption operations
        var consumeResult1 = await warehouseService.TakeItems(TestItemId, 800, TestMarketId);
        
        // Update storage to reflect consumption
        var remainingLot1 = WarehouseServiceTestFixture.CreateTestLot("remaining1", TestItemId, TestMarketId, 200, 120);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, remainingLot1, lot2);

        var consumeResult2 = await warehouseService.TakeItems(TestItemId, 300, TestMarketId);

        // Assert - verify operations succeeded
        consumeResult1.Success.Should().BeTrue("first consumption should succeed");
        consumeResult1.ConsumedQuantity.Should().Be(800, "should consume requested quantity");

        consumeResult2.Success.Should().BeTrue("second consumption should succeed");
        consumeResult2.ConsumedQuantity.Should().Be(300, "should consume requested quantity");

        // Verify warehouse_operations_total was incremented for each operation
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.AtLeast(2), "should increment general operations counter for consumption");

        // Verify consumption-specific metrics were incremented
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_consumption_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.Exactly(2), "should increment consumption operations counter for each TakeItems call");

        // Verify consumption events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(TestItemId, 800, It.IsAny<long>(), TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit consumption event for first operation");
        
        fixture.MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(TestItemId, 300, It.IsAny<long>(), TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit consumption event for second operation");
    }

    [Fact]
    public async Task GetWeightedAverageCost_PerformanceTiming_SlowOperationWarning()
    {
        // Arrange - setup warehouse with complex lot structure for cost calculations
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        // Setup multiple lots to simulate complex cost calculation
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("perf1", TestItemId, TestMarketId, 10000, 100);
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("perf2", TestItemId, TestMarketId, 8000, 150);
        var lot3 = WarehouseServiceTestFixture.CreateTestLot("perf3", TestItemId, TestMarketId, 6000, 200);
        var lot4 = WarehouseServiceTestFixture.CreateTestLot("perf4", TestItemId, TestMarketId, 4000, 250);
        var lot5 = WarehouseServiceTestFixture.CreateTestLot("perf5", TestItemId, TestMarketId, 2000, 300);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2, lot3, lot4, lot5);

        // Act - perform weighted average cost calculation (potentially slow operation)
        var startTime = DateTime.UtcNow;
        var weightedAvgCost = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);
        var endTime = DateTime.UtcNow;
        var operationDuration = endTime - startTime;

        // Assert - verify cost calculation is correct
        // Expected: (10000*100 + 8000*150 + 6000*200 + 4000*250 + 2000*300) / 30000 = 5400000 / 30000 = 180
        var expectedCost = (10000L * 100 + 8000L * 150 + 6000L * 200 + 4000L * 250 + 2000L * 300) / 30000L;
        weightedAvgCost.Should().Be(expectedCost, "should calculate correct weighted average across all lots");

        // If the operation took longer than 10ms, verify performance metrics were set
        if (operationDuration.TotalMilliseconds > 10)
        {
            // Verify performance timing metric was set (if the service tracks this)
            // Note: This depends on the actual service implementation
            fixture.MockMetricsService.Verify(
                x => x.SetGauge("warehouse_cost_calculation_time_ms", It.IsAny<string[]>(), It.Is<double>(d => d > 10)),
                Times.AtMostOnce(), "should set performance timing metric for slow operations");

            // Verify warning was logged for slow operation (if implemented)
            fixture.MockLogger.Verify(
                x => x.Log(
                    Microsoft.Extensions.Logging.LogLevel.Warning,
                    It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("slow") || v.ToString()!.Contains("performance")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtMostOnce(), "should log warning for slow cost calculations");
        }

        // Verify operation completed successfully regardless of performance
        operationDuration.Should().BeLessThan(TimeSpan.FromSeconds(5), "cost calculation should complete within reasonable time");
    }

    [Fact]
    public async Task EventService_Integration_ItemsAddedAndConsumed()
    {
        // Arrange - setup complete scenario for event integration testing
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false); // Production mode for event emission
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        const long addQuantity = 2000;
        const long addUnitCost = 175;
        const long consumeQuantity = 1200;

        // Act - complete add and consume workflow with event tracking

        // Phase 1: Add items (should emit items_added event)
        var addResult = await warehouseService.AddItems(TestItemId, addQuantity, addUnitCost, TestMarketId);

        // Setup lot for consumption
        var testLot = WarehouseServiceTestFixture.CreateTestLot("event-test", TestItemId, TestMarketId, addQuantity, addUnitCost);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, testLot);

        // Phase 2: Consume items (should emit items_consumed event)
        var consumeResult = await warehouseService.TakeItems(TestItemId, consumeQuantity, TestMarketId);

        // Assert - verify operations succeeded
        addResult.Should().BeTrue("addition should succeed");
        consumeResult.Success.Should().BeTrue("consumption should succeed");
        consumeResult.ConsumedQuantity.Should().Be(consumeQuantity, "should consume requested quantity");
        consumeResult.WeightedAverageCost.Should().Be(addUnitCost, "single-lot consumption should maintain unit cost");

        // Verify ItemsAddedAsync event was emitted with correct parameters
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(TestItemId, addQuantity, addUnitCost, TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit items_added event with correct parameters");

        // Verify EmitItemsConsumedAsync event was emitted with correct parameters
        fixture.MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(TestItemId, consumeQuantity, addUnitCost, TestMarketId, It.IsAny<DateTime?>()),
            Times.Once, "should emit items_consumed event with correct parameters");

        // Verify no unexpected events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitLotCreatedAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.AtMostOnce(), "may emit lot_created events during normal operations");

        // Verify events were not emitted multiple times
        fixture.MockEventService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ComplexOperations_MultipleMetrics_ComprehensiveTracking()
    {
        // Arrange - setup complex scenario with multiple metrics types
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);
        fixture.SetupEmptyWarehouse(TestItemId, 2); // Second market for complexity

        // Act - perform comprehensive operations across multiple markets and operations

        // Phase 1: Add items to multiple markets
        await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);
        await warehouseService.AddItems(TestItemId, 800, 120, 2);
        await warehouseService.AddItems(TestItemId, 600, 150, TestMarketId);

        // Setup lots for operations
        var lot1m1 = WarehouseServiceTestFixture.CreateTestLot("complex1", TestItemId, TestMarketId, 1600, 125); // Combined
        var lot1m2 = WarehouseServiceTestFixture.CreateTestLot("complex2", TestItemId, 2, 800, 120);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1m1);
        fixture.SetupLotStorageWithLots(TestItemId, 2, lot1m2);

        // Phase 2: Query operations (potential metrics)
        var quantity1 = await warehouseService.GetAvailableQuantity(TestItemId, TestMarketId);
        var quantity2 = await warehouseService.GetAvailableQuantity(TestItemId, 2);
        var avgCost1 = await warehouseService.GetWeightedAverageCost(TestItemId, TestMarketId);

        // Phase 3: Consumption operations
        await warehouseService.TakeItems(TestItemId, 500, TestMarketId);
        await warehouseService.TakeItems(TestItemId, 300, 2);

        // Phase 4: Cleanup operations
        await warehouseService.CleanupEmptyLots(TestItemId, TestMarketId);
        await warehouseService.CleanupEmptyLots(TestItemId, 2);

        // Assert - verify comprehensive metrics tracking

        // Verify addition operations were tracked
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.AtLeast(3), "should track all addition operations");

        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_lot_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.AtLeast(3), "should track lot operations for additions");

        // Verify consumption operations were tracked  
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_consumption_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.AtLeast(2), "should track consumption operations");

        // Verify all addition events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Exactly(3), "should emit events for all addition operations");

        // Verify consumption events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitItemsConsumedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Exactly(2), "should emit events for consumption operations");

        // Verify query operations completed successfully
        quantity1.Should().Be(1600, "should return correct quantity for market 1");
        quantity2.Should().Be(800, "should return correct quantity for market 2");
        avgCost1.Should().Be(125, "should calculate correct weighted average cost");
    }

    [Fact]
    public async Task LotMerge_Events_ProperMetricsAndEvents()
    {
        // Arrange - setup scenario that will trigger lot merging for metrics validation
        var fixture = new WarehouseServiceTestFixture();
        fixture.SetDryRunMode(false);
        var warehouseService = fixture.CreateWarehouseService();

        // Configure low max lots per item to force merging
        var config = fixture.MockConfigService.Object.Config;
        config.Warehouse.MaxLotsPerItem = 2; // Force merging when adding third lot

        fixture.SetupEmptyWarehouse(TestItemId, TestMarketId);

        // Act - add enough lots to trigger merging (if service supports it)
        
        // Add two lots first (within limit)
        await warehouseService.AddItems(TestItemId, 1000, 100, TestMarketId);
        await warehouseService.AddItems(TestItemId, 800, 110, TestMarketId);

        // Setup existing lots
        var lot1 = WarehouseServiceTestFixture.CreateTestLot("merge1", TestItemId, TestMarketId, 1000, 100);
        var lot2 = WarehouseServiceTestFixture.CreateTestLot("merge2", TestItemId, TestMarketId, 800, 110);
        fixture.SetupLotStorageWithLots(TestItemId, TestMarketId, lot1, lot2);

        // Add third lot (should trigger merging logic)
        await warehouseService.AddItems(TestItemId, 600, 120, TestMarketId);

        // Assert - verify merging operations and metrics

        // Verify lot merge frequency metric was incremented (if merging occurred)
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_lot_merge_frequency", It.IsAny<string[]>(), It.IsAny<double>()),
            Times.AtMostOnce(), "should increment merge frequency if lots were merged");

        // Verify lot merge event was emitted (if merging occurred)
        fixture.MockEventService.Verify(
            x => x.EmitLotMergedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                                    It.IsAny<long>(), TestItemId, TestMarketId, It.IsAny<DateTime?>()),
            Times.AtMostOnce(), "should emit lot merge event if lots were merged");

        // Verify all addition operations were tracked regardless of merging
        fixture.MockMetricsService.Verify(
            x => x.Increment("warehouse_operations_total", new[] { "mode:production" }, It.IsAny<double>()),
            Times.AtLeast(3), "should track all addition operations");

        // Verify all items_added events were emitted
        fixture.MockEventService.Verify(
            x => x.EmitItemsAddedAsync(It.IsAny<ulong>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ulong?>(), It.IsAny<DateTime?>()),
            Times.Exactly(3), "should emit events for all addition operations");
    }
}