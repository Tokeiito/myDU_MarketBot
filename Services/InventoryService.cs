using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public class InventoryService : IInventoryService
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<IInventoryService> _logger;

    public InventoryService(IDatabase redisDatabase, ILogger<IInventoryService> logger)
    {
        _redisDatabase = redisDatabase;
        _logger = logger;
    }

    // Function to get all items; marketId is optional.
    public async Task<Dictionary<ulong, string>> GetAll(ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        var hashEntries = await _redisDatabase.HashGetAllAsync(key);

        return hashEntries.ToDictionary(
            entry => (ulong)entry.Name, // Converting RedisValue to ulong directly
            entry => entry.Value.ToString() // Converting RedisValue to string for the value
        );
    }

    // Function to get item quantity; marketId is optional.
    public async Task<long> Get(ulong itemId, ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        var value = await _redisDatabase.HashGetAsync(key, itemId.ToString());

        if (value.HasValue && value.TryParse(out long quantity))
        {
            return quantity;
        }
        return 0;
    }

    // Function to add/update an item; marketId is optional.
    public async Task AddUpdate(ulong itemId, long value, ulong? marketId = null)
    {
        var marketKey = marketId.HasValue ? $"inventory:{marketId}:items" : null;
        var globalKey = "inventory:global:items";

        // Check if the item already exists in the market or globally
        long currentMarketQuantity = marketId.HasValue ? await Get(itemId, marketId) : 0;
        long currentGlobalQuantity = await Get(itemId, null); // Get global inventory

        // Case 1: Increase quantity (either new item or update existing)
        if (value > 0)
        {
            if (marketId.HasValue)
            {
                await _redisDatabase.HashSetAsync(marketKey, itemId.ToString(), currentMarketQuantity + value);
            }
            else
            {
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), currentGlobalQuantity + value);
            }
        }
        // Case 2: Reduce quantity (split between global and market if necessary)
        else if (value < 0)
        {
            long quantityToReduce = Math.Abs(value);

            // Step 1: Reduce from global inventory first
            long remainingReduction = quantityToReduce;

            if (currentGlobalQuantity >= remainingReduction)
            {
                // If global inventory can cover the reduction, just reduce it
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), currentGlobalQuantity - remainingReduction);
            }
            else
            {
                // If global inventory cannot fully cover the reduction, reduce it to zero and reduce the market
                await _redisDatabase.HashSetAsync(globalKey, itemId.ToString(), 0);
                remainingReduction -= currentGlobalQuantity;

                // Step 2: Reduce remaining quantity from market inventory (if applicable)
                if (marketId.HasValue && currentMarketQuantity > 0)
                {
                    long marketReduction = Math.Min(currentMarketQuantity, remainingReduction);
                    await _redisDatabase.HashSetAsync(marketKey, itemId.ToString(), currentMarketQuantity - marketReduction);
                    remainingReduction -= marketReduction;
                }

                // Step 3: If any reduction remains, inventory deficit exists (could handle as an error or log)
                if (remainingReduction > 0)
                {
                    _logger.LogWarning($"Insufficient inventory for item {itemId}. Unable to reduce by {quantityToReduce} units.");
                }
            }
        }
    }

    // Function to delete an item; marketId is optional.
    public async Task Delete(ulong itemId, ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        await _redisDatabase.HashDeleteAsync(key, itemId.ToString());
    }

    // Function to clean the inventory; marketId is optional.
    public async Task CleanInventory(ulong? marketId = null)
    {
        var key = marketId.HasValue ? $"inventory:{marketId}:items" : "inventory:global:items";
        await _redisDatabase.KeyDeleteAsync(key);
    }
}
