using System;
using FluentAssertions;
using MarketBot.Domain;
using MarketBot.TestUtilities.Builders;
using Xunit;

namespace MarketBot.UnitTests.Domain;

/// <summary>
/// Unit tests for WarehouseLot domain model - Method-level testing focusing on happy path scenarios
/// </summary>
[Trait("Category", "Unit")]
[Trait("Performance", "Fast")]
public class WarehouseLotUnitTests
{
    [Fact]
    public void Constructor_WhenValidParameters_ShouldCreateLotWithCorrectProperties()
    {
        // Arrange
        const string expectedLotId = "test-lot-123";
        const ulong expectedItemId = 1001;
        const ulong expectedMarketId = 1;
        const long expectedQuantity = 1000;
        const long expectedUnitCost = 500;

        // Act
        var lot = new WarehouseLot(expectedLotId, expectedItemId, expectedMarketId, expectedQuantity, expectedUnitCost);

        // Assert
        lot.LotId.Should().Be(expectedLotId);
        lot.ItemId.Should().Be(expectedItemId);
        lot.MarketId.Should().Be(expectedMarketId);
        lot.Quantity.Should().Be(expectedQuantity);
        lot.UnitCost.Should().Be(expectedUnitCost);
        lot.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TotalValue_WhenStandardLot_ShouldReturnCorrectCalculation()
    {
        // Arrange
        var lot = WarehouseLotBuilder.Create()
            .WithQuantity(1000)
            .WithUnitCost(500)
            .Build();

        // Act
        var totalValue = lot.TotalValue;

        // Assert
        totalValue.Should().Be(500000); // 1000 * 500
    }

    [Fact]
    public void IsEmpty_WhenQuantityIsZero_ShouldReturnTrue()
    {
        // Arrange
        var lot = WarehouseLotBuilder.Create()
            .WithEmptyQuantity()
            .Build();

        // Act
        var isEmpty = lot.IsEmpty;

        // Assert
        isEmpty.Should().BeTrue();
    }

    [Fact]
    public void CreateCopy_WhenCalled_ShouldReturnNewInstanceWithSameProperties()
    {
        // Arrange
        var originalLot = WarehouseLotBuilder.Create()
            .WithLotId("original-lot-123")
            .WithItemId(12345)
            .WithMarketId(1)
            .WithQuantity(500)
            .WithUnitCost(100)
            .Build();
        
        var newLotId = "copy-lot-456";
        var newQuantity = 200L;
        
        // Act
        var copiedLot = originalLot.CreateCopy(newLotId, newQuantity);
        
        // Assert
        copiedLot.Should().NotBeSameAs(originalLot); // Different object instances
        copiedLot.LotId.Should().Be(newLotId);
        copiedLot.Quantity.Should().Be(newQuantity);
        copiedLot.ItemId.Should().Be(originalLot.ItemId);
        copiedLot.MarketId.Should().Be(originalLot.MarketId);
        copiedLot.UnitCost.Should().Be(originalLot.UnitCost);
        copiedLot.CreatedAt.Should().Be(originalLot.CreatedAt); // Should preserve original creation time
    }

    #region HIGH Priority Tests - Input Validation

    [Fact]
    public void Constructor_WhenNullLotId_ShouldThrowArgumentNullException()
    {
        // Arrange
        string? nullLotId = null;
        const ulong itemId = 1001;
        const ulong marketId = 1;
        const long quantity = 1000;
        const long unitCost = 500;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new WarehouseLot(nullLotId!, itemId, marketId, quantity, unitCost));
        
        exception.ParamName.Should().Be("lotId");
    }

    [Fact]
    public void Constructor_WhenNegativeQuantity_ShouldCreateLotWithNegativeQuantity()
    {
        // Arrange
        const string lotId = "test-lot-negative";
        const ulong itemId = 1001;
        const ulong marketId = 1;
        const long negativeQuantity = -500; // Could represent returns/refunds
        const long unitCost = 100;

        // Act
        var lot = new WarehouseLot(lotId, itemId, marketId, negativeQuantity, unitCost);

        // Assert
        lot.Quantity.Should().Be(negativeQuantity);
        lot.IsEmpty.Should().BeTrue(); // Negative quantity should be considered empty
    }

    [Fact]
    public void Constructor_WhenNegativeUnitCost_ShouldCreateLotWithNegativeCost()
    {
        // Arrange - scenario: player gets credits for selling items
        const string lotId = "credit-lot";
        const ulong itemId = 1001;
        const ulong marketId = 1;
        const long quantity = 1000;
        const long negativeCost = -50; // Credits received

        // Act
        var lot = new WarehouseLot(lotId, itemId, marketId, quantity, negativeCost);

        // Assert
        lot.UnitCost.Should().Be(negativeCost);
        lot.TotalValue.Should().Be(quantity * negativeCost); // Should be negative total value
    }

    [Fact]
    public void TotalValue_WhenNegativeValues_ShouldHandleCorrectly()
    {
        // Arrange - test various combinations of negative values
        var testCases = new[]
        {
            (Quantity: -100L, UnitCost: 50L, ExpectedTotalValue: -5000L), // Negative qty, positive cost
            (Quantity: 100L, UnitCost: -50L, ExpectedTotalValue: -5000L), // Positive qty, negative cost  
            (Quantity: -100L, UnitCost: -50L, ExpectedTotalValue: 5000L)  // Both negative = positive
        };

        foreach (var (quantity, unitCost, expectedTotalValue) in testCases)
        {
            // Act
            var lot = WarehouseLotBuilder.Create()
                .WithQuantity(quantity)
                .WithUnitCost(unitCost)
                .Build();

            // Assert
            lot.TotalValue.Should().Be(expectedTotalValue, 
                $"because {quantity} * {unitCost} should equal {expectedTotalValue}");
        }
    }

    [Fact]
    public void CreateCopy_WhenNullNewLotId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var originalLot = WarehouseLotBuilder.Create().Build();
        string? nullNewLotId = null;
        const long newQuantity = 100;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            originalLot.CreateCopy(nullNewLotId!, newQuantity));
        
        exception.ParamName.Should().Be("lotId");
    }

    [Fact]
    public void CreateCopy_WhenOriginalIsEmpty_ShouldCreateEmptyCopy()
    {
        // Arrange
        var emptyLot = WarehouseLotBuilder.Create()
            .WithEmptyQuantity() // Quantity = 0
            .Build();
        
        const string newLotId = "empty-copy";
        const long newQuantity = 0; // Also empty

        // Act
        var copiedLot = emptyLot.CreateCopy(newLotId, newQuantity);

        // Assert
        copiedLot.IsEmpty.Should().BeTrue();
        copiedLot.Quantity.Should().Be(0);
        copiedLot.LotId.Should().Be(newLotId);
    }

    #endregion
}
