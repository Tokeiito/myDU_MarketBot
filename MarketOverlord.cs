using System;
using System.Threading.Tasks;
using Backend;
using Backend.Business;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketBot
{
    public class MarketOverlord : IMarketOverlord
    {
        private readonly ITickService _tickService;
        private readonly IRecipeService _recipeService;
        private readonly IDataAccessor _dataAccessor;
        private readonly IWarehouseService _inventoryService;
        private readonly MarketService _marketService;
        private readonly ILogger<MarketOverlord> _logger;
        private readonly ConfigService _configService;
        private readonly CraftingQueue _craftingQueue;
        private readonly IPriceService _priceService;
        private readonly BotConnectionManager _botConnectionManager;
        private readonly IWorldModelService _worldModelService;
        private readonly IGameplayBank _gameplayBank;

        public MarketOverlord(
            ITickService tickService,
            IRecipeService recipeService,
            ILogger<MarketOverlord> logger,
            ConfigService configService,
            IWarehouseService inventoryService,
            MarketService marketService,
            CraftingQueue craftingQueue,
            IPriceService priceService,
            IDataAccessor dataAccessor,
            BotConnectionManager botConnectionManager,
            IWorldModelService worldModelService,
            IGameplayBank gameplayBank
            )
        {
            _tickService = tickService;
            _recipeService = recipeService;
            _logger = logger;
            _configService = configService;
            _recipeService = recipeService;
            _inventoryService = inventoryService;
            _marketService = marketService;
            _craftingQueue = craftingQueue;
            _priceService = priceService;
            _dataAccessor = dataAccessor;
            _botConnectionManager = botConnectionManager;
            _worldModelService = worldModelService;
            _gameplayBank = gameplayBank;

            // Register World Model Service with tick system
            _tickService.RegisterTickable(_worldModelService);
            
            // Check for inventory cleanup configuration and execute if requested
            if (_configService.Config.Development.CleanInventoryData)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogCritical("🚨 INVENTORY CLEANUP REQUESTED - THIS WILL DELETE ALL EXISTING INVENTORY DATA! 🚨");
                        _logger.LogCritical("CleanInventoryData is set to TRUE in configuration");
                        _logger.LogCritical("Creating backup before cleanup...");
                        
                        await _inventoryService.SafeCleanInventoryWithBackup();
                        
                        _logger.LogCritical("✅ INVENTORY CLEANUP COMPLETED - All old inventory data has been cleaned and backed up");
                        _logger.LogCritical("⚠️  IMPORTANT: Set CleanInventoryData=false in config.json to prevent this from running again");
                        _logger.LogCritical("📦 Backup created with 7-day retention for emergency restore");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "❌ CRITICAL FAILURE: Inventory cleanup failed - System may be in inconsistent state!");
                        throw; // Re-throw to potentially halt startup
                    }
                });
            }
            
            // Initialize World Model Service
            _ = Task.Run(async () =>
            {
                try
                {
                    await _worldModelService.InitializeAsync();
                    _logger.LogInformation("World Model Service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize World Model Service");
                }
            });

            foreach (var planetId in _configService.Config.MarketOverlord.OperationPlanets)
            {
                var marketManager = new MarketManager(
                    planetId,
                    _logger,
                    _configService,
                    _recipeService,
                    _dataAccessor,
                    _inventoryService,
                    _marketService,
                    _craftingQueue,
                    _priceService,
                    _botConnectionManager,
                    _gameplayBank
                    );
                _tickService.RegisterTickable(marketManager);
            }

            // StatisticsScheduler removed - metrics now handled by InventoryService + BackgroundMetricsCalculator
        }

        public void Start()
        {
            _tickService.Start();
        }

        public void Stop()
        {
            _tickService.Stop();
        }
    }
}
