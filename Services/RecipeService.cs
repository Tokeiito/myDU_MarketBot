using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class RecipeService : IRecipeService
{
    private readonly ILogger<RecipeService> _logger;
    private const string ResourcesFilePath = "Data/resources.json";
    private const string RecipesFilePath = "Data/recipes.json";

    private readonly Lazy<Task<List<Resource>>> _resources;
    private readonly Lazy<Task<List<Recipe>>> _recipes;

    private readonly ConcurrentDictionary<(int, ItemType), List<ulong>> _itemsByTypeCache = new ConcurrentDictionary<(int, ItemType), List<ulong>>();

    public RecipeService(ILogger<RecipeService> logger)
    {
        _logger = logger;
        _resources = new Lazy<Task<List<Resource>>>(() => LoadDataAsync<Resource>(ResourcesFilePath));
        _recipes = new Lazy<Task<List<Recipe>>>(() => LoadDataAsync<Recipe>(RecipesFilePath));
    }

    // Returns cached resources if available, otherwise loads from file
    public async Task<List<Resource>> GetResourcesAsync()
    {
        return await _resources.Value;
    }

    // Returns cached recipes if available, otherwise loads from file
    public async Task<List<Recipe>> GetRecipesAsync()
    {
        return await _recipes.Value;
    }

    // Filter resources by tier
    public async Task<List<Resource>> GetResourcesByTier(int tier)
    {
        var resources = await GetResourcesAsync();
        return resources.FindAll(resource => resource.Tier == tier);
    }

    // Filter recipes by tier
    public async Task<List<Recipe>> GetRecipesByTier(int tier)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.Tier == tier);
    }

    // Get recipes where the item is the product
    public async Task<List<Recipe>> GetRecipesItemIsProduct(ulong itemId)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.Products.Exists(product => product.Id == itemId));
    }

    // Get recipes where the item is an ingredient
    public async Task<List<Recipe>> GetRecipesItemIsIngredient(ulong itemId)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.Ingredients.Exists(ingredient => ingredient.Id == itemId));
    }

    // Get recipes where the item is an ingredient and recipe matches the given tier
    public async Task<List<Recipe>> GetRecipesItemIsIngredientTier(ulong itemId, int tier_of_recipe)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.Tier == tier_of_recipe && recipe.Ingredients.Exists(ingredient => ingredient.Id == itemId));
    }

    public async Task<Resource> GetResourceAsync(ulong itemTypeId)
    {
        var resources = await GetResourcesAsync();
        return resources.Find(resource => resource.Id == itemTypeId);
    }

    public async Task<List<ulong>> GetItemsByType(int tier, ItemType resourceType)
    {
        var cacheKey = (tier, resourceType);

        // Check if the result is cached
        if (_itemsByTypeCache.TryGetValue(cacheKey, out var cachedItems))
        {
            return cachedItems;
        }

        List<ulong> items;

        switch (resourceType)
        {
            case ItemType.Resource:
                var resources = await GetResourcesByTier(tier);
                items = resources.ConvertAll(resource => resource.Id);
                break;

            case ItemType.Component:
                var recipesForComponents = await GetRecipesByTier(tier);
                var componentIds = new HashSet<ulong>();

                foreach (var recipe in recipesForComponents)
                {
                    foreach (var ingredient in recipe.Ingredients)
                    {
                        componentIds.Add(ingredient.Id);
                    }

                    foreach (var product in recipe.Products)
                    {
                        componentIds.Add(product.Id);
                    }
                }
                items = new List<ulong>(componentIds);
                break;

            case ItemType.Product:
                var recipesForProducts = await GetRecipesByTier(tier);
                var productIds = new HashSet<ulong>();

                foreach (var recipe in recipesForProducts)
                {
                    foreach (var product in recipe.Products)
                    {
                        bool isIngredient = recipe.Ingredients.Exists(ingredient => ingredient.Id == product.Id);
                        if (!isIngredient)
                        {
                            productIds.Add(product.Id);
                        }
                    }
                }
                items = new List<ulong>(productIds);
                break;

            default:
                _logger.LogError("Invalid resource type {ResourceType}", resourceType);
                return new List<ulong>();
        }

        // Cache the result for future calls
        _itemsByTypeCache[cacheKey] = items;

        return items;
    }

    public async Task<List<ulong>> GetItemIdsByTier(int tier)
    {
        var resourcesTask = GetResourcesByTier(tier);
        var recipesTask = GetRecipesByTier(tier);

        var resources = await resourcesTask;
        var recipes = await recipesTask;

        var resourceIds = resources.Select(resource => resource.Id);

        var productIds = recipes
            .SelectMany(recipe => recipe.Products)
            .Select(product => product.Id);

        var items = resourceIds
            .Concat(productIds)
            .Distinct()
            .ToList();

        return items;
    }

    public async Task<Recipe> GetRecipeAsync(ulong itemId)
    {
        var recipes = await _recipes.Value;

        return recipes.Find(recipe => recipe.Products.Any(product => (ulong)product.Id == itemId));
    }

    public async Task<ItemType> GetType(ulong id)
    {
        var resources = await _resources.Value;

        if (resources.Exists(resource => resource.Id == id))
        {
            return ItemType.Resource;
        }
        else
        {
            var recipes = await GetRecipesItemIsIngredient(id);
            if (recipes.Count == 0)
            {
                return ItemType.Product;
            }
        }

        return ItemType.Component;
    }

    public async Task<int> GetTier(ulong itemId) {
        var type = await GetType(itemId);

        if (type == ItemType.Resource) {
            var resource = await GetResourceAsync(itemId);
            return resource.Tier;
        }

        var recipe = await GetRecipeAsync(itemId);
        return recipe.Tier;
        
    }

    // Private method to load data from JSON files
    private async Task<List<T>> LoadDataAsync<T>(string filePath)
    {
        try
        {
            var jsonData = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<List<T>>(jsonData);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error reading file {FilePath}", filePath);
            return new List<T>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON from file {FilePath}", filePath);
            return new List<T>();
        }
    }

    
}