public class Item
{
    public ulong Id { get; set; }
    public string ItemType { get; set; }
    private long _quantity;

    public long Quantity
    {
        get => _quantity;
        set
        {
            if (ItemType == "material")
            {
                _quantity = value << 24;
            }
            else
            {
                _quantity = value;
            }
        }
    }
}
