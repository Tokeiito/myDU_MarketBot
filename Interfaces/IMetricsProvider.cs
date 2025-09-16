using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Interface that business services implement to provide their metrics functionality.
    /// Services implementing this interface will be automatically discovered and managed
    /// by the BackgroundMetricsCalculator.
    /// </summary>
    public interface IMetricsProvider
    {
        /// <summary>
        /// Unique name identifying this metrics provider
        /// </summary>
        string ProviderName { get; }
        
        /// <summary>
        /// List of simple metrics that this provider manages.
        /// These are updated in real-time during business operations.
        /// </summary>
        IEnumerable<string> SimpleMetrics { get; }
        
        /// <summary>
        /// List of complex metrics that this provider calculates.
        /// These are calculated periodically by the background calculator.
        /// </summary>
        IEnumerable<string> ComplexMetrics { get; }
        
        /// <summary>
        /// Initialize simple metrics by recalculating from current Redis state.
        /// Called during application startup to restore metrics after restart.
        /// </summary>
        /// <param name="metricsService">The metrics service to update</param>
        Task InitializeMetrics(IMetricsService metricsService);
        
        /// <summary>
        /// Calculate and update complex metrics that require expensive operations.
        /// Called periodically by the background calculator.
        /// </summary>
        /// <param name="metricsService">The metrics service to update</param>
        Task CalculateComplexMetrics(IMetricsService metricsService);
    }
}