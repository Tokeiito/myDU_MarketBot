using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarketBot.Domain;

namespace MarketBot.Interfaces
{
    public interface IResourceDefinitionService
    {
        Task<IReadOnlyList<ResourceDefinition>> GetOresAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ResourceDefinition>> GetPlasmasAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ResourceDefinition>> GetAllAsync(CancellationToken ct = default);
    }
}