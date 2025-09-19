namespace MarketBot.Domain
{
    public sealed class ResourceDefinition
    {
        public ulong Id { get; init; }
        public string Name { get; init; }  // yaml.displayName (user-friendly)
        public string StrId { get; init; } // item_definition.name (internal identifier)
        public int Tier { get; init; }     // yaml.level for ores; 5 for plasmas
        public string Kind { get; init; }  // "Ore" or "Plasma"
    }
}
