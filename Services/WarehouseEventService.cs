using System;
using System.Threading.Tasks;
using MarketBot.Domain;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MarketBot.Services
{
    /// <summary>
    /// Basic implementation of warehouse event service for telemetry and notifications
    /// </summary>
    public class WarehouseEventService : IWarehouseEventService
    {
        private readonly ILogger<WarehouseEventService> _logger;
        private readonly IMetricsService _metricsService;
        private readonly ConfigService _configService;
        
        /// <summary>
        /// Check if dry run mode is enabled.
        /// </summary>
        private bool IsDryRunMode => _configService.Config.Development.DryRun;

        public WarehouseEventService(ILogger<WarehouseEventService> logger, IMetricsService metricsService, ConfigService configService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Emit event when items are added to warehouse
        /// </summary>
        public async Task EmitItemsAddedAsync(ulong itemId, long quantity, long unitCost, ulong? marketId, DateTime? timestamp = null)
        {
            try
            {
                var warehouseEvent = new ItemsAddedEvent
                {
                    ItemId = itemId,
                    Quantity = quantity,
                    UnitCost = unitCost,
                    MarketId = marketId,
                    Timestamp = timestamp ?? DateTime.UtcNow
                };

                await EmitEventAsync(warehouseEvent);
                
                // Tag metrics for dry run mode
                var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
                _metricsService.Increment("warehouse_events_items_added_total", metricTags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to emit items_added event for item {ItemId}", itemId);
            }
        }

        /// <summary>
        /// Emit event when items are consumed from warehouse
        /// </summary>
        public async Task EmitItemsConsumedAsync(ulong itemId, long consumedQuantity, long weightedAverageCost, ulong? marketId, DateTime? timestamp = null)
        {
            try
            {
                var warehouseEvent = new ItemsConsumedEvent
                {
                    ItemId = itemId,
                    ConsumedQuantity = consumedQuantity,
                    WeightedAverageCost = weightedAverageCost,
                    MarketId = marketId,
                    Timestamp = timestamp ?? DateTime.UtcNow
                };

                await EmitEventAsync(warehouseEvent);
                
                // Tag metrics for dry run mode
                var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
                _metricsService.Increment("warehouse_events_items_consumed_total", metricTags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to emit items_consumed event for item {ItemId}", itemId);
            }
        }

        /// <summary>
        /// Emit event when lots are merged during consolidation
        /// </summary>
        public async Task EmitLotMergedAsync(string oldLotId1, string oldLotId2, string newLotId, long newUnitCost, ulong itemId, ulong? marketId, DateTime? timestamp = null)
        {
            try
            {
                var warehouseEvent = new LotMergedEvent
                {
                    OldLotId1 = oldLotId1,
                    OldLotId2 = oldLotId2,
                    NewLotId = newLotId,
                    NewUnitCost = newUnitCost,
                    ItemId = itemId,
                    MarketId = marketId,
                    Timestamp = timestamp ?? DateTime.UtcNow
                };

                await EmitEventAsync(warehouseEvent);
                
                // Tag metrics for dry run mode
                var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
                _metricsService.Increment("warehouse_events_lot_merged_total", metricTags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to emit lot_merged event for lots {OldLotId1}/{OldLotId2}", oldLotId1, oldLotId2);
            }
        }

        /// <summary>
        /// Emit event when a new lot is created
        /// </summary>
        public async Task EmitLotCreatedAsync(string lotId, ulong itemId, long unitCost, ulong? marketId, DateTime? timestamp = null)
        {
            try
            {
                var warehouseEvent = new LotCreatedEvent
                {
                    LotId = lotId,
                    ItemId = itemId,
                    UnitCost = unitCost,
                    MarketId = marketId,
                    Timestamp = timestamp ?? DateTime.UtcNow
                };

                await EmitEventAsync(warehouseEvent);
                
                // Tag metrics for dry run mode
                var metricTags = IsDryRunMode ? new[] { "mode:dry_run" } : new[] { "mode:production" };
                _metricsService.Increment("warehouse_events_lot_created_total", metricTags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to emit lot_created event for lot {LotId}", lotId);
            }
        }

        /// <summary>
        /// Core event emission method - logs event and could be extended to publish to message bus
        /// </summary>
        private async Task EmitEventAsync(WarehouseEvent warehouseEvent)
        {
            await Task.CompletedTask; // For async consistency
            
            // Add dry run prefix to logs when in dry run mode
            var logPrefix = IsDryRunMode ? "[DRY-RUN] " : "";

            // Log structured event data
            _logger.LogInformation("{LogPrefix}Warehouse Event: {EventType} - Item {ItemId} in Market {MarketId} at {Timestamp}",
                logPrefix,
                warehouseEvent.EventType, 
                warehouseEvent.ItemId, 
                warehouseEvent.MarketId?.ToString() ?? "Global",
                warehouseEvent.Timestamp);

            // Log detailed event data for debugging (trace level)
            var eventJson = JsonConvert.SerializeObject(warehouseEvent, Formatting.None);
            _logger.LogTrace("{LogPrefix}Warehouse Event Details: {EventJson}", logPrefix, eventJson);

            // TODO: In the future, this could publish to a message bus/event stream
            // In dry run mode, we might want to suppress external publications
            // if (!IsDryRunMode)
            // {
            //     await _messageBus.PublishAsync(warehouseEvent);
            // }
        }
    }
}