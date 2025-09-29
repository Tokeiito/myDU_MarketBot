using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;

public class CraftingQueueService : ITickable
{
    private readonly CraftingQueue _craftingQueue;
    private readonly ILogger<CraftingQueueService> _logger;
    private readonly ConfigService _configService;
    private readonly ITickService _tickService;
    private readonly IWarehouseService _inventoryService;

    public CraftingQueueService(
        ILogger<CraftingQueueService> logger,
        CraftingQueue craftingQueue,
        ConfigService configService,
        ITickService tickService,
        IWarehouseService inventoryService
        )
    {
        _craftingQueue = craftingQueue;
        _logger = logger;
        _configService = configService;
        _tickService = tickService;
        _inventoryService = inventoryService;


        _tickService.RegisterTickable(this);
    }

    public void Start() {
        _tickService.Start();
    }

    public void Tick()
    {
        try
        {
            ProcessQueueAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during Tick.");
        }
    }

    private async Task ProcessQueueAsync()
    {
        _logger.LogInformation("Processing crafting queue.");

        var jobsToRemove = new List<Guid>();

        foreach (var kvp in _craftingQueue.GetAllJobs())
        {
            var jobId = kvp.Key;
            var job = kvp.Value;

            if (DateTime.UtcNow >= job.CraftingStartTime.Add(job.CraftingDuration))
            {
                try
                {
                    await _inventoryService.AddUpdate(job.ItemId, job.Quantity, job.MarketId);
                    _logger.LogInformation($"Processed job ID: {jobId}, ItemId: {job.ItemId}, MarketId: {job.MarketId}");

                    // Mark the job for removal
                    jobsToRemove.Add(jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to process job ID: {jobId}, ItemId: {job.ItemId}, MarketId: {job.MarketId}, Quantity: {job.Quantity}");
                    // Decide whether to remove or keep the job in case of failure
                }
            }
        }

        // Remove processed jobs
        foreach (var jobId in jobsToRemove)
        {
            _craftingQueue.Remove(jobId);
        }
    }
}
