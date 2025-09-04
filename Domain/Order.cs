using NQ;

public class Order
{
    public ulong OrderId { get; set; }
    public ulong ItemId { get; set; }
    public long Quantity { get; set; }
    public long Price { get; set; }
    public ulong MarketId { get; set; }
    public TimePoint? Expiration { get; set; }
}