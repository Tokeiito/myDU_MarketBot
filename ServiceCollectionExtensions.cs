using MarketBot.Interfaces;
using MarketBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace MarketBot
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMarketBot(this IServiceCollection services, string configPath)
        {
            services.AddSingleton<ConnectionMultiplexer>(sp =>
            {
                var redisHost = NQutils.Config.Config.Instance.redis.host;
                var redisPosrt = NQutils.Config.Config.Instance.redis.port;

                var configurator = ConfigurationOptions.Parse($"{redisHost}:{redisPosrt}", true);
                configurator.DefaultDatabase = 5;
                return ConnectionMultiplexer.Connect(configurator);
            });

            services.AddScoped<IDatabase>(sp =>
            {
                var connectionMultiplexer = sp.GetRequiredService<ConnectionMultiplexer>();
                return connectionMultiplexer.GetDatabase();
            });

            services.Configure<ConfigOptions>(options =>
            {
                options.ConfigPath = configPath;
            });
            services.AddSingleton<BotConnectionManager>();
            services.AddSingleton<ModMarketBot>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<CraftingQueue>();
            services.AddSingleton<IRecipeService, RecipeService>();
            services.AddSingleton<MarketService>();
            services.AddSingleton<CraftingQueueService>();

            services.AddSingleton<IMarketOverlord, MarketOverlord>();
            services.AddSingleton<ITickService, TickService>();
            services.AddSingleton<IInventoryService, InventoryService>();
            services.AddSingleton<IPriceService, PriceService>();
            
            // Register metrics infrastructure
            services.AddSingleton<IMetricsService, MetricsService>();
            services.AddSingleton<BackgroundMetricsCalculator>();
            
            // Explicitly register services as IMetricsProvider so they can be discovered
            services.AddSingleton<IMetricsProvider>(provider => provider.GetRequiredService<IInventoryService>() as IMetricsProvider);

            return services;
        }
    }
}
