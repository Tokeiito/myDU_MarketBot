using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketBot.Services
{
    // Service responsible for retrieving item prices based on recipe time
    public class PriceService : IPriceService
    {
        private readonly IRecipeService _recipeService;
        private readonly ILogger<PriceService> _logger;
        private readonly ConfigService _configService;
        private readonly MarketService _marketService;

        // Cache to store calculated prices, with itemId as the key
        private readonly ConcurrentDictionary<(ulong ItemId, ulong MarketId), (long Price, DateTime Timestamp)> _priceCache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30); // Cache duration of 30 minutes

        public PriceService(
            IRecipeService recipeService,
            ILogger<PriceService> logger,
            ConfigService configService,
            MarketService marketService
            )
        {
            _recipeService = recipeService;
            _logger = logger;
            _configService = configService;
            _marketService = marketService;

            _priceCache = new ConcurrentDictionary<(ulong ItemId, ulong MarketId), (long Price, DateTime Timestamp)>();

        }

        public async Task<long> GetPrice(ulong itemId, ulong marketId)
        {
            _logger.LogDebug($"Retrieving price for item {itemId}");

            // Check if the price is already in cache and is still valid
            if (_priceCache.TryGetValue((itemId, marketId), out var cachedPrice) && DateTime.UtcNow - cachedPrice.Timestamp < _cacheDuration)
            {
                _logger.LogDebug($"Returning cached price for item {itemId}: {cachedPrice.Price}");
                return cachedPrice.Price;
            }

            var type = await _recipeService.GetType(itemId);

            // Step 1: Fetch market orders for the item
            var orders = await _marketService.GetOrders(itemId, marketId);
            var buyOrders = orders.Where(order => order.Quantity > 0).ToList();
            var sellOrders = orders.Where(order => order.Quantity < 0).ToList();

            // Step 2: Filter out extreme orders
            var filteredBuyOrders = FilterOutExtremeOrders(buyOrders);
            var filteredSellOrders = FilterOutExtremeOrders(sellOrders);

            // Step 3: Calculate median price from remaining orders
            long medianBuyPrice = CalculateMedianPrice(filteredBuyOrders);
            long medianSellPrice = CalculateMedianPrice(filteredSellOrders);

            long price;

            if (type == ItemType.Resource)
            {
                var tier = await _recipeService.GetTier(itemId);
                // For resources, fallback to baseline if no valid orders
                if (medianBuyPrice == 0 || medianSellPrice == 0)
                {
                    _logger.LogWarning($"No valid buy or sell orders found for item {itemId} in market {marketId}, using baseline price.");
                    return _configService.Config.MarketOverlord.OreBaselinePrices[tier];
                }
                // Otherwise, use the median price
                return (medianBuyPrice + medianSellPrice) / 2;
            }
            else
            {
                price = medianBuyPrice > 0 && medianSellPrice > 0
                    ? (medianBuyPrice + medianSellPrice) / 2 // Average between buy and sell prices
                    : await GetRecursivePrice(itemId, marketId); ; // Fallback to baseline price if no valid orders
            }
            // Store the calculated price in the cache
            _priceCache[(itemId, marketId)] = (price, DateTime.UtcNow);

            return price;
        }

        private async Task<long> GetRecursivePrice(ulong itemId, ulong marketId)
        {
            // Fetch the recipe for the item
            var recipe = await _recipeService.GetRecipeAsync(itemId);

            if (recipe != null)
            {
                long totalIngredientCost = 0;

                // Step 1: Calculate the cost of each ingredient
                foreach (var ingredient in recipe.ingredients)
                {
                    var ingredientPrice = await GetPrice(ingredient.itemId, marketId);
                    totalIngredientCost += (long)(ingredientPrice * ingredient.quantity.quantity);
                }

                // Step 2: Calculate the number of products produced by the recipe
                var product = recipe.products.FirstOrDefault(p => p.itemId == itemId);

                if (product != null && product.quantity.quantity > 0)
                {
                    // Step 3: Divide the total cost by the number of products to get the cost per unit
                    long costPerProduct = totalIngredientCost / (long)product.quantity.quantity;
                    return costPerProduct;
                }
                else
                {
                    _logger.LogError($"Product with ItemId: {itemId} not found in recipe or quantity is zero.");
                    return 0;
                }
            }

            _logger.LogError($"Recipe not found for item {itemId}");
            return 0;
        }

        private IEnumerable<Order> FilterOutExtremeOrders(IEnumerable<Order> orders)
        {
            // Convert order prices to a list
            var prices = orders.Select(o => o.Price).ToList();

            if (prices.Count == 0) return orders; // Return empty if no prices

            // Calculate percentiles (e.g., remove top and bottom 5%)
            double lowerPercentile = GetPercentile(prices, 5);
            double upperPercentile = GetPercentile(prices, 95);

            // Filter orders within the desired price range
            return orders.Where(o => o.Price >= lowerPercentile && o.Price <= upperPercentile);
        }

        private double GetPercentile(List<long> prices, double percentile)
        {
            prices.Sort();
            int index = (int)(percentile / 100.0 * prices.Count);
            return prices[Math.Max(0, Math.Min(index, prices.Count - 1))];
        }

        private long CalculateMedianPrice(IEnumerable<Order> orders)
        {
            // Extract prices from orders and sort them
            var prices = orders.Select(o => o.Price).OrderBy(p => p).ToList();

            if (prices.Count == 0)
            {
                // No orders to calculate median from, return 0 or fallback value
                return 0;
            }

            int middleIndex = prices.Count / 2;

            // If odd number of elements, return the middle one
            if (prices.Count % 2 == 1)
            {
                return prices[middleIndex];
            }
            else
            {
                // If even, return the average of the two middle elements
                return (prices[middleIndex - 1] + prices[middleIndex]) / 2;
            }
        }
    }
}
