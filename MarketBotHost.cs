using System;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.DependencyInjection;

public class ModMarketBot : Mod
{
    public ModMarketBot()
    {
    }

    // Main loop for the bot, responsible for initializing and scheduling tasks
    public override async Task Loop()
    {
        StartServices();

        // Schedule the action to run every 1 minute
        await SafeLoop(Action, 60000, async () =>
        {
            Console.WriteLine("Reconnecting bot...");
            bot = await CreateUser("trader", true, false);
        });
    }

    private void StartServices()
    {
        var craftingQueueService = serviceProvider.GetRequiredService<CraftingQueueService>();
        craftingQueueService.Start();

        var marketOverlord = serviceProvider.GetRequiredService<IMarketOverlord>();
        marketOverlord.Start();

        Console.WriteLine("Services have been started successfully.");
    }

    // Main action logic (for example, mining or other bot tasks)
    public async Task Action()
    {
        await Task.Delay(10000); // Simulate some work
    }
}