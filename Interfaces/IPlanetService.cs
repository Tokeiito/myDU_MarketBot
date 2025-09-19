using System.Threading;
using System.Threading.Tasks;
using MarketBot.Domain;

namespace MarketBot.Interfaces
{
    /// <summary>
    /// Service for retrieving planet information with in-memory caching.
    /// Provides cached access to planet data with process-lifetime TTL.
    /// </summary>
    public interface IPlanetService
    {
        /// <summary>
        /// Retrieves a planet by its unique identifier.
        /// Results are cached in memory until application restart.
        /// </summary>
        /// <param name="planetId">The unique planet identifier (construct ID)</param>
        /// <param name="ct">Cancellation token for async operation</param>
        /// <returns>Planet object if found, null if not found or not a valid planet</returns>
        Task<Planet?> GetPlanetByIdAsync(ulong planetId, CancellationToken ct = default);
    }
}