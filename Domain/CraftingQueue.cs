using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

public class CraftingQueue
{
    private readonly ConcurrentDictionary<Guid, CraftingJob> _craftingJobs;
    private readonly ILogger<CraftingQueue> _logger;

    public CraftingQueue(ILogger<CraftingQueue> logger)
    {
        _logger = logger;
        _craftingJobs = new ConcurrentDictionary<Guid, CraftingJob>();
    }

    /// <summary>
    /// Adds a crafting job to the queue.
    /// </summary>
    public Guid Add(CraftingJob job)
    {
        // Generate a unique identifier for the job
        var jobId = Guid.NewGuid();

        // Check for duplicates
        if (_craftingJobs.Values.Any(existingJob =>
            existingJob.MarketId == job.MarketId && existingJob.ItemId == job.ItemId))
        {
            _logger.LogDebug($"Duplicate job detected for ItemId: {job.ItemId}, MarketId: {job.MarketId}. Skipping addition.");
            return Guid.Empty; // Indicate that the job wasn't added
        }

        // Add the job to the dictionary
        if (_craftingJobs.TryAdd(jobId, job))
        {
            _logger.LogDebug($"Job added with ID: {jobId}, ItemId: {job.ItemId}, Quantity: {job.Quantity}, Market: {job.MarketId}, Start: {job.CraftingStartTime}, End: {job.CraftingStartTime.Add(job.CraftingDuration)}");
            return jobId;
        }
        else
        {
            _logger.LogError($"Failed to add job for ItemId: {job.ItemId}, MarketId: {job.MarketId}.");
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Retrieves all crafting jobs.
    /// </summary>
    public IEnumerable<KeyValuePair<Guid, CraftingJob>> GetAllJobs()
    {
        return _craftingJobs;
    }

    /// <summary>
    /// Removes a crafting job by its identifier.
    /// </summary>
    public bool Remove(Guid jobId)
    {
        return _craftingJobs.TryRemove(jobId, out _);
    }

    /// <summary>
    /// Checks if an item is already queued.
    /// </summary>
    public bool ItemQueued(ulong itemId)
    {
        return _craftingJobs.Values.Any(job => job.ItemId == itemId);
    }

    /// <summary>
    /// Returns the count of jobs currently in the queue.
    /// </summary>
    public int Count => _craftingJobs.Count;
}