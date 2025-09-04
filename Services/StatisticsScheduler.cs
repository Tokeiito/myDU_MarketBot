using MarketBot.Interfaces;
using System;

public class StatisticsScheduler : ITickable
{
    private readonly InventoryStatisticsService _inventoryStatisticsService;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);
    private DateTime _nextRunTime;

    public StatisticsScheduler(InventoryStatisticsService inventoryStatisticsService)
    {
        _inventoryStatisticsService = inventoryStatisticsService;
        _nextRunTime = DateTime.UtcNow + _interval;
    }

    public void Tick()
    {
        if (DateTime.UtcNow >= _nextRunTime)
        {
            _inventoryStatisticsService.CollectAndSaveStatistics().GetAwaiter().GetResult();
            _nextRunTime = DateTime.UtcNow + _interval; // Schedule the next run
        }
    }
}
