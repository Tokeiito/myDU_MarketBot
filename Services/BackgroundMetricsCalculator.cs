using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services
{
    public class BackgroundMetricsCalculator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMetricsService _metricsService;
        private readonly ILogger<BackgroundMetricsCalculator> _logger;
        private readonly TimeSpan _calculationInterval;
        private IEnumerable<IMetricsProvider> _providers;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _backgroundTask;

        public BackgroundMetricsCalculator(
            IServiceProvider serviceProvider,
            IMetricsService metricsService,
            ILogger<BackgroundMetricsCalculator> logger)
        {
            _serviceProvider = serviceProvider;
            _metricsService = metricsService;
            _logger = logger;
            _calculationInterval = TimeSpan.FromMinutes(5); // TODO: Make configurable
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Initialize metrics service first
                await _metricsService.Initialize();
                
                // Auto-discover all metrics providers
                _providers = _serviceProvider.GetServices<IMetricsProvider>().ToList();
                
                _logger.LogInformation("Discovered {ProviderCount} metrics providers: {Providers}",
                    _providers.Count(),
                    string.Join(", ", _providers.Select(p => p.ProviderName)));
                
                // Initialize simple metrics for all providers
                await InitializeAllMetrics();
                
                // Start background task
                _backgroundTask = Task.Run(() => ExecuteAsync(_cancellationTokenSource.Token), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start BackgroundMetricsCalculator");
                throw;
            }
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackgroundMetricsCalculator started with interval: {Interval}", _calculationInterval);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CalculateComplexMetrics();
                    await Task.Delay(_calculationInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, break the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during complex metrics calculation");
                    // Continue running even if one calculation cycle fails
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Shorter delay on error
                }
            }
        }

        public async Task InitializeAllMetrics()
        {
            var tasks = new List<Task>();
            
            foreach (var provider in _providers)
            {
                tasks.Add(InitializeProviderMetrics(provider));
            }
            
            await Task.WhenAll(tasks);
            _logger.LogInformation("Initialized metrics for all providers");
        }

        private async Task InitializeProviderMetrics(IMetricsProvider provider)
        {
            try
            {
                _logger.LogDebug("Initializing metrics for provider: {Provider}", provider.ProviderName);
                await provider.InitializeMetrics(_metricsService);
                
                _logger.LogInformation("Initialized {SimpleMetrics} simple metrics for {Provider}",
                    provider.SimpleMetrics.Count(), provider.ProviderName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize metrics for provider: {Provider}", provider.ProviderName);
            }
        }

        public async Task CalculateComplexMetrics()
        {
            var tasks = new List<Task>();
            
            foreach (var provider in _providers)
            {
                if (provider.ComplexMetrics.Any())
                {
                    tasks.Add(CalculateProviderComplexMetrics(provider));
                }
            }
            
            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
                _logger.LogDebug("Calculated complex metrics for {ProviderCount} providers", tasks.Count);
            }
        }

        private async Task CalculateProviderComplexMetrics(IMetricsProvider provider)
        {
            try
            {
                _logger.LogDebug("Calculating complex metrics for provider: {Provider}", provider.ProviderName);
                await provider.CalculateComplexMetrics(_metricsService);
                
                _logger.LogDebug("Calculated {ComplexMetrics} complex metrics for {Provider}",
                    provider.ComplexMetrics.Count(), provider.ProviderName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate complex metrics for provider: {Provider}", provider.ProviderName);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Stopping BackgroundMetricsCalculator");
            _cancellationTokenSource?.Cancel();
            if (_backgroundTask != null)
            {
                await _backgroundTask;
            }
            await _metricsService.Shutdown();
            _cancellationTokenSource?.Dispose();
        }
    }
}