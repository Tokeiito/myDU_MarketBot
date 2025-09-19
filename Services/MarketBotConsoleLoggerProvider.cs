using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services
{
    /// <summary>
    /// Custom console logger provider that only logs MarketBot services to console
    /// Works alongside the existing NQutils/Serilog setup
    /// </summary>
    public class MarketBotConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LoggingSettings _config;
        private readonly ConcurrentDictionary<string, MarketBotConsoleLogger> _loggers = new();

        public MarketBotConsoleLoggerProvider(LoggingSettings config)
        {
            _config = config;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new MarketBotConsoleLogger(name, _config));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }

    /// <summary>
    /// Custom console logger that only outputs MarketBot service logs to console
    /// </summary>
    public class MarketBotConsoleLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly LoggingSettings _config;

        public MarketBotConsoleLogger(string categoryName, LoggingSettings config)
        {
            _categoryName = categoryName;
            _config = config;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            // Only enable for MarketBot services
            if (!_categoryName.StartsWith("MarketBot", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check if this specific service has a configured log level
            if (_config.ServiceLogLevels.TryGetValue(_categoryName, out var configuredLevel))
            {
                // Special case: "Off" disables all console logging for this service
                if (configuredLevel.Equals("Off", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                
                if (Enum.TryParse<LogLevel>(configuredLevel, true, out var level))
                {
                    return logLevel >= level;
                }
            }

            // Check if global console logging is disabled
            if (_config.ConsoleLogLevel.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Fallback to default console log level
            if (Enum.TryParse<LogLevel>(_config.ConsoleLogLevel, true, out var defaultLevel))
            {
                return logLevel >= defaultLevel;
            }

            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelString = GetLogLevelString(logLevel);
            var serviceName = GetShortServiceName(_categoryName);
            var message = formatter(state, exception);

            // Custom MarketBot console format
            Console.WriteLine($"[{timestamp}] [{levelString} {serviceName}] {message}");

            if (exception != null)
            {
                Console.WriteLine($"[{timestamp}] [{levelString} {serviceName}] Exception: {exception}");
            }
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG", 
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "UNK"
            };
        }

        private static string GetShortServiceName(string fullName)
        {
            // Convert "MarketBot.Services.WorldModel.ResourceGenerationService" 
            // to "ResourceGenerationService" for cleaner console output
            if (fullName.StartsWith("MarketBot.Services."))
            {
                var parts = fullName.Split('.');
                return parts[parts.Length - 1];
            }
            return fullName;
        }
    }
}