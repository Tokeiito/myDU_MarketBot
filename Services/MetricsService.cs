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
        
        public void Increment(string name, string[] labels, double value = 1)
        {
            try
            {
                var counter = _counters.GetOrAdd(name, n => 
                {
                    var labelNames = GetLabelNamesForMetric(n);
                    return Metrics.CreateCounter($"marketbot_{n}", $"MarketBot metric: {n}", labelNames);
                });
                counter.WithLabels(labels).Inc(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to increment metric {MetricName} with labels", name);
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
                {
                    var labelNames = GetLabelNamesForMetric(n);
                    return Metrics.CreateGauge($"marketbot_{n}", $"MarketBot metric: {n}", labelNames);
                });
                gauge.WithLabels(labels).Set(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set gauge metric {MetricName} with labels", name);
            }
        }

        private string[] GetLabelNamesForMetric(string metricName)
        {
            return metricName switch
            {
                // WorldModel metrics with specific label schemas
                "worldmodel_lai_by_market" => new[] { "planet_id", "market_id", "resource_id" },
                "worldmodel_generation_by_market" => new[] { "planet_id", "market_id", "resource_id" },
                "worldmodel_generation_by_planet" => new[] { "planet_id", "resource_id" },
                "worldmodel_generation_events_by_market" => new[] { "planet_id", "market_id" },
                "worldmodel_local_resource_capacity_utilization" => new[] { "planet_id", "market_id", "resource_id" },
                "worldmodel_generation_throttled_total" => new[] { "planet_id", "market_id", "resource_id" },
                "worldmodel_local_resource_quantity" => new[] { "planet_id", "market_id", "resource_id" },
                
                // WorldModel metrics without labels
                "worldmodel_update_cycles_total" => new string[0],
                "worldmodel_resource_generation_total" => new string[0],
                "worldmodel_generation_events_total" => new string[0],
                
                // Legacy WorldModel metrics (for backward compatibility)
                "worldmodel_lai_values" => new[] { "planet_id", "resource_id" },
                "worldmodel_resources_per_planet" => new[] { "planet_id" },
                
                // Inventory metrics with specific label schemas
                "inventory_unique_items_per_market" => new[] { "market_id" },
                "inventory_item_quantity" => new[] { "market_id", "item_id" },
                
                // Warehouse metrics with specific label schemas
                "warehouse_operations_total" => new[] { "mode" },
                "warehouse_lot_operations_total" => new[] { "mode" },
                "warehouse_consumption_operations_total" => new[] { "mode" },
                "warehouse_events_items_added_total" => new[] { "mode" },
                "warehouse_events_items_consumed_total" => new[] { "mode" },
                "warehouse_events_lot_merged_total" => new[] { "mode" },
                "warehouse_events_lot_created_total" => new[] { "mode" },
                "warehouse_unique_items_per_market" => new[] { "market_id" },
                "warehouse_lots_total" => new[] { "market_id" },
                "warehouse_item_quantity" => new[] { "market_id", "item_id" },
                "warehouse_total_value" => new[] { "market_id" },
                "warehouse_cost_variance" => new[] { "market_id", "item_id" },
                "warehouse_consumption_rate" => new[] { "market_id", "item_id" },
                
                // Default for other metrics
                _ => new[] { "market_id", "item_id" }
            };
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