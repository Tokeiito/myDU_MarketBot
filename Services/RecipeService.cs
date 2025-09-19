using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using Backend.Business;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NQ;

public class RecipeService : IRecipeService
{
    private readonly ILogger<RecipeService> _logger;
    private readonly IDataAccessor _dataAccessor;
    private readonly IRecipes _recipesAccessor;

    private readonly Lazy<Task<List<Resource>>> _resources;
    private readonly Lazy<Task<List<NQ.Recipe>>> _recipes;

    private readonly ConcurrentDictionary<(int, ItemType), List<ulong>> _itemsByTypeCache = new ConcurrentDictionary<(int, ItemType), List<ulong>>();

    public RecipeService(ILogger<RecipeService> logger, IDataAccessor dataAccessor, IRecipes recipes)
    {
        _logger = logger;
        _dataAccessor = dataAccessor;
        _recipesAccessor = recipes;
        
        // Initialize _resources lazy field by identifying base materials from recipe data
        // Resources are items that are used as ingredients but never produced by any recipe
        _resources = new Lazy<Task<List<Resource>>>(() => 
        {
            // Get recipes synchronously in initialization (safe since it's constructor)
            var allRecipes = _recipesAccessor.GetAllRecipes().Result;
            var resources = BuildResourcesFromRecipeData(allRecipes);
            return Task.FromResult(resources);
        });
        
        // Initialize _recipes lazy field to get all recipes from accessor
        _recipes = new Lazy<Task<List<NQ.Recipe>>>(() => 
            _recipesAccessor.GetAllRecipes()
        );
    }

    // Returns cached resources if available, otherwise loads from file
    public async Task<List<Resource>> GetResourcesAsync()
    {
        return await _resources.Value;
    }

    // Returns cached recipes if available, otherwise loads from file
    public async Task<List<NQ.Recipe>> GetRecipesAsync()
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
    public async Task<List<NQ.Recipe>> GetRecipesByTier(int tier)
    {
        var recipes = await GetRecipesAsync();
        // @TODO: Implement tier calculation logic for NQ.Recipe since it doesn't have Tier property
        return recipes; // Temporary placeholder - returns all recipes
    }

    // Get recipes where the item is the product
    public async Task<List<NQ.Recipe>> GetRecipesItemIsProduct(ulong itemId)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.products.Exists(product => product.itemId == itemId));
    }

    // Get recipes where the item is an ingredient
    public async Task<List<NQ.Recipe>> GetRecipesItemIsIngredient(ulong itemId)
    {
        var recipes = await GetRecipesAsync();
        return recipes.FindAll(recipe => recipe.ingredients.Exists(ingredient => ingredient.itemId == itemId));
    }

    // Get recipes where the item is an ingredient and recipe matches the given tier
    public async Task<List<NQ.Recipe>> GetRecipesItemIsIngredientTier(ulong itemId, int tier_of_recipe)
    {
        var recipes = await GetRecipesAsync();
        // @TODO: Implement tier calculation logic for NQ.Recipe since it doesn't have Tier property
        return recipes.FindAll(recipe => recipe.ingredients.Exists(ingredient => ingredient.itemId == itemId)); // Temporary - ignores tier filter
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
                var allRecipesForComponents = await GetRecipesAsync();
                var componentIds = new HashSet<ulong>();

                foreach (var recipe in recipesForComponents)
                {
                    // Add all ingredients - they are components by definition
                    foreach (var ingredient in recipe.ingredients)
                    {
                        componentIds.Add(ingredient.itemId);
                    }

                    // Add products that are used as ingredients elsewhere (intermediate products)
                    foreach (var product in recipe.products)
                    {
                        bool isUsedAsIngredient = IsUsedAsIngredient(product.itemId, allRecipesForComponents);
                        
                        if (isUsedAsIngredient)
                        {
                            componentIds.Add(product.itemId);
                        }
                    }
                }
                items = new List<ulong>(componentIds);
                break;

            case ItemType.Product:
                var recipesForProducts = await GetRecipesByTier(tier);
                var allRecipes = await GetRecipesAsync();
                var productIds = new HashSet<ulong>();

                foreach (var recipe in recipesForProducts)
                {
                    foreach (var product in recipe.products)
                    {
                        // Check if this product is used as ingredient anywhere in ALL recipes
                        bool isUsedAsIngredient = IsUsedAsIngredient(product.itemId, allRecipes);
                        
                        if (!isUsedAsIngredient)
                        {
                            productIds.Add(product.itemId);
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
            .SelectMany(recipe => recipe.products)
            .Select(product => product.itemId);

        var items = resourceIds
            .Concat(productIds)
            .Distinct()
            .ToList();

        return items;
    }

    public async Task<NQ.Recipe> GetRecipeAsync(ulong itemId)
    {
        var recipes = await _recipes.Value;

        return recipes.Find(recipe => recipe.products.Any(product => product.itemId == itemId));
    }

    /// <summary>
    /// Determines the type of an item based on the new classification logic:
    /// - Resource: Predefined base materials that don't appear as ingredients in any recipe
    /// - Component: Items that appear as ingredients in at least one recipe (intermediate items)
    /// - Product: Items that never appear as ingredients anywhere (final products only)
    /// </summary>
    /// <param name="id">The item ID to classify</param>
    /// <returns>The ItemType classification</returns>
    public async Task<ItemType> GetType(ulong id)
    {
        // Check if it's a resource first (resources don't have ingredients and are predefined)
        var resources = await _resources.Value;
        if (resources.Exists(resource => resource.Id == id))
        {
            return ItemType.Resource;
        }

        // Get all recipes to check ingredient/product status across the entire recipe system
        var allRecipes = await GetRecipesAsync();
        
        // Check if this item appears as an ingredient in any recipe globally
        bool isUsedAsIngredient = IsUsedAsIngredient(id, allRecipes);
        
        if (isUsedAsIngredient)
        {
            // If it's used as ingredient anywhere, it's a Component (intermediate item)
            return ItemType.Component;
        }
        else
        {
            // If it's not used as ingredient anywhere, it's a Product (final product)
            return ItemType.Product;
        }
    }

    /// <summary>
    /// Calculate the tier of any item (Resource, Component, or Product) based on comprehensive recipe analysis.
    /// 
    /// Tier Calculation Algorithm:
    /// - Resources: Based on usage frequency, industry types, and recipe complexity
    /// - Products: Based on the most complex recipe that produces the item  
    /// - Components: Based on both production complexity and usage in higher-tier recipes
    /// 
    /// Factors considered:
    /// 1. Industry tier requirements (from producer IDs)
    /// 2. Crafting time complexity
    /// 3. Ingredient count and complexity
    /// 4. Usage patterns across the recipe system
    /// </summary>
    /// <param name="itemId">The item ID to get tier for</param>
    /// <returns>The calculated tier (0-5, where 0=T0 basic, 5=T5 advanced)</returns>
    public async Task<int> GetTier(ulong itemId) 
    {
        var type = await GetType(itemId);

        var allRecipes = await GetRecipesAsync();

        switch (type)
        {
            case ItemType.Resource:
                // For resources, use the tier calculated during resource initialization
                var resource = await GetResourceAsync(itemId);
                return resource?.Tier ?? 0;

            case ItemType.Product:
                // For products, find the recipe that produces this item and calculate tier
                return CalculateProductTier(itemId, allRecipes);

            case ItemType.Component:
                // For components, calculate tier based on where they're used and produced
                return CalculateComponentTier(itemId, allRecipes);

            default:
                return 0; // Default tier
        }
    }
    
    /// <summary>
    /// Calculate tier for products based on their production recipe
    /// </summary>
    private int CalculateProductTier(ulong productId, List<NQ.Recipe> allRecipes)
    {
        var producingRecipes = allRecipes.Where(recipe => 
            recipe.products.Any(product => product.itemId == productId)).ToList();
        
        if (!producingRecipes.Any()) return 0;
        
        // Calculate tier based on the most complex recipe that produces this item
        int maxTier = 0;
        foreach (var recipe in producingRecipes)
        {
            int recipeTier = CalculateRecipeTier(recipe, allRecipes);
            maxTier = Math.Max(maxTier, recipeTier);
        }
        
        return maxTier;
    }
    
    /// <summary>
    /// Calculate tier for components based on both production and usage
    /// </summary>
    private int CalculateComponentTier(ulong componentId, List<NQ.Recipe> allRecipes)
    {
        var producingRecipes = allRecipes.Where(recipe => 
            recipe.products.Any(product => product.itemId == componentId)).ToList();
        
        var usingRecipes = allRecipes.Where(recipe => 
            recipe.ingredients.Any(ingredient => ingredient.itemId == componentId)).ToList();
        
        // Calculate tier based on production complexity
        int productionTier = 0;
        if (producingRecipes.Any())
        {
            productionTier = producingRecipes.Max(recipe => CalculateRecipeTier(recipe, allRecipes));
        }
        
        // Calculate tier based on usage in higher-tier recipes
        int usageTier = 0;
        if (usingRecipes.Any())
        {
            foreach (var recipe in usingRecipes)
            {
                int recipeTier = CalculateRecipeTier(recipe, allRecipes);
                usageTier = Math.Max(usageTier, recipeTier - 1); // Components are typically one tier lower than what they produce
            }
        }
        
        // Use the higher of the two tiers
        return Math.Max(productionTier, usageTier);
    }
    
    /// <summary>
    /// Calculate the tier of a specific recipe based on multiple factors
    /// </summary>
    private int CalculateRecipeTier(NQ.Recipe recipe, List<NQ.Recipe> allRecipes)
    {
        // Factor 1: Industry tier (from producers)
        int industryTier = 0;
        if (recipe.producers.Any())
        {
            industryTier = (int)recipe.producers.Average(ExtractTierFromIndustryId);
        }
        
        // Factor 2: Crafting time complexity
        int timeTier = CalculateTierFromCraftingTime(recipe.time);
        
        // Factor 3: Ingredient count complexity
        int ingredientTier = CalculateTierFromIngredientCount(recipe.ingredients.Count);
        
        // Factor 4: Ingredient tier complexity (recursive)
        int maxIngredientTier = 0;
        foreach (var ingredient in recipe.ingredients)
        {
            // Avoid infinite recursion by limiting depth
            int ingredientItemTier = CalculateIngredientTierSimple(ingredient.itemId, allRecipes);
            maxIngredientTier = Math.Max(maxIngredientTier, ingredientItemTier);
        }
        
        // Weighted calculation
        int calculatedTier = (int)Math.Round(
            (industryTier * 0.3) + 
            (timeTier * 0.3) + 
            (ingredientTier * 0.2) + 
            (maxIngredientTier * 0.2)
        );
        
        return Math.Max(0, Math.Min(5, calculatedTier));
    }
    
    /// <summary>
    /// Simple ingredient tier calculation to avoid infinite recursion
    /// </summary>
    private int CalculateIngredientTierSimple(ulong ingredientId, List<NQ.Recipe> allRecipes)
    {
        // Check if it's a resource (base material)
        bool isResource = !allRecipes.Any(recipe => 
            recipe.products.Any(product => product.itemId == ingredientId));
        
        if (isResource)
        {
            // For resources, use usage frequency as a simple heuristic
            int usageCount = allRecipes.Count(recipe => 
                recipe.ingredients.Any(ingredient => ingredient.itemId == ingredientId));
            return CalculateTierFromUsagePattern(usageCount);
        }
        else
        {
            // For manufactured items, find the simplest recipe that produces it
            var producingRecipes = allRecipes.Where(recipe => 
                recipe.products.Any(product => product.itemId == ingredientId)).ToList();
            
            if (producingRecipes.Any())
            {
                // Use the recipe with the shortest time as a simple heuristic
                var simplestRecipe = producingRecipes.OrderBy(r => r.time).First();
                return Math.Max(1, CalculateTierFromCraftingTime(simplestRecipe.time));
            }
        }
        
        return 0;
    }

    /// <summary>
    /// Helper method to check if an item is used as an ingredient anywhere in the recipe system
    /// </summary>
    /// <param name="itemId">The item ID to check</param>
    /// <param name="allRecipes">All recipes to search through</param>
    /// <returns>True if the item is used as ingredient anywhere, false otherwise</returns>
    private bool IsUsedAsIngredient(ulong itemId, List<NQ.Recipe> allRecipes)
    {
        return allRecipes.Any(recipe => 
            recipe.ingredients.Any(ingredient => ingredient.itemId == itemId));
    }

    /// <summary>
    /// Builds a list of resources by identifying all items that are used as ingredients but never produced by any recipe.
    /// These are considered base resources that must be obtained through mining/harvesting.
    /// </summary>
    /// <param name="allRecipes">All recipes to analyze</param>
    /// <returns>List of Resource objects representing base materials</returns>
    private List<Resource> BuildResourcesFromRecipeData(List<NQ.Recipe> allRecipes)
    {
        var resources = new List<Resource>();
        
        // Get all unique ingredient IDs
        var allIngredientIds = new HashSet<ulong>();
        foreach (var recipe in allRecipes)
        {
            foreach (var ingredient in recipe.ingredients)
            {
                allIngredientIds.Add(ingredient.itemId);
            }
        }
        
        // Get all unique product IDs
        var allProductIds = new HashSet<ulong>();
        foreach (var recipe in allRecipes)
        {
            foreach (var product in recipe.products)
            {
                allProductIds.Add(product.itemId);
            }
        }
        
        // Resources are ingredients that are never produced by any recipe
        foreach (var ingredientId in allIngredientIds)
        {
            if (!allProductIds.Contains(ingredientId))
            {
                // This item is used as ingredient but never produced - it's a base resource
                resources.Add(new Resource
                {
                    Id = ingredientId,
                    DisplayNameWithSize = $"Resource_{ingredientId}", // Placeholder name
                    Tier = CalculateResourceTier(ingredientId, allRecipes) // Calculate tier based on usage patterns
                });
            }
        }
        
        return resources;
    }

    /// <summary>
    /// Calculate the tier of a resource based on multiple factors from recipe analysis.
    /// Uses industry types, usage patterns, and ingredient complexity to determine tier.
    /// </summary>
    /// <param name="itemId">The resource item ID</param>
    /// <param name="allRecipes">All recipes to check</param>
    /// <returns>The calculated tier (0-5)</returns>
    private int CalculateResourceTier(ulong itemId, List<NQ.Recipe> allRecipes)
    {
        // Find recipes where this resource is used as an ingredient
        var recipesUsingThis = allRecipes.Where(recipe => 
            recipe.ingredients.Any(ingredient => ingredient.itemId == itemId)).ToList();
        
        if (!recipesUsingThis.Any())
            return 0; // If not used in any recipe, default to T0
        
        // Analyze industry types to determine tier
        var industryTiers = new List<int>();
        foreach (var recipe in recipesUsingThis)
        {
            // Extract tier information from industry names
            foreach (var industry in recipe.producers)
            {
                int tier = ExtractTierFromIndustryId(industry);
                if (tier >= 0) industryTiers.Add(tier);
            }
        }
        
        // Calculate tier based on multiple factors
        int tierFromIndustry = industryTiers.Any() ? (int)industryTiers.Average() : 0;
        int tierFromUsage = CalculateTierFromUsagePattern(recipesUsingThis.Count);
        int tierFromComplexity = CalculateTierFromRecipeComplexity(recipesUsingThis);
        
        // Use weighted average, favoring industry tier as it's most reliable
        int calculatedTier = (int)Math.Round(
            (tierFromIndustry * 0.5) + 
            (tierFromUsage * 0.3) + 
            (tierFromComplexity * 0.2)
        );
        
        // Clamp to valid range
        return Math.Max(0, Math.Min(5, calculatedTier));
    }
    
    /// <summary>
    /// Extract tier information from industry/producer IDs
    /// </summary>
    private int ExtractTierFromIndustryId(ulong industryId)
    {
        // This is a simplified approach - in reality you'd need to map industry IDs to tiers
        // Based on the YAML patterns, higher tier industries have higher IDs or specific patterns
        
        // For now, use a heuristic based on industry ID ranges
        // This would need to be refined with actual industry data
        if (industryId <= 1000) return 0;      // T0 industries (basic)
        if (industryId <= 5000) return 1;      // T1 industries  
        if (industryId <= 10000) return 2;     // T2 industries
        if (industryId <= 20000) return 3;     // T3 industries
        if (industryId <= 50000) return 4;     // T4 industries
        return 5;                              // T5 industries (highest)
    }
    
    /// <summary>
    /// Calculate tier based on usage frequency patterns
    /// </summary>
    private int CalculateTierFromUsagePattern(int usageCount)
    {
        // Common resources (used in many recipes) are lower tier
        // Rare resources (used in few recipes) are higher tier
        if (usageCount >= 50) return 0;  // Very common - T0
        if (usageCount >= 30) return 1;  // Common - T1  
        if (usageCount >= 20) return 2;  // Moderate - T2
        if (usageCount >= 10) return 3;  // Less common - T3
        if (usageCount >= 5) return 4;   // Rare - T4
        return 5;                        // Very rare - T5
    }
    
    /// <summary>
    /// Calculate tier based on recipe complexity (crafting time, ingredient count)
    /// </summary>
    private int CalculateTierFromRecipeComplexity(List<NQ.Recipe> recipesUsingResource)
    {
        if (!recipesUsingResource.Any()) return 0;
        
        // Calculate average recipe complexity metrics
        double avgCraftingTime = recipesUsingResource.Average(r => r.time);
        double avgIngredientCount = recipesUsingResource.Average(r => r.ingredients.Count);
        
        // Higher crafting times and more ingredients suggest higher tier
        int tierFromTime = CalculateTierFromCraftingTime(avgCraftingTime);
        int tierFromIngredients = CalculateTierFromIngredientCount(avgIngredientCount);
        
        return (int)Math.Round((tierFromTime + tierFromIngredients) / 2.0);
    }
    
    /// <summary>
    /// Calculate tier based on average crafting time
    /// </summary>
    private int CalculateTierFromCraftingTime(double avgTime)
    {
        if (avgTime < 60) return 0;        // Under 1 minute - T0
        if (avgTime < 300) return 1;       // Under 5 minutes - T1
        if (avgTime < 1800) return 2;      // Under 30 minutes - T2  
        if (avgTime < 7200) return 3;      // Under 2 hours - T3
        if (avgTime < 28800) return 4;     // Under 8 hours - T4
        return 5;                          // Over 8 hours - T5
    }
    
    /// <summary>
    /// Calculate tier based on average ingredient count
    /// </summary>
    private int CalculateTierFromIngredientCount(double avgCount)
    {
        if (avgCount < 2) return 0;        // 1-2 ingredients - T0
        if (avgCount < 3) return 1;        // 2-3 ingredients - T1
        if (avgCount < 4) return 2;        // 3-4 ingredients - T2
        if (avgCount < 5) return 3;        // 4-5 ingredients - T3
        if (avgCount < 6) return 4;        // 5-6 ingredients - T4
        return 5;                          // 6+ ingredients - T5
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