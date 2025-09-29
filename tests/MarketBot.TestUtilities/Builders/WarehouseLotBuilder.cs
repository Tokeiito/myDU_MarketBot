using System;
using MarketBot.Domain;

namespace MarketBot.TestUtilities.Builders;

/// <summary>
/// Builder pattern for creating WarehouseLot test data
/// </summary>
public class WarehouseLotBuilder
{
    private string _lotId = Guid.NewGuid().ToString("N")[..12];
    private ulong _itemId = 1001;
    private ulong? _marketId = 1;
    private long _quantity = 1000;
    private long _unitCost = 100;
    private DateTime _createdAt = DateTime.UtcNow;

    public static WarehouseLotBuilder Create() => new();

    public WarehouseLotBuilder WithLotId(string lotId)
    {
        _lotId = lotId;
        return this;
    }

    public WarehouseLotBuilder WithItemId(ulong itemId)
    {
        _itemId = itemId;
        return this;
    }

    public WarehouseLotBuilder WithMarketId(ulong? marketId)
    {
        _marketId = marketId;
        return this;
    }

    public WarehouseLotBuilder WithQuantity(long quantity)
    {
        _quantity = quantity;
        return this;
    }

    public WarehouseLotBuilder WithUnitCost(long unitCost)
    {
        _unitCost = unitCost;
        return this;
    }

    public WarehouseLotBuilder WithCreatedAt(DateTime createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    /// <summary>
    /// Create a zero cost lot (mined resources)
    /// </summary>
    public WarehouseLotBuilder WithZeroCost()
    {
        _unitCost = 0;
        return this;
    }

    /// <summary>
    /// Create a high-value lot with large quantities
    /// </summary>
    public WarehouseLotBuilder WithHighValue()
    {
        _quantity = 100000;
        _unitCost = 5000;
        return this;
    }

    /// <summary>
    /// Create a small quantity lot for precision testing
    /// </summary>
    public WarehouseLotBuilder WithSmallQuantity()
    {
        _quantity = 1;
        _unitCost = 1;
        return this;
    }

    /// <summary>
    /// Create an empty lot
    /// </summary>
    public WarehouseLotBuilder WithEmptyQuantity()
    {
        _quantity = 0;
        return this;
    }

    /// <summary>
    /// Create a global market lot (null market ID)
    /// </summary>
    public WarehouseLotBuilder WithGlobalMarket()
    {
        _marketId = null;
        return this;
    }

    public WarehouseLot Build()
    {
        var lot = new WarehouseLot(_lotId, _itemId, _marketId, _quantity, _unitCost)
        {
            CreatedAt = _createdAt
        };
        return lot;
    }
}