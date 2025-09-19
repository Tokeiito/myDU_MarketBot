using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarketBot.Domain;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using YamlDotNet.RepresentationModel;

namespace MarketBot.Services
{
    // Read-only, in-memory cached (TTL = process lifetime) resource definition loader for ores and plasmas.
    public class ResourceDefinitionService : IResourceDefinitionService
    {
        private readonly IPostgresReadService _db;
        private readonly ILogger<ResourceDefinitionService> _logger;

        private volatile Task _loadTask;
        private IReadOnlyList<ResourceDefinition> _ores = Array.Empty<ResourceDefinition>();
        private IReadOnlyList<ResourceDefinition> _plasmas = Array.Empty<ResourceDefinition>();

        private const string OreMaterialName = "OreMaterial";
        private const string PlasmaName = "Plasma";

        public ResourceDefinitionService(IPostgresReadService db, ILogger<ResourceDefinitionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ResourceDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            await EnsureLoadedAsync(ct);
            var all = new List<ResourceDefinition>(_ores.Count + _plasmas.Count);
            all.AddRange(_ores);
            all.AddRange(_plasmas);
            return all;
        }

        public async Task<IReadOnlyList<ResourceDefinition>> GetOresAsync(CancellationToken ct = default)
        {
            await EnsureLoadedAsync(ct);
            return _ores;
        }

        public async Task<IReadOnlyList<ResourceDefinition>> GetPlasmasAsync(CancellationToken ct = default)
        {
            await EnsureLoadedAsync(ct);
            return _plasmas;
        }

        private Task EnsureLoadedAsync(CancellationToken ct)
        {
            var task = _loadTask;
            if (task != null) return task;

            lock (this)
            {
                if (_loadTask == null)
                {
                    _loadTask = LoadAsync(ct);
                }
                return _loadTask;
            }
        }

        private async Task LoadAsync(CancellationToken ct)
        {
            // Resolve parents
            var oreMaterialId = await GetParentIdByNameAsync(OreMaterialName, ct) ?? throw new InvalidOperationException($"Parent '{OreMaterialName}' not found");
            var plasmaParentId = await GetParentIdByNameAsync(PlasmaName, ct) ?? throw new InvalidOperationException($"Parent '{PlasmaName}' not found");

            // Get Ore1..Ore5 group ids
            var oreGroupIds = await _db.QueryAsync(
                "SELECT id FROM public.item_definition WHERE parent_id = @pid AND name LIKE 'Ore%' ORDER BY name;",
                new[] { new NpgsqlParameter("@pid", (long)oreMaterialId) },
                r => (ulong)r.GetInt64(0), ct);

            if (oreGroupIds.Count == 0)
            {
                _logger.LogWarning("No Ore[1..5] groups found under OreMaterial");
            }

            // Fetch ores under those groups
            var loadedOres = new List<ResourceDefinition>();
            if (oreGroupIds.Count > 0)
            {
                var inParams = string.Join(",", oreGroupIds.Select((_, i) => $"@p{i}"));
                var sql = $"SELECT id, name, yaml FROM public.item_definition WHERE parent_id IN ({inParams});";
                var parameters = oreGroupIds.Select((id, i) => new NpgsqlParameter($"@p{i}", (long)id)).ToArray();
                var rows = await _db.QueryAsync(sql, parameters, r => new
                {
                    Id = (ulong)r.GetInt64(0),
                    Name = r.GetString(1),
                    Yaml = r.GetString(2)
                }, ct);

                foreach (var row in rows)
                {
                    var (displayName, level) = ParseDisplayNameAndLevel(row.Yaml);
                    if (string.IsNullOrEmpty(displayName))
                    {
                        _logger.LogWarning("Missing displayName in yaml for item {ItemName} ({ItemId})", row.Name, row.Id);
                        continue;
                    }
                    loadedOres.Add(new ResourceDefinition
                    {
                        Id = row.Id,
                        Name = displayName,
                        StrId = row.Name,
                        Tier = level ?? 0,
                        Kind = "Ore"
                    });
                }
            }

            // Fetch plasmas
            var plasmaRows = await _db.QueryAsync(
                "SELECT id, name, yaml FROM public.item_definition WHERE parent_id = @pid ORDER BY name;",
                new[] { new NpgsqlParameter("@pid", (long)plasmaParentId) },
                r => new
                {
                    Id = (ulong)r.GetInt64(0),
                    Name = r.GetString(1),
                    Yaml = r.GetString(2)
                }, ct);

            var loadedPlasmas = new List<ResourceDefinition>();
            foreach (var row in plasmaRows)
            {
                var (displayName, _) = ParseDisplayNameAndLevel(row.Yaml);
                if (string.IsNullOrEmpty(displayName))
                {
                    _logger.LogWarning("Missing displayName in yaml for plasma {ItemName} ({ItemId})", row.Name, row.Id);
                    continue;
                }
                loadedPlasmas.Add(new ResourceDefinition
                {
                    Id = row.Id,
                    Name = displayName,
                    StrId = row.Name,
                    Tier = 5,
                    Kind = "Plasma"
                });
            }

            _ores = loadedOres.OrderBy(o => o.Tier).ThenBy(o => o.Name).ToList();
            _plasmas = loadedPlasmas.OrderBy(p => p.Name).ToList();

            _logger.LogInformation("Loaded {OreCount} ores and {PlasmaCount} plasmas into in-memory cache", _ores.Count, _plasmas.Count);
        }

        private async Task<ulong?> GetParentIdByNameAsync(string name, CancellationToken ct)
        {
            var result = await _db.QueryAsync(
                "SELECT id FROM public.item_definition WHERE name = @name LIMIT 1;",
                new[] { new NpgsqlParameter("@name", name) },
                r => (ulong)r.GetInt64(0), ct);
            return result.FirstOrDefault();
        }

        private static (string displayName, int? level) ParseDisplayNameAndLevel(string yamlText)
        {
            try
            {
                using var reader = new StringReader(yamlText);
                var yaml = new YamlStream();
                yaml.Load(reader);
                if (yaml.Documents.Count == 0) return (null, null);
                if (yaml.Documents[0].RootNode is not YamlMappingNode root || root.Children.Count == 0)
                    return (null, null);
                var first = root.Children.First();
                if (first.Value is not YamlMappingNode mapping) return (null, null);

                string displayName = null;
                int? level = null;
                foreach (var kv in mapping.Children)
                {
                    var key = (kv.Key as YamlScalarNode)?.Value;
                    if (string.Equals(key, "displayName", StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = (kv.Value as YamlScalarNode)?.Value;
                    }
                    else if (string.Equals(key, "level", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse((kv.Value as YamlScalarNode)?.Value, out var l)) level = l;
                    }
                }
                return (displayName, level);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}