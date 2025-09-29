using System;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using MarketBot.Services;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services
{
    /// <summary>
    /// Service for managing local resource capacity limits and generation throttling.
    /// Follows warehouseService patterns for unit handling and Redis storage.
    /// </summary>
    public class WarehouseCapacityService : IWarehouseCapacityService
    {
        private readonly IWarehouseService _warehouseService;
        private readonly ConfigService _configService;
        private readonly ILogger<WarehouseCapacityService> _logger;

        public WarehouseCapacityService(
            IWarehouseService warehouseService,
            ConfigService configService,
            ILogger<WarehouseCapacityService> logger)
        {
            _warehouseService = warehouseService ?? throw new ArgumentNullException(nameof(warehouseService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Checks if resource generation is allowed based on current local capacity.
        /// Uses display units throughout for consistency with ResourceGenerationService.
        /// </summary>
        public async Task<bool> CanGenerateResource(ulong resourceId, long displayQuantity, ulong marketId)
        {
            try
            {
                var capacity = GetLocalResourceCapacityLimit();
                
                // Handle special capacity values
                if (capacity == -1)
                {
                    _logger.LogTrace("Local resource capacity is unlimited for resource {ResourceId} in market {MarketId}", 
                        resourceId, marketId);
                    return true; // Unlimited
                }
                
                if (capacity == 0)
                {
                    _logger.LogTrace("Local resource generation disabled for resource {ResourceId} in market {MarketId}", 
                        resourceId, marketId);
                    return false; // No generation allowed
                }
                
                // Check current local quantity in display units
                var currentDisplayQuantity = await _warehouseService.GetLocalResourceDisplay(resourceId, marketId);
                
                // Check if adding the new quantity would exceed capacity
                var projectedQuantity = currentDisplayQuantity + displayQuantity;
                var canGenerate = projectedQuantity <= capacity;
                
                _logger.LogTrace("Capacity check for resource {ResourceId} in market {MarketId}: current={Current}, " +
                                "adding={Adding}, projected={Projected}, capacity={Capacity}, allowed={Allowed}",
                    resourceId, marketId, currentDisplayQuantity, displayQuantity, projectedQuantity, capacity, canGenerate);
                
                return canGenerate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking capacity for resource {ResourceId} in market {MarketId}", 
                    resourceId, marketId);
                
                // Fail-safe: allow generation if capacity check fails
                return true;
            }
        }

        /// <summary>
        /// Gets current local resource utilization as percentage of capacity.
        /// Returns -1.0 for unlimited capacity.
        /// </summary>
        public async Task<double> GetLocalResourceUtilization(ulong resourceId, ulong marketId)
        {
            try
            {
                var capacity = GetLocalResourceCapacityLimit();
                
                if (capacity == -1)
                {
                    return -1.0; // Unlimited capacity
                }
                
                if (capacity == 0)
                {
                    return 1.0; // No generation allowed = 100% utilization
                }
                
                var currentDisplayQuantity = await _warehouseService.GetLocalResourceDisplay(resourceId, marketId);
                var utilization = (double)currentDisplayQuantity / capacity;
                
                // Clamp to valid range (can exceed 1.0 if imports pushed us over capacity)
                return Math.Max(0.0, utilization);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating utilization for resource {ResourceId} in market {MarketId}", 
                    resourceId, marketId);
                return 0.0;
            }
        }

        /// <summary>
        /// Gets the current local resource capacity limit from configuration.
        /// </summary>
        public long GetLocalResourceCapacityLimit()
        {
            return _configService.Config.WorldModel.GenerationSettings.LocalResourceCapacity;
        }

        /// <summary>
        /// Checks if local resource capacity limits are enabled.
        /// </summary>
        public bool IsCapacityLimitEnabled()
        {
            var capacity = GetLocalResourceCapacityLimit();
            return capacity != -1; // Enabled unless set to unlimited (-1)
        }
    }
}