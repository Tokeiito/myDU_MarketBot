using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Service for managing local resource capacity limits and generation throttling.
    /// Prevents infinite resource accumulation while maintaining operational workflows.
    /// </summary>
    public interface IInventoryCapacityService
    {
        /// <summary>
        /// Checks if resource generation is allowed based on current local capacity.
        /// </summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="displayQuantity">Quantity to generate in display units</param>
        /// <param name="marketId">Market identifier</param>
        /// <returns>True if generation is allowed, false if capacity would be exceeded</returns>
        Task<bool> CanGenerateResource(ulong resourceId, long displayQuantity, ulong marketId);

        /// <summary>
        /// Gets current local resource utilization as percentage of capacity.
        /// </summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="marketId">Market identifier</param>
        /// <returns>Utilization percentage (0.0 to 1.0), or -1.0 if unlimited</returns>
        Task<double> GetLocalResourceUtilization(ulong resourceId, ulong marketId);

        /// <summary>
        /// Gets the current local resource capacity limit from configuration.
        /// </summary>
        /// <returns>Capacity limit (-1 = unlimited, 0 = no generation, positive = limit)</returns>
        long GetLocalResourceCapacityLimit();

        /// <summary>
        /// Checks if local resource capacity limits are enabled.
        /// </summary>
        /// <returns>True if capacity limits are active</returns>
        bool IsCapacityLimitEnabled();
    }
}