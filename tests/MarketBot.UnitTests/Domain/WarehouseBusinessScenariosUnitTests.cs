using System;
using System.Collections.Generic;
using FluentAssertions;
using MarketBot.Domain;
using MarketBot.TestUtilities.Builders;
using Xunit;

namespace MarketBot.UnitTests.Domain;

/// <summary>
/// Unit tests for realistic warehouse business scenarios - MEDIUM priority edge cases
/// </summary>
[Trait("Category", "Unit")]
[Trait("Performance", "Fast")]
public class WarehouseBusinessScenariosUnitTests
{
    // Test constants for consistency
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;

    #region MEDIUM Priority Tests - Empty Inventory & Business Scenarios

    [Fact]
    public void WarehouseLotSummary_WhenNoLots_ShouldReturnEmptySummaryWithZeroValues()
    {
        // Arrange - completely empty warehouse
        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot>() // Empty collection
        };

        // Act & Assert - all properties should handle empty state gracefully
        summary.TotalQuantity.Should().Be(0);
        summary.TotalValue.Should().Be(0);
        summary.WeightedAverageCost.Should().Be(0);
        summary.LotCount.Should().Be(0);
        summary.LowestCost.Should().Be(0);
        summary.HighestCost.Should().Be(0);
        
        var sortedLots = summary.GetLotsSortedByCost();
        sortedLots.Should().BeEmpty();
    }

    [Fact]
    public void AddThenTakeItems_ShouldMaintainCorrectInventoryBalance()
    {
        // Arrange - simulate typical add/take workflow
        var lot1 = WarehouseLotBuilder.Create()
            .WithLotId("add-lot-1")
            .WithQuantity(1000)
            .WithUnitCost(100)
            .Build();

        var lot2 = WarehouseLotBuilder.Create()
            .WithLotId("add-lot-2")
            .WithQuantity(500)
            .WithUnitCost(200)
            .Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot> { lot1, lot2 }
        };

        // Act - simulate taking partial quantity (FIFO consumption from cheapest first)
        // Take 800 from lot1 (1000@100), leaving 200@100
        lot1.Quantity = 200; // Remaining after consumption

        // Assert - verify correct balance after consumption
        summary.TotalQuantity.Should().Be(700); // 200 + 500
        summary.TotalValue.Should().Be(120000); // (200 * 100) + (500 * 200)
        summary.WeightedAverageCost.Should().Be(171); // 120000 / 700 = 171 (integer division)
    }

    [Fact]
    public void AddMultipleItemsWithDifferentCosts_ThenTakePartial_ShouldCalculateCorrectWeightedAverageCost()
    {
        // Arrange - realistic multi-lot scenario with different acquisition costs
        var minedLot = WarehouseLotBuilder.Create()
            .WithLotId("mined-resources")
            .WithQuantity(2000)
            .WithZeroCost() // Free mined resources
            .Build();

        var purchasedLot = WarehouseLotBuilder.Create()
            .WithLotId("purchased-resources")
            .WithQuantity(1000)
            .WithUnitCost(150) // Bought from market
            .Build();

        var craftedLot = WarehouseLotBuilder.Create()
            .WithLotId("crafted-resources")
            .WithQuantity(500)
            .WithUnitCost(300) // High-cost crafted items
            .Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot> { minedLot, purchasedLot, craftedLot }
        };

        // Act - verify weighted average cost calculation for consumption scenario
        // Consumption order would be: 2000 @ 0 cost, then 500 @ 150 cost
        const long totalConsumed = 2500;
        const long consumptionTotalCost = (2000 * 0) + (500 * 150); // 0 + 75,000 = 75,000

        // Assert - weighted average cost of consumed items
        const long expectedWeightedAverage = consumptionTotalCost / totalConsumed; // 30
        expectedWeightedAverage.Should().Be(30);

        // Verify original summary totals before consumption
        summary.TotalQuantity.Should().Be(3500);
        summary.TotalValue.Should().Be(300000); // (2000*0) + (1000*150) + (500*300)
        summary.WeightedAverageCost.Should().Be(85); // 300000 / 3500 = 85
    }

    [Fact] 
    public void WarehouseLotSummary_GetLotsSortedByCost_WithZeroCostLots_ShouldOrderZeroCostFirst()
    {
        // Arrange - mix of zero cost and paid resources (common scenario)
        var laterTime = DateTime.UtcNow.AddMinutes(-10);
        var earlierTime = DateTime.UtcNow.AddMinutes(-20);

        var paidLot = WarehouseLotBuilder.Create()
            .WithLotId("paid-lot")
            .WithUnitCost(100)
            .WithCreatedAt(earlierTime)
            .Build();

        var minedLotOld = WarehouseLotBuilder.Create()
            .WithLotId("mined-old")
            .WithZeroCost()
            .WithCreatedAt(earlierTime) // Older but same cost
            .Build();

        var minedLotNew = WarehouseLotBuilder.Create()
            .WithLotId("mined-new") 
            .WithZeroCost()
            .WithCreatedAt(laterTime) // Newer but same cost
            .Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot> { paidLot, minedLotNew, minedLotOld } // Mixed order
        };

        // Act
        var sortedLots = summary.GetLotsSortedByCost();

        // Assert - should be ordered by cost first, then by creation time
        sortedLots.Should().HaveCount(3);
        sortedLots[0].Should().BeSameAs(minedLotOld); // Cost 0, earlier time
        sortedLots[1].Should().BeSameAs(minedLotNew); // Cost 0, later time
        sortedLots[2].Should().BeSameAs(paidLot);     // Cost 100
    }

    [Fact]
    public void WarehouseLot_TotalValue_WhenLargeQuantityAndCost_ShouldNotOverflow()
    {
        // Arrange - test with large but realistic values
        const long largeQuantity = 1_000_000_000; // 1 billion units
        const long largeCost = 1_000; // 1000 cost per unit
        const long expectedTotalValue = 1_000_000_000_000; // 1 trillion total value

        var lot = WarehouseLotBuilder.Create()
            .WithQuantity(largeQuantity)
            .WithUnitCost(largeCost)
            .Build();

        // Act
        var totalValue = lot.TotalValue;

        // Assert - should handle large values without overflow
        totalValue.Should().Be(expectedTotalValue);
        totalValue.Should().BePositive();
    }

    [Fact]
    public void WarehouseLotSummary_WeightedAverageCost_WhenSingleLotWithZeroQuantity_ShouldReturnZero()
    {
        // Arrange - edge case: lot exists but has zero quantity
        var emptyLot = WarehouseLotBuilder.Create()
            .WithEmptyQuantity() // Quantity = 0
            .WithUnitCost(150) // Has cost but no quantity
            .Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot> { emptyLot }
        };

        // Act
        var weightedAverageCost = summary.WeightedAverageCost;

        // Assert - should return zero when total quantity is zero (avoid division by zero)
        weightedAverageCost.Should().Be(0);
        summary.TotalQuantity.Should().Be(0);
        summary.TotalValue.Should().Be(0); // 0 quantity * any cost = 0 value
    }

    [Fact]
    public void WarehouseLotSummary_WeightedAverageCost_WhenMultipleLotsOneWithZeroQuantity_ShouldIgnoreZeroQuantityLots()
    {
        // Arrange - realistic scenario: some lots depleted, others active
        var emptyLot = WarehouseLotBuilder.Create()
            .WithEmptyQuantity() // Quantity = 0 (depleted)
            .WithUnitCost(999) // High cost but doesn't matter due to zero quantity
            .Build();

        var activeLot1 = WarehouseLotBuilder.Create()
            .WithQuantity(1000)
            .WithUnitCost(100)
            .Build();

        var activeLot2 = WarehouseLotBuilder.Create()
            .WithQuantity(2000)
            .WithUnitCost(200)
            .Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Lots = new List<WarehouseLot> { emptyLot, activeLot1, activeLot2 }
        };

        // Act
        var weightedAverageCost = summary.WeightedAverageCost;

        // Assert - should only consider active lots: (1000*100 + 2000*200) / (1000+2000)
        const long expectedCost = (100000 + 400000) / (1000 + 2000); // 500000 / 3000 = 166
        weightedAverageCost.Should().Be(expectedCost);
        summary.TotalQuantity.Should().Be(3000); // Only active lots counted
    }

    #endregion
}