using System;

public class Item
{
    public ulong Id { get; set; }
    public string ItemType { get; set; }
    
    /// <summary>
    /// Raw quantity as stored in the database.
    /// For materials, this is the volume multiplied by MATERIAL_MULTIPLIER.
    /// For non-materials, this equals the display quantity.
    /// </summary>
    public long RawQuantity { get; set; }
    
    /// <summary>
    /// Legacy property for backward compatibility.
    /// Returns RawQuantity directly - callers should migrate to using RawQuantity or DisplayQuantity explicitly.
    /// </summary>
    public long Quantity
    {
        get => RawQuantity;
        set => RawQuantity = value;
    }
}
