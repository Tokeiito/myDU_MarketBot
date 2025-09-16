using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using Prometheus;
using StackExchange.Redis;

namespace MarketBot.Services
{
    public class MetricsService : IMetricsService
    {
        private readonly ILogger<MetricsService> _logger;
        private readonly IDatabase _redis;
        private readonly ConcurrentDictionary<string, Counter> _counters;
        private readonly ConcurrentDictionary<string, Gauge> _gauges;
        private readonly ConcurrentDictionary<string, DateTime> _complexMetricCache;
        private MetricServer _prometheusServer;
        private bool _initialized;

        public MetricsService(ILogger<MetricsService> logger, IDatabase redis)
        {
            _logger = logger;
            _redis = redis;
            _counters = new ConcurrentDictionary<string, Counter>();
            _gauges = new ConcurrentDictionary<string, Gauge>();
            _complexMetricCache = new ConcurrentDictionary<string, DateTime>();
        }

        public Task Initialize()
        {
            if (_initialized)
                return Task.CompletedTask;

            try
            {
                // Start Prometheus metrics server on port 9100
                _prometheusServer = new MetricServer(hostname: "*", port: 9100);
                _prometheusServer.Start();
                
                _logger.LogInformation("Prometheus metrics server started on port 9100");
                _initialized = true;
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize metrics server");
                throw;
            }
        }

        public void Increment(string name, double value = 1)
        {
            try
            {
                var counter = _counters.GetOrAdd(name, n => 
                    Metrics.CreateCounter($"marketbot_{n}", $"MarketBot metric: {n}"));
                counter.Inc(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to increment metric {MetricName}", name);
            }
        }

        public void Decrement(string name, double value = 1)
        {
            try
            {
                var gauge = _gauges.GetOrAdd(name, n => 
                    Metrics.CreateGauge($"marketbot_{n}", $"MarketBot metric: {n}"));
                gauge.Dec(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrement metric {MetricName}", name);
            }
        }

        public void SetGauge(string name, double value)
        {
            try
            {
                var gauge = _gauges.GetOrAdd(name, n => 
                    Metrics.CreateGauge($"marketbot_{n}", $"MarketBot metric: {n}"));
                gauge.Set(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set gauge metric {MetricName}", name);
            }
        }

        public void SetGauge(string name, string[] labels, double value)
        {
            try
            {
                var gauge = _gauges.GetOrAdd(name, n => 
                    Metrics.CreateGauge($"marketbot_{n}", $"MarketBot metric: {n}", 
                                      new string[] { "market_id", "item_id" }));
                gauge.WithLabels(labels).Set(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set gauge metric {MetricName} with labels", name);
            }
        }

        public void SetComplexMetric(string name, double value, TimeSpan? ttl = null)
        {
            try
            {
                // Set the Prometheus metric
                var gauge = _gauges.GetOrAdd(name, n => 
                    Metrics.CreateGauge($"marketbot_{n}", $"MarketBot complex metric: {n}"));
                gauge.Set(value);

                // Cache in Redis if TTL is specified
                if (ttl.HasValue)
                {
                    var cacheKey = $"metrics:complex:{name}";
                    _redis.StringSetAsync(cacheKey, value, ttl.Value);
                    _complexMetricCache[name] = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set complex metric {MetricName}", name);
            }
        }

        public async Task<double?> GetCachedMetric(string name)
        {
            try
            {
                var cacheKey = $"metrics:complex:{name}";
                var cachedValue = await _redis.StringGetAsync(cacheKey);
                
                if (cachedValue.HasValue && cachedValue.TryParse(out double value))
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get cached metric {MetricName}", name);
            }
            
            return null;
        }

        public Task Shutdown()
        {
            try
            {
                _prometheusServer?.Stop();
                _logger.LogInformation("Prometheus metrics server stopped");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics service shutdown");
                return Task.FromException(ex);
            }
        }
    }
}