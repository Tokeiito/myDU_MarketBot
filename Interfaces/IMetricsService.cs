using System;
using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Centralized metrics service that provides methods for updating metrics
    /// and exposes them via Prometheus endpoint.
    /// </summary>
    public interface IMetricsService
    {
        /// <summary>
        /// Increment a counter metric by the specified value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to increment by (default: 1)</param>
        void Increment(string name, double value = 1);
        
        /// <summary>
        /// Increment a counter metric with labels by the specified value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="labels">Label values</param>
        /// <param name="value">Value to increment by (default: 1)</param>
        void Increment(string name, string[] labels, double value = 1);
        
        /// <summary>
        /// Decrement a counter metric by the specified value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to decrement by (default: 1)</param>
        void Decrement(string name, double value = 1);
        
        /// <summary>
        /// Set a gauge metric to a specific value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to set</param>
        void SetGauge(string name, double value);
        
        /// <summary>
        /// Set a gauge metric with labels to a specific value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="labels">Label values</param>
        /// <param name="value">Value to set</param>
        void SetGauge(string name, string[] labels, double value);
        
        /// <summary>
        /// Set a complex metric that may be cached with TTL
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <param name="value">Value to set</param>
        /// <param name="ttl">Optional TTL for caching</param>
        void SetComplexMetric(string name, double value, TimeSpan? ttl = null);
        
        /// <summary>
        /// Get a cached complex metric value
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <returns>Cached value if available</returns>
        Task<double?> GetCachedMetric(string name);
        
        /// <summary>
        /// Initialize metrics system and start Prometheus server
        /// </summary>
        Task Initialize();
        
        /// <summary>
        /// Stop metrics server and cleanup resources
        /// </summary>
        Task Shutdown();
    }
}