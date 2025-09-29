namespace MarketBot.Interfaces
{
    /// <summary>
    /// Interface for configuration service to enable better testability
    /// </summary>
    public interface IConfigService
    {
        /// <summary>
        /// The current MarketBot configuration
        /// </summary>
        MarketBotConfig Config { get; }
    }
}