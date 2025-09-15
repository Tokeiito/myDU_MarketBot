using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using BotLib.Generated;
using BotLib.Utils;
using Microsoft.Extensions.Logging;
using NQ;
using Orleans;

public class MarketService
{
    private readonly IClusterClient _orleans;
    private readonly IGameplayBank _gameplayBank;
    private readonly IRecipeService _recipeService;
    private readonly ConfigService _configService;
    private readonly ILogger<MarketService> _logger;
    private readonly BotConnectionManager _botConnectionManager;

    public MarketService(
        ILogger<MarketService> logger,
        ConfigService configService,
        IClusterClient clusterClient,
        IGameplayBank gameplayBank,
        IRecipeService recipeService,
        BotConnectionManager botConnectionManager
        )
    {
        _orleans = clusterClient;
        _gameplayBank = gameplayBank;
        _configService = configService;
        _recipeService = recipeService;
        _logger = logger;
        _botConnectionManager = botConnectionManager;
    }

    public async Task CreateItem(ulong itemTypeId, long quantity)
    {
        // Check if dry-run mode is enabled
        if (_configService.Config.Development.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] CreateItem: Would create {Quantity} items of type {ItemTypeId}", quantity, itemTypeId);
            return;
        }

        var item = _gameplayBank.GetDefinition(itemTypeId);

        var itemAndQuantity = new ItemAndQuantity
        {
            item = item.AsItemInfo(),
            quantity = quantity,
        };

        var list = new ItemAndQuantityList();
        list.content.Add(itemAndQuantity);

        await RetryHelper.RetryOnExceptionAsync(
            async () =>
            {
                await Mod.bot.Req.BotGiveItems(list);
                _logger.LogDebug("Item successfully created in the bot's inventory.");
            },
            _botConnectionManager.IsDisconnectedException,
            _botConnectionManager.ReconnectBotAsync
            );
    }

    public async Task PlaceMarketOrder(ulong marketId, ulong itemTypeId, long quantity, double unitPrice, bool sell = false, bool fromMarketContainer = false)
    {
        quantity = sell ? quantity * -1 : quantity;

        // Check if dry-run mode is enabled
        if (_configService.Config.Development.DryRun)
        {
            var orderType = sell ? "sell" : "buy";
            var source = fromMarketContainer ? "market container" : "inventory";
            _logger.LogInformation("[DRY-RUN] PlaceMarketOrder: Would place {OrderType} order for {Quantity} items of type {ItemTypeId} in market {MarketId} at price {UnitPrice} from {Source}", 
                orderType, Math.Abs(quantity), itemTypeId, marketId, unitPrice, source);
            return;
        }

        _logger.LogInformation($"Placing market order for item {itemTypeId} in market {marketId}. Quantity: {quantity}, Price per unit: {unitPrice}.");

        MarketRequest marketRequest = new MarketRequest
        {
            marketId = marketId,
            itemType = itemTypeId,
            buyQuantity = quantity, // Use a positive value for buy order, negative for sell
            expirationDate = DateTime.Now.AddDays(3).ToNQTimePoint(),
            unitPrice = (long)(unitPrice * 100), // Convert price to the appropriate format
        };

        if (fromMarketContainer)
        {
            marketRequest.source = MarketRequestSource.FROM_MARKET_CONTAINER;
        }
        else {
            marketRequest.source = MarketRequestSource.FROM_INVENTORY;
        }

        await RetryHelper.RetryOnExceptionAsync(
        async () =>
        {
            await Mod.bot.Req.MarketPlaceOrder(marketRequest);
            _logger.LogDebug($"Successfully placed market order for {quantity} of item {itemTypeId} in market {marketId} at price {unitPrice}.");
        },
            _botConnectionManager.IsDisconnectedException,
            _botConnectionManager.ReconnectBotAsync
        );
    }

    public async Task<IEnumerable<Order>> GetBuyOrdersForItem(ulong marketId, ulong itemTypeId)
    {
        return await RetryHelper.RetryOnExceptionAsync(
            async () =>
            {
                var orders = await Mod.bot.Req.MarketSelectItem(new MarketSelectRequest
                {
                    marketIds = new List<ulong> { marketId },
                    itemTypes = new List<ulong> { itemTypeId }
                });

                var buyOrders = orders.orders
                    .Where(order => order.buyQuantity > 0)
                    .Select(order => new Order
                    {
                        OrderId = order.orderId,
                        ItemId = order.itemType,
                        Quantity = order.buyQuantity,
                        Price = order.unitPrice.amount,
                        MarketId = order.marketId
                    });

                return buyOrders;
            },
            _botConnectionManager.IsDisconnectedException,
            _botConnectionManager.ReconnectBotAsync
        );

    }

    public async Task<IEnumerable<Order>> GetOrders(ulong itemId, ulong marketId)
    {
        return await RetryHelper.RetryOnExceptionAsync(
           async () =>
           {
               var orders = await Mod.bot.Req.MarketSelectItem(new MarketSelectRequest
               {
                   marketIds = new List<ulong> { marketId },
                   itemTypes = new List<ulong> { itemId }
               });

               var _orders = orders.orders
                   .Select(order => new Order
                   {
                       OrderId = order.orderId,
                       ItemId = order.itemType,
                       Quantity = order.buyQuantity,
                       Price = order.unitPrice.amount,
                       MarketId = order.marketId
                   });

               return _orders;
           },
           _botConnectionManager.IsDisconnectedException,
           _botConnectionManager.ReconnectBotAsync
       );
    }

    public async Task<IEnumerable<Order>> GetSellOrders(ulong marketId, ulong itemId)
    {
        return await RetryHelper.RetryOnExceptionAsync(
          async () =>
          {
              var orders = await Mod.bot.Req.MarketSelectItem(new MarketSelectRequest
              {
                  marketIds = new List<ulong> { marketId },
                  itemTypes = new List<ulong> { itemId }
              });

              var _orders = orders.orders
                  .Where(order => order.buyQuantity < 0)
                  .Select(order => new Order
                  {
                      OrderId = order.orderId,
                      ItemId = order.itemType,
                      Quantity = Math.Abs(order.buyQuantity),
                      Price = order.unitPrice.amount,
                      MarketId = order.marketId
                  });

              return _orders;
          },
          _botConnectionManager.IsDisconnectedException,
          _botConnectionManager.ReconnectBotAsync
      );
    }

    public async Task<IEnumerable<Order>> GetSellOrders(ulong marketId, List<ulong> itemIds)
    {
        return await RetryHelper.RetryOnExceptionAsync(
          async () =>
          {
              var orders = await Mod.bot.Req.MarketSelectItem(new MarketSelectRequest
              {
                  marketIds = new List<ulong> { marketId },
                  itemTypes = itemIds
              });

              var _orders = orders.orders
                  .Where(order => order.buyQuantity < 0)
                  .Select(order => new Order
                  {
                      OrderId = order.orderId,
                      ItemId = order.itemType,
                      Quantity = Math.Abs(order.buyQuantity),
                      Price = order.unitPrice.amount,
                      MarketId = order.marketId,
                      Expiration = order.expirationDate
                  });

              return _orders;
          },
          _botConnectionManager.IsDisconnectedException,
          _botConnectionManager.ReconnectBotAsync
      );
    }

    public async Task<IEnumerable<Order>> GetItemsForTierAsync(int currentTier, ulong marketId) {

        List<ulong> items = await _recipeService.GetItemIdsByTier(currentTier);

        return await RetryHelper.RetryOnExceptionAsync(
           async () =>
           {
               var orders = await Mod.bot.Req.MarketSelectItem(new MarketSelectRequest
               {
                   marketIds = new List<ulong> { marketId },
                   itemTypes = items
               });

               var _orders = orders.orders
                   .Select(order => new Order
                   {
                       OrderId = order.orderId,
                       ItemId = order.itemType,
                       Quantity = order.buyQuantity,
                       Price = order.unitPrice.amount,
                       MarketId = order.marketId
                   });

               return _orders;
           },
           _botConnectionManager.IsDisconnectedException,
           _botConnectionManager.ReconnectBotAsync
       );
    }

    internal async Task<IEnumerable<Item>> GetMarketInventoryAsync(ulong marketId)
    {
        return await RetryHelper.RetryOnExceptionAsync(
                async () => {
                    var items = await Mod.bot.Req.MarketContainerGetMyContent(new MarketSelectRequest
                        {
                            marketIds = new List<ulong> { marketId },
                            ownerId =  Mod.bot.AsEntityId(),
                        }
                    );

                    var _orders = items.slots.Where(item => item.purchased == true).Select(item => new Item{
                        Id = item.itemAndQuantity.item.type,
                        ItemType = Mod.bot.GameplayBank.GetDefinition(item.itemAndQuantity.item.type).GetStaticPropertyOpt("inventoryType").stringValue,
                        Quantity = item.itemAndQuantity.quantity.value
                    });

                    return _orders;
                },
                _botConnectionManager.IsDisconnectedException,
                _botConnectionManager.ReconnectBotAsync
            );
    }

    internal async Task MoveItemFromMarketToInventory(ulong marketId, ulong itemId, long quantity) {

        // Check if dry-run mode is enabled
        if (_configService.Config.Development.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] MoveItemFromMarketToInventory: Would move {Quantity} items of type {ItemId} from market {MarketId} to inventory", 
                quantity, itemId, marketId);
            return;
        }

        await RetryHelper.RetryOnExceptionAsync(
                async () => {
                    await Mod.bot.Req.MarketStorageMove(
                                new MarketStorageMoveInfo
                                {
                                    marketId = marketId,
                                    itemType = itemId,
                                    quantity = quantity,
                                    itemOwner = Mod.bot.AsEntityId(),
                                }
                                );
                },
                _botConnectionManager.IsDisconnectedException,
                _botConnectionManager.ReconnectBotAsync
            );
    }

    internal async Task CancelOrder(ulong marketId, ulong orderId, ulong itemId)
    {
        // Check if dry-run mode is enabled
        if (_configService.Config.Development.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] CancelOrder: Would cancel order {OrderId} for item {ItemId} in market {MarketId}", 
                orderId, itemId, marketId);
            return;
        }

        await RetryHelper.RetryOnExceptionAsync(
               async () =>
               {
                   await Mod.bot.Req.MarketCancelOrder(new MarketOrder
                   {
                       marketId = marketId,
                       orderId = orderId,
                       itemType = itemId
                   });
               },
               _botConnectionManager.IsDisconnectedException,
               _botConnectionManager.ReconnectBotAsync
               );
    }
}
