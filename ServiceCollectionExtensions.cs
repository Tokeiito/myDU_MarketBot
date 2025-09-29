using MarketBot.Interfaces;
using MarketBot.Services;
using MarketBot.Services.WorldModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Npgsql;

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
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<CraftingQueue>();
            services.AddSingleton<IRecipeService, RecipeService>();
            services.AddSingleton<MarketService>();
            services.AddSingleton<CraftingQueueService>();

            // PostgreSQL read-only service
            services.AddSingleton<IPostgresReadService, PostgresReadService>();

            services.AddSingleton<IMarketOverlord, MarketOverlord>();
            services.AddSingleton<ITickService, TickService>();
            
            // Warehouse services
            services.AddSingleton<IWarehouseLotStorage, WarehouseLotStorage>();
            services.AddSingleton<IWarehouseEventService, WarehouseEventService>();
            services.AddSingleton<IWarehouseService, WarehouseService>();
            
            services.AddSingleton<IPriceService, PriceService>();
            
            // Register metrics infrastructure
            services.AddSingleton<IMetricsService, MetricsService>();
            services.AddSingleton<BackgroundMetricsCalculator>();
            
            // Register World Model services - Let DI handle constructor injection
            services.AddSingleton<IPlanetaryResourceService, PlanetaryResourceService>();
            services.AddSingleton<ILAIStateService, LAIStateService>();
            services.AddSingleton<LAIEngine>();
            services.AddSingleton<WorldModelMetricsService>();
            services.AddSingleton<IWarehouseCapacityService, WarehouseCapacityService>();
            services.AddSingleton<ResourceGenerationService>();
            services.AddSingleton<IWorldModelService, WorldModelService>();
            
            // Explicitly register services as IMetricsProvider so they can be discovered
            services.AddSingleton<IMetricsProvider>(provider => provider.GetRequiredService<IWarehouseService>() as IMetricsProvider);
            services.AddSingleton<IMetricsProvider>(provider => provider.GetRequiredService<WorldModelMetricsService>());

            // Resource definitions (TTL = restart, in-memory)
            services.AddSingleton<IResourceDefinitionService, ResourceDefinitionService>();
            
            // Planet service (TTL = restart, in-memory)
            services.AddSingleton<IPlanetService, PlanetService>();

            return services;
        }
    }
}
