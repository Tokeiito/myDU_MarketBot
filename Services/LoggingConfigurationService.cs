using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace MarketBot.Services
{
    /// <summary>
    /// Service responsible for configuring logging based on MarketBot configuration
    /// Works with the existing Serilog setup from NQutils.Logging
    /// </summary>
    public static class LoggingConfigurationService
    {
        /// <summary>
        /// Configure MarketBot-specific console logging that works alongside NQutils.Logging
        /// Adds a custom console provider specifically for MarketBot services
        /// </summary>
        /// <param name="loggerFactory">The logger factory to configure</param>
        /// <param name="configService">The configuration service to read settings from</param>
        public static void ConfigureMarketBotLogging(ILoggerFactory loggerFactory, ConfigService configService)
        {
            var loggingConfig = configService.Config.Logging;
            
            Console.WriteLine($"MarketBot Logging Configuration: LogToConsole={loggingConfig.LogToConsole}, ServiceLogLevels Count={loggingConfig.ServiceLogLevels.Count}");
            
            if (!loggingConfig.LogToConsole)
            {
                Console.WriteLine("MarketBot console logging is disabled");
                return;
            }

            // Add a custom console provider specifically for MarketBot services
            loggerFactory.AddProvider(new MarketBotConsoleLoggerProvider(loggingConfig));
            
            foreach (var serviceLogLevel in loggingConfig.ServiceLogLevels)
            {
                Console.WriteLine($"Added MarketBot console logging for service '{serviceLogLevel.Key}' at level: {serviceLogLevel.Value}");
            }
        }
        
        /// <summary>
        /// Check if debug level logging is enabled for a specific service
        /// </summary>
        /// <param name="serviceName">Full service class name</param>
        /// <param name="configService">Configuration service</param>
        /// <returns>True if debug logging is enabled</returns>
        public static bool IsDebugEnabledForService(string serviceName, ConfigService configService)
        {
            var loggingConfig = configService.Config.Logging;
            
            if (!loggingConfig.LogToConsole)
                return false;
                
            if (loggingConfig.ServiceLogLevels.TryGetValue(serviceName, out var levelString))
            {
                if (Enum.TryParse<LogLevel>(levelString, true, out var level))
                {
                    return level <= LogLevel.Debug;
                }
            }
            
            // Default to checking against the ConsoleLogLevel
            if (Enum.TryParse<LogLevel>(loggingConfig.ConsoleLogLevel, true, out var defaultLevel))
            {
                return defaultLevel <= LogLevel.Debug;
            }
            
            return false;
        }

        /// <summary>
        /// Helper method to parse log level strings to LogLevel enum
        /// </summary>
        /// <param name="logLevelString">String representation of log level</param>
        /// <param name="defaultLevel">Default level if parsing fails</param>
        /// <returns>Parsed LogLevel</returns>
        public static LogLevel ParseLogLevel(string logLevelString, LogLevel defaultLevel = LogLevel.Information)
        {
            if (Enum.TryParse<LogLevel>(logLevelString, true, out var level))
            {
                return level;
            }
            return defaultLevel;
        }

        /// <summary>
        /// Get the full service class name for a given service type
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Full class name including namespace</returns>
        public static string GetServiceLoggerName<T>()
        {
            return typeof(T).FullName ?? typeof(T).Name;
        }
    }
}