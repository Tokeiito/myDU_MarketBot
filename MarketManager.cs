using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Business;
using BotLib.Generated;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using NQ;

namespace MarketBot
{
    public class MarketManager : ITickable
    {
        private readonly ulong _planet;
        private readonly IRecipeService _recipeService;
        private readonly IDataAccessor _dataAccessor;
        private readonly IInventoryService _inventoryService;
        private readonly MarketService _marketService;
        private readonly ILogger<MarketOverlord> _logger;
        private readonly ConfigService _configService;
        private readonly MarketList _marketList;
        private readonly CraftingQueue _craftingQueue;
        private readonly IPriceService _priceService;
        private readonly BotConnectionManager _botConnectionManager;

        private int _currentMarketIndex = 0;
        private int _currentItemIndex = 0;
        private int _itemsPerTick = 5;
        private int _currentTier = 0;

        public MarketManager(
            ulong planet,
            ILogger<MarketOverlord> logger,
            ConfigService configService,
            IRecipeService recipeService,
            IDataAccessor dataAccessor,
            IInventoryService inventoryService,
            MarketService marketService,
            CraftingQueue craftingQueue,
            IPriceService priceService,
            BotConnectionManager botConnectionManager
            )
        {
            _planet = planet;
            _logger = logger;
            _configService = configService;
            _recipeService = recipeService;
            _dataAccessor = dataAccessor;
            _inventoryService = inventoryService;
            _marketService = marketService;
            _craftingQueue = craftingQueue;
            _priceService = priceService;
            _botConnectionManager = botConnectionManager;

            _marketList = _dataAccessor.FetchMarketListAsync(_planet).Result;
        }

        public async void Tick()
        {
            _logger.LogInformation($"Processing market for planet {_planet}, Tier {_currentTier}, Market {_marketList.markets[_currentMarketIndex].marketId}");

            var market = _marketList.markets[_currentMarketIndex];
            var itemsInTier = await _recipeService.GetItemIdsByTier(_currentTier);

            //await ProcessExpiredOrders(market.marketId, itemsInTier);

            await ProcessMarketInventory(market.marketId);

            // Process the current batch of items from the market
            await ProcessMarketForTier(market.marketId, itemsInTier);

            await ProcessBotInventory();

            // Move to the next set of items
            _currentItemIndex += _itemsPerTick;

            // If all items in the market are processed, move to the next market or tier
            if (_currentItemIndex >= itemsInTier.Count())
            {
                _currentItemIndex = 0; // Reset the item index
                _currentMarketIndex++; // Move to the next market

                // If all markets are processed, reset market index and move to the next tier
                if (_currentMarketIndex >= _marketList.markets.Count)
                {
                    _currentMarketIndex = 0;
                    _currentTier = (_currentTier + 1) % 6; // Cycle through T0 to T5
                }
            }
        }

        private async Task ProcessBotInventory()
        {
            var items = await RetryHelper.RetryOnExceptionAsync(
                          async () =>
                          {
                              var slots = await Mod.bot.Req.InventoryGet();
                              return slots.content.Select(slot => new Item{
                                Id = slot.content.Ref.Type,
                                ItemType = Mod.bot.GameplayBank.GetDefinition(slot.content.Ref.Type).GetStaticPropertyOpt("inventoryType").stringValue,
                                Quantity = slot.quantity.quantity
                              });
                          },
                              _botConnectionManager.IsDisconnectedException,
                              _botConnectionManager.ReconnectBotAsync
                          );

            foreach (var item in items)
            {
                try
                {
                    await _marketService.CreateItem(item.Id, item.Quantity * -1);
                    await _inventoryService.AddUpdate(item.Id, item.Quantity, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unable to move item ({item.Id}, {item.Quantity}, {item.ItemType}) from bot inventory to virtual inventory.");
                }
            }
        }

        private async Task ProcessMarketForTier(ulong marketId, List<ulong> itemsInTier)
        {
            var itemsToProcess = itemsInTier
            .Skip(_currentItemIndex)
            .Take(_itemsPerTick)
            .ToList();

            // Separate buy and sell orders based on quantity
            var activeOrders = await _marketService.GetItemsForTierAsync(_currentTier, marketId);
            var buyOrders = activeOrders.Where(o => o.Quantity > 0).ToList();
            var sellOrders = activeOrders.Where(o => o.Quantity < 0).ToList();

            foreach (var itemId in itemsToProcess)
            {
                // Orders for the current item (could be empty)
                var buyOrdersForItem = buyOrders.Where(o => o.ItemId == itemId).ToList();
                var sellOrdersForItem = sellOrders.Where(o => o.ItemId == itemId).ToList();

                // Calculate quantities, treating missing orders as zero
                var totalBuyQuantityForItem = buyOrdersForItem.Sum(o => o.Quantity);
                var totalSellQuantityForItem = sellOrdersForItem.Sum(o => Math.Abs(o.Quantity));

                // Make the decision to buy, sell, or craft
                await MakeBuyOrSellDecision(
                    marketId,
                    itemId,
                    buyOrdersForItem.Count,         // If no buy orders, this will be 0
                    sellOrdersForItem.Count,        // If no sell orders, this will be 0
                    totalBuyQuantityForItem,        // If no buy orders, this will be 0
                    totalSellQuantityForItem);      // If no sell orders, this will be 0
            }

            // Move to the next batch of items
            _currentItemIndex += _itemsPerTick;

            if (_currentItemIndex >= itemsInTier.Count)
            {
                _currentItemIndex = 0;  // Reset item index
                _currentMarketIndex++;  // Move to the next market

                // If all markets are processed, reset market index and cycle to the next tier
                if (_currentMarketIndex >= _marketList.markets.Count)
                {
                    _currentMarketIndex = 0;
                    _currentTier = (_currentTier + 1) % 6;  // Cycle through T0 to T5
                }
            }
        }

        private async Task MakeBuyOrSellDecision(
            ulong marketId,
            ulong itemId,
            int buyOrderCount,
            int sellOrderCount,
            long totalBuyQuantity,
            long totalSellQuantity
            )
        {
            long inventoryQuantity = await _inventoryService.Get(itemId, marketId);

            ItemType itemType = await _recipeService.GetType(itemId);

            var tierSettings = _configService.Config.MarketOverlord.TierSettings[_currentTier];
            var itemSettings = tierSettings[itemType.ToString()];

            // Thresholds
            int numberOfSellOrders = itemSettings.NumberOfSellOrders;
            long quantityInSellOrders = itemSettings.QuantityInSellOrders;

            int numberOfBuyOrders = itemSettings.NumberOfBuyOrders;
            long quantityInBuyOrders = itemSettings.QuantityInBuyOrders;

            int inventoryThreshold = itemSettings.QuantityInInventory;

            if (sellOrderCount < numberOfSellOrders && inventoryQuantity > inventoryThreshold)
            {
                await ExecuteSellOrder(marketId, itemId, inventoryQuantity - inventoryThreshold);
            }

            if (buyOrderCount < numberOfBuyOrders)
            {
                // Calculate how many items should be in each buy order
                long itemsPerBuyOrder = quantityInBuyOrders / numberOfBuyOrders;

                // Calculate how many items are missing across all buy orders
                long totalMissingBuyQuantity = itemsPerBuyOrder * numberOfBuyOrders - totalBuyQuantity;

                // Place a new buy order for the missing quantity, or for the quantity needed per buy order
                long quantityToBuy = Math.Min(itemsPerBuyOrder, totalMissingBuyQuantity);

                if (quantityToBuy > 0)
                {
                    await ExecuteBuyOrder(marketId, itemId, quantityToBuy);
                }
            }

            // 3. Check for Crafting Opportunity Based on Market Conditions
            if (buyOrderCount > 0)  // Only consider crafting if there are buy orders
            {
                // Fetch the cost of crafting the item
                double craftingCost = await _priceService.GetPrice(itemId, marketId);

                // Check for existing sell orders and the minimum sell price
                double minSellPrice = double.MaxValue;
                if (sellOrderCount > 0)
                {
                    var sellOrdersForItem = await _marketService.GetSellOrders(marketId, itemId);
                    minSellPrice = sellOrdersForItem.Min(o => o.Price);
                }

                // Decision to craft:
                // 1. No sell orders exist
                // 2. Crafting is cheaper than the minimum sell price
                if (sellOrderCount == 0 || craftingCost < minSellPrice)
                {
                    // Calculate how many items should be in each sell order
                    long quantityPerSellOrder = itemSettings.QuantityInSellOrders / numberOfSellOrders;

                    // Calculate the required crafting quantity based on sell orders and inventory
                    long requiredCraftingQuantity = Math.Max(quantityPerSellOrder, inventoryThreshold) - inventoryQuantity;

                    if (requiredCraftingQuantity > 0)
                    {
                        await ExecuteCraftOrder(marketId, itemId, requiredCraftingQuantity);
                    }
                }
            }
        }

        private async Task ExecuteCraftOrder(ulong marketId, ulong itemId, long requiredCraftingQuantity)
        {
            var itemType = await _recipeService.GetType(itemId);

            if (itemType != ItemType.Resource)
            {
                var recipe = await _recipeService.GetRecipeAsync(itemId);

                if (recipe != null)
                {
                    var craftingJob = new CraftingJob();
                    craftingJob.MarketId = marketId;
                    craftingJob.ItemId = itemId;
                    craftingJob.CraftingDuration = TimeSpan.FromSeconds(recipe.time);
                    craftingJob.Quantity = requiredCraftingQuantity;
                    craftingJob.CraftingStartTime = DateTime.UtcNow;

                    _craftingQueue.Add(craftingJob);
                }
                else
                {
                    _logger.LogWarning($"Item {itemId} does not have recipe.");
                }
            }
        }

        private async Task ExecuteSellOrder(ulong marketId, ulong itemId, long maxSellQuantity)
        {

            var price = await _priceService.GetPrice(itemId, marketId) * 1.2;
            long quantityToSell = maxSellQuantity;

            try
            {
                await _marketService.CreateItem(itemId, quantityToSell);

                await _marketService.PlaceMarketOrder(marketId, itemId, quantityToSell, price, true, false);

                await _inventoryService.AddUpdate(itemId, -quantityToSell, marketId);

                _logger.LogInformation($"Created sell order for {quantityToSell} of item {itemId} in market {marketId}.");
            }
            catch (Exception ex)
            {
                try
                {
                    _logger.LogError(ex, $"Sell order failed for item {itemId} in market {marketId}, quantity {quantityToSell}. Rolling back item creation.");

                    // Rollback the item creation: remove the created item from the inventory
                    await _marketService.CreateItem(itemId, -quantityToSell);

                    _logger.LogInformation($"Rolled back item creation for {itemId} by removing {quantityToSell} from inventory.");
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, $"Failed to roll back item creation for {itemId} in market {marketId}.");
                }
            }

        }

        private async Task ExecuteBuyOrder(ulong marketId, ulong itemId, long quantity)
        {
            var price = await _priceService.GetPrice(itemId, marketId) * 0.8;
            if (price > 0)
            {
                var money = await Mod.bot.ImplementationClient.GetWallet();

                if (money < price * quantity * 100)
                {
                    _logger.LogWarning($"Not enough money to create buy order for item {itemId} in market {marketId}.");
                    return;
                }

                try
                {
                    await _marketService.PlaceMarketOrder(marketId, itemId, quantity, price);

                    _logger.LogInformation($"Created buy order for {quantity} of item {itemId} in market {marketId}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to execute buy order");
                }
            }
        }

        private async Task ProcessMarketInventory(ulong marketId)
        {
            var marketInventory = await _marketService.GetMarketInventoryAsync(marketId);

            foreach (var item in marketInventory.Where(item => item.Quantity > 0))
            {
                var invItemQ = await _inventoryService.Get(item.Id, marketId);
                try
                {
                    // Move to inventory
                    await _marketService.MoveItemFromMarketToInventory(marketId, item.Id, item.Quantity);

                    // Remove item from inventory
                    await _marketService.CreateItem(item.Id, invItemQ * -1);

                    await _inventoryService.AddUpdate(item.Id, item.Quantity, marketId);

                    _logger.LogInformation($"Moved {item.Quantity} of item {item.Id} from market {marketId} to inventory.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to process market inventory");
                }
            }
        }

        private async Task ProcessExpiredOrders(ulong marketId, List<ulong> itemsInTier)
        {
            var orders = await _marketService.GetSellOrders(marketId, itemsInTier);

            foreach (var order in orders)
            {
                if (order.Expiration <= TimePoint.Now())
                {
                    try
                    {
                        await _marketService.CancelOrder(order.MarketId, order.OrderId, order.ItemId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unable to cancel order");
                    }
                }
            }
        }
    }
}
