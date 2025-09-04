using System;
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
        private readonly IInventoryService _inventoryService;
        private readonly MarketService _marketService;
        private readonly ILogger<MarketOverlord> _logger;
        private readonly ConfigService _configService;
        private readonly CraftingQueue _craftingQueue;
        private readonly IPriceService _priceService;
        private readonly BotConnectionManager _botConnectionManager;

        public MarketOverlord(
            ITickService tickService,
            IRecipeService recipeService,
            ILogger<MarketOverlord> logger,
            ConfigService configService,
            IInventoryService inventoryService,
            MarketService marketService,
            CraftingQueue craftingQueue,
            IPriceService priceService,
            IDataAccessor dataAccessor,
            BotConnectionManager botConnectionManager,
            StatisticsScheduler statisticsScheduler
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
                    _botConnectionManager
                    );
                _tickService.RegisterTickable(marketManager);
            }

            _tickService.RegisterTickable(statisticsScheduler);
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
