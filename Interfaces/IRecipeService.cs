using System.Collections.Generic;
using System.Threading.Tasks;
using NQ;

public interface IRecipeService
{
    // Retrieves all resources available in the system
    Task<List<Resource>> GetResourcesAsync();

    // Retrieves all recipes available in the system
    Task<List<NQ.Recipe>> GetRecipesAsync();

    // Retrieves resources filtered by the specific tier
    Task<List<Resource>> GetResourcesByTier(int tier);

    // Retrieves recipes filtered by the specific tier
    Task<List<NQ.Recipe>> GetRecipesByTier(int tier);

    // Retrieves recipes where the given item is a product
    Task<List<NQ.Recipe>> GetRecipesItemIsProduct(ulong itemId);

    // Retrieves recipes where the given item is an ingredient
    Task<List<NQ.Recipe>> GetRecipesItemIsIngredient(ulong itemId);

    // Retrieves recipes where the given item is an ingredient and matches the specific tier
    Task<List<NQ.Recipe>> GetRecipesItemIsIngredientTier(ulong itemId, int tier_of_recipe);
    Task<Resource> GetResourceAsync(ulong itemTypeId);
    Task<List<ulong>> GetItemsByType(int tier, ItemType resource);
    Task<NQ.Recipe> GetRecipeAsync(ulong itemId);
    Task<ItemType> GetType(ulong id);
    Task<List<ulong>> GetItemIdsByTier(int currentTier);
    Task<int> GetTier(ulong itemId);
}
