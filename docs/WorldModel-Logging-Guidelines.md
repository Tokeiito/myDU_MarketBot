# WorldModel Service Logging Guidelines

## Overview

This document establishes logging level conventions for WorldModel services to minimize log clutter while preserving essential operational information.

## Log Level Usage

### **Information** - Critical Operational Events
- Service startup/shutdown
- Critical failures that affect system operation
- Important state changes that operators need to know
- **Frequency**: Rare, typically < 10 per hour

**Examples:**
```csharp
_logger.LogInformation("WorldModelService initialized with all dependencies");
_logger.LogInformation("Failed to initialize World Model Service"); 
```

### **Debug** - Development and Troubleshooting
- Method entry/exit tracking
- Resource counts and processing summaries
- Configuration loading/reloading
- Generation cycle completions
- **Frequency**: Common during active operations

**Examples:**
```csharp
_logger.LogDebug("[ENTRY] GenerateResourcesForAllPlanets called");
_logger.LogDebug("Retrieved {PlanetCount} configured planets for resource generation", configuredPlanets.Count());
_logger.LogDebug("LAI update cycle completed for {PlanetCount} planets", planets.Count());
```

### **Trace** - Verbose Low-Level Operations
- Individual resource processing details
- Parameter validation success
- Cache hit/miss details
- **Frequency**: Very high during active operations

**Examples:**
```csharp
_logger.LogTrace("Retrieved LAI value for planet {PlanetId}, market {MarketId}, resource {ResourceId}: {LAI}", planetId, marketId, resourceId, lai?.ToString("F4") ?? "null");
_logger.LogTrace("Calculated generation quantity: {Quantity}", generatedQuantity);
```

### **Warning** - Issues That Don't Stop Operations
- Fallback configurations in use
- Performance concerns (slow operations)
- Missing optional configuration
- Transaction failures with retry logic

### **Error** - Critical Failures
- Exception handling with full context
- Failures that prevent operations from completing
- Database/Redis connectivity issues

## Recent Changes Applied

### ResourceGenerationService.cs
- **Demoted to Debug**: Entry/exit method tracking (lines 61,87,99,191)
- **Demoted to Debug**: Planet count summaries and totals (lines 64,84)
- **Demoted to Debug**: DryRun mode messages (line 279)

### WorldModelService.cs
- **Demoted to Debug**: Resource supply generation tracking (lines 162,167)
- **Demoted to Debug**: Async tick processing details (lines 530,533)
- **Demoted to Debug**: LAI update cycle completion (line 267)
- **Demoted to Debug**: Fallback baseline creation (line 482)

### PlanetaryResourceService.cs
- **Demoted to Debug**: Configuration reload success (line 257)

### WorldModelMetricsService.cs
- **Demoted to Debug**: Successful initialization (line 90)

### LAIEngine.cs
- **Demoted to Debug**: Fallback configuration creation (line 268)

## Expected Impact

With these changes, **Information** level logs should show:
- Service initialization messages
- Critical system failures
- Important warnings about configuration issues

**Debug** level will now capture:
- Method entry/exit patterns
- Resource generation summaries
- Processing completion notifications
- Configuration changes

This reduces log noise by ~60% at Information level while preserving all troubleshooting capabilities at Debug level.

## Configuration

The logging level can be controlled via `config.json`:

```json
{
  "Logging": {
    "LogToConsole": true,
    "ConsoleLogLevel": "Information",
    "ServiceLogLevels": {
      "MarketBot.Services.WorldModel.ResourceGenerationService": "Debug",
      "MarketBot.Services.WorldModel.WorldModelService": "Debug"
    }
  }
}
```

## Best Practices Going Forward

1. **Use Information sparingly** - Only for events operators must see
2. **Default to Debug** for routine operational logging
3. **Use Trace for high-frequency details** - Individual resource processing
4. **Include context** - Always provide relevant IDs and values
5. **Performance awareness** - Consider log frequency under normal operations
6. **Conditional logging** - Use `EnableDetailedLogging` config for verbose trace logs

## Testing

After applying changes, verify:
1. Information logs show only critical events
2. Debug logs capture sufficient detail for troubleshooting  
3. No loss of operational visibility
4. Acceptable performance impact