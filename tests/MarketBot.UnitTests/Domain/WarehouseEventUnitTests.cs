using FluentAssertions;
using MarketBot.Domain;
using Xunit;

namespace MarketBot.UnitTests.Domain;

/// <summary>
/// Unit tests for WarehouseEvent domain model subclasses - Method-level testing focusing on happy path scenarios
/// </summary>
[Trait("Category", "Unit")]
[Trait("Performance", "Fast")]
public class WarehouseEventUnitTests
{
    // Test constants - values that don't affect the actual test logic
    private const ulong TestItemId = 1001;
    private const ulong TestMarketId = 1;
    private const long TestQuantity = 1000;
    private const long TestUnitCost = 500;
    private const string TestLotId1 = "lot-123";
    private const string TestLotId2 = "lot-456";
    private const string TestNewLotId = "lot-789";

    [Fact]
    public void ItemsAddedEvent_EventType_ShouldReturnExpectedValue()
    {
        // Arrange
        var itemsAddedEvent = new ItemsAddedEvent
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            Quantity = TestQuantity,
            UnitCost = TestUnitCost
        };

        // Act
        var eventType = itemsAddedEvent.EventType;

        // Assert
        eventType.Should().Be("items_added");
    }

    [Fact]
    public void ItemsConsumedEvent_EventType_ShouldReturnExpectedValue()
    {
        // Arrange
        var itemsConsumedEvent = new ItemsConsumedEvent
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            ConsumedQuantity = TestQuantity / 2, // 500 - test-specific value
            WeightedAverageCost = 150 // Different from TestUnitCost - test-specific
        };

        // Act
        var eventType = itemsConsumedEvent.EventType;

        // Assert
        eventType.Should().Be("items_consumed");
    }

    [Fact]
    public void LotMergedEvent_EventType_ShouldReturnExpectedValue()
    {
        // Arrange
        var lotMergedEvent = new LotMergedEvent
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            OldLotId1 = TestLotId1,
            OldLotId2 = TestLotId2,
            NewLotId = TestNewLotId,
            NewUnitCost = 175 // Weighted average - test-specific value
        };

        // Act
        var eventType = lotMergedEvent.EventType;

        // Assert
        eventType.Should().Be("lot_merged");
    }

    [Fact]
    public void LotCreatedEvent_EventType_ShouldReturnExpectedValue()
    {
        // Arrange
        var lotCreatedEvent = new LotCreatedEvent
        {
            ItemId = TestItemId,
            MarketId = TestMarketId,
            LotId = TestNewLotId,
            UnitCost = TestUnitCost
        };

        // Act
        var eventType = lotCreatedEvent.EventType;

        // Assert
        eventType.Should().Be("lot_created");
    }
}