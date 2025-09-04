using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;

public class InventoryStatisticsService
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<InventoryStatisticsService> _logger;

    public InventoryStatisticsService(IInventoryService inventoryService, ILogger<InventoryStatisticsService> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    // This method will aggregate inventory data and save it to a CSV file every 30 minutes
    public async Task CollectAndSaveStatistics()
    {
        var timestamp = DateTime.UtcNow;

        // Initialize collections to track statistics
        var marketStatistics = new Dictionary<ulong, Dictionary<ulong, long>>();
        var globalStatistics = new Dictionary<ulong, long>();

        // Collect global inventory statistics
        var globalInventory = await _inventoryService.GetAll(null); // Assume this gets all global items
        foreach (var item in globalInventory)
        {
            globalStatistics[item.Key] = long.Parse(item.Value);
        }

        // Collect per-market statistics
        for (ulong marketId = 1; marketId <= 5; marketId++) // Adjust range based on number of markets
        {
            var marketInventory = await _inventoryService.GetAll(marketId);
            marketStatistics[marketId] = new Dictionary<ulong, long>();

            foreach (var item in marketInventory)
            {
                marketStatistics[marketId][item.Key] = long.Parse(item.Value);
            }
        }

        // After collecting, write the data to a CSV file
        await WriteStatisticsToCsv(timestamp, globalStatistics, marketStatistics);
    }

    private async Task WriteStatisticsToCsv(DateTime timestamp, Dictionary<ulong, long> globalStats, Dictionary<ulong, Dictionary<ulong, long>> marketStats)
    {
        var fileName = $"/logs/InventoryStatistics_{timestamp:yyyyMMdd_HHmm}.csv";
        using (var writer = new StreamWriter(fileName))
        {
            // Write header
            await writer.WriteLineAsync("Timestamp,MarketId,ItemId,Quantity");

            // Write global statistics
            foreach (var kvp in globalStats)
            {
                await writer.WriteLineAsync($"{timestamp:O},Global,{kvp.Key},{kvp.Value}");
            }

            // Write market statistics
            foreach (var market in marketStats)
            {
                foreach (var item in market.Value)
                {
                    await writer.WriteLineAsync($"{timestamp:O},{market.Key},{item.Key},{item.Value}");
                }
            }
        }

        _logger.LogInformation($"Inventory statistics saved to {fileName}");
    }
}
