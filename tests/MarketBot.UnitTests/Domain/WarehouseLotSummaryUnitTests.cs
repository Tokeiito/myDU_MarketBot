using System;
using System.Collections.Generic;
using FluentAssertions;
using MarketBot.Domain;
using MarketBot.TestUtilities.Builders;
using Xunit;

namespace MarketBot.UnitTests.Domain;

/// <summary>
/// Unit tests for WarehouseLotSummary domain model - Method-level testing focusing on happy path scenarios
/// </summary>
[Trait("Category", "Unit")]
[Trait("Performance", "Fast")]
public class WarehouseLotSummaryUnitTests
{
    [Fact]
    public void TotalQuantity_WhenMultipleLots_ShouldSumCorrectly()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithQuantity(1000).Build(),
                WarehouseLotBuilder.Create().WithQuantity(500).Build(),
                WarehouseLotBuilder.Create().WithQuantity(2000).Build()
            }
        };

        // Act
        var totalQuantity = summary.TotalQuantity;

        // Assert
        totalQuantity.Should().Be(3500); // 1000 + 500 + 2000
    }

    [Fact]
    public void TotalValue_WhenMultipleLots_ShouldSumCorrectly()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithQuantity(1000).WithUnitCost(100).Build(), // Value: 100,000
                WarehouseLotBuilder.Create().WithQuantity(500).WithUnitCost(200).Build(),  // Value: 100,000
                WarehouseLotBuilder.Create().WithQuantity(2000).WithUnitCost(150).Build()  // Value: 300,000
            }
        };

        // Act
        var totalValue = summary.TotalValue;

        // Assert
        totalValue.Should().Be(500000); // 100,000 + 100,000 + 300,000
    }

    [Fact]
    public void WeightedAverageCost_WhenQuantityIsNonZero_ShouldCalculateCorrectly()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithQuantity(1000).WithUnitCost(100).Build(), // Value: 100,000
                WarehouseLotBuilder.Create().WithQuantity(2000).WithUnitCost(200).Build()  // Value: 400,000
            }
        };

        // Act
        var weightedAverageCost = summary.WeightedAverageCost;

        // Assert
        // Total value: 500,000, Total quantity: 3,000 => Avg cost: 166.67 (rounded down to 166)
        weightedAverageCost.Should().Be(166); // 500,000 / 3,000 = 166 (integer division)
    }

    [Fact]
    public void WeightedAverageCost_WhenQuantityIsZero_ShouldReturnZero()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>() // Empty lots list
        };

        // Act
        var weightedAverageCost = summary.WeightedAverageCost;

        // Assert
        weightedAverageCost.Should().Be(0);
    }

    [Fact]
    public void LotCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().Build(),
                WarehouseLotBuilder.Create().Build(),
                WarehouseLotBuilder.Create().Build()
            }
        };

        // Act
        var lotCount = summary.LotCount;

        // Assert
        lotCount.Should().Be(3);
    }

    [Fact]
    public void LowestCost_WhenLotsExist_ShouldReturnMinimumCost()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithUnitCost(300).Build(),
                WarehouseLotBuilder.Create().WithUnitCost(100).Build(), // Lowest
                WarehouseLotBuilder.Create().WithUnitCost(200).Build()
            }
        };

        // Act
        var lowestCost = summary.LowestCost;

        // Assert
        lowestCost.Should().Be(100);
    }

    [Fact]
    public void LowestCost_WhenNoLots_ShouldReturnZero()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>() // Empty lots list
        };

        // Act
        var lowestCost = summary.LowestCost;

        // Assert
        lowestCost.Should().Be(0);
    }

    [Fact]
    public void HighestCost_WhenLotsExist_ShouldReturnMaximumCost()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithUnitCost(100).Build(),
                WarehouseLotBuilder.Create().WithUnitCost(300).Build(), // Highest
                WarehouseLotBuilder.Create().WithUnitCost(200).Build()
            }
        };

        // Act
        var highestCost = summary.HighestCost;

        // Assert
        highestCost.Should().Be(300);
    }

    [Fact]
    public void HighestCost_WhenNoLots_ShouldReturnZero()
    {
        // Arrange
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>() // Empty lots list
        };

        // Act
        var highestCost = summary.HighestCost;

        // Assert
        highestCost.Should().Be(0);
    }

    [Fact]
    public void GetLotsSortedByCost_ShouldReturnLotsOrderedByUnitCostThenCreatedAt()
    {
        // Arrange
        var earlierTime = DateTime.UtcNow.AddHours(-2);
        var laterTime = DateTime.UtcNow.AddHours(-1);
        
        var lot1 = WarehouseLotBuilder.Create().WithUnitCost(200).WithCreatedAt(laterTime).Build();
        var lot2 = WarehouseLotBuilder.Create().WithUnitCost(100).WithCreatedAt(earlierTime).Build(); // Cheapest, earliest
        var lot3 = WarehouseLotBuilder.Create().WithUnitCost(100).WithCreatedAt(laterTime).Build();   // Cheapest, later
        var lot4 = WarehouseLotBuilder.Create().WithUnitCost(300).WithCreatedAt(earlierTime).Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot> { lot1, lot2, lot3, lot4 } // Unsorted order
        };

        // Act
        var sortedLots = summary.GetLotsSortedByCost();

        // Assert
        sortedLots.Should().HaveCount(4);
        sortedLots[0].Should().BeSameAs(lot2); // UnitCost 100, earlier time
        sortedLots[1].Should().BeSameAs(lot3); // UnitCost 100, later time
        sortedLots[2].Should().BeSameAs(lot1); // UnitCost 200
        sortedLots[3].Should().BeSameAs(lot4); // UnitCost 300
    }

    #region HIGH Priority Tests - Division by Zero & Edge Cases

    [Fact]
    public void WeightedAverageCost_WhenLotHasZeroCost_ShouldNotCauseException()
    {
        // Arrange - scenario: mined resources with zero cost
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithQuantity(1000).WithZeroCost().Build(), // Zero cost lot
                WarehouseLotBuilder.Create().WithQuantity(500).WithUnitCost(200).Build()  // Normal cost lot
            }
        };

        // Act
        var weightedAverageCost = summary.WeightedAverageCost;

        // Assert
        weightedAverageCost.Should().Be(66); // (0 + 100,000) / (1000 + 500) = 100,000 / 1500 = 66
    }

    [Fact] 
    public void TotalQuantity_WhenSomeLotsAreEmpty_ShouldSumOnlyNonEmptyLots()
    {
        // Arrange - mix of empty and non-empty lots
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithQuantity(1000).Build(), // Normal lot
                WarehouseLotBuilder.Create().WithEmptyQuantity().Build(), // Empty lot (0)
                WarehouseLotBuilder.Create().WithQuantity(500).Build(),  // Normal lot
                WarehouseLotBuilder.Create().WithQuantity(-100).Build()  // Negative lot (returns)
            }
        };

        // Act
        var totalQuantity = summary.TotalQuantity;

        // Assert
        totalQuantity.Should().Be(1400); // 1000 + 0 + 500 + (-100) = 1400
    }

    [Fact]
    public void HighestCost_WhenAllLotsHaveNegativeCosts_ShouldReturnLeastNegativeCost()
    {
        // Arrange - scenario: all lots are credits/refunds
        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot>
            {
                WarehouseLotBuilder.Create().WithUnitCost(-100).Build(), // -100 credits
                WarehouseLotBuilder.Create().WithUnitCost(-50).Build(),  // -50 credits (highest/least negative)
                WarehouseLotBuilder.Create().WithUnitCost(-200).Build()  // -200 credits
            }
        };

        // Act
        var highestCost = summary.HighestCost;

        // Assert
        highestCost.Should().Be(-50); // Least negative = "highest"
    }

    [Fact]
    public void GetLotsSortedByCost_WhenLotsHaveSameUnitCostAndCreatedAt_ShouldMaintainStableSort()
    {
        // Arrange - identical lots to test stable sorting
        var identicalTime = DateTime.UtcNow.AddHours(-1);
        const long identicalCost = 150;
        
        var lot1 = WarehouseLotBuilder.Create().WithLotId("lot-1").WithUnitCost(identicalCost).WithCreatedAt(identicalTime).Build();
        var lot2 = WarehouseLotBuilder.Create().WithLotId("lot-2").WithUnitCost(identicalCost).WithCreatedAt(identicalTime).Build();
        var lot3 = WarehouseLotBuilder.Create().WithLotId("lot-3").WithUnitCost(identicalCost).WithCreatedAt(identicalTime).Build();

        var summary = new WarehouseLotSummary
        {
            ItemId = 1001,
            MarketId = 1,
            Lots = new List<WarehouseLot> { lot1, lot2, lot3 } // Original order
        };

        // Act
        var sortedLots = summary.GetLotsSortedByCost();

        // Assert - should maintain original order when all factors are equal
        sortedLots.Should().HaveCount(3);
        sortedLots[0].Should().BeSameAs(lot1); // Original order preserved
        sortedLots[1].Should().BeSameAs(lot2);
        sortedLots[2].Should().BeSameAs(lot3);
    }

    #endregion
}
