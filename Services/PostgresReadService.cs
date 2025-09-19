using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using NQutils.Config;

namespace MarketBot.Services
{
    /// Minimal read-only PostgreSQL service using raw Npgsql.
    public class PostgresReadService : MarketBot.Interfaces.IPostgresReadService
    {
        private readonly ILogger<PostgresReadService> _logger;
        private readonly string _connectionString;

        public PostgresReadService(ILogger<PostgresReadService> logger)
        {
            _logger = logger;
            // Build connection string from dual.yaml -> postgres section via NQutils.Config
            var pg = Config.Instance.postgres;
            // Enforce read-only transactions by default and identify app name
            _connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = pg.host,
                Port = pg.port,
                Database = pg.database,
                Username = pg.user,
                Password = pg.password,
                ApplicationName = "marketbot",
                // Pooling enabled by default in Npgsql
                // Enforce read-only transactions for safety (server/user should also be RO)
                Options = "-c default_transaction_read_only=on"
            }.ToString();
        }

        private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        public async Task<T> ExecuteScalarAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters = null, CancellationToken ct = default)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null)
            {
                foreach (var p in parameters)
                    cmd.Parameters.Add(p);
            }
            var result = await cmd.ExecuteScalarAsync(ct);
            if (result == null || result is DBNull) return default;
            return (T)Convert.ChangeType(result, typeof(T));
        }

        public async Task<List<T>> QueryAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters, Func<NpgsqlDataReader, T> map, CancellationToken ct = default)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null)
            {
                foreach (var p in parameters)
                    cmd.Parameters.Add(p);
            }
            var list = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(map(reader));
            }
            return list;
        }

        public Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct = default)
            => QueryAsync(sql, null, map, ct);

        // Called during startup to fail fast when DB is not reachable
        public async Task ValidateConnectivityAsync(CancellationToken ct = default)
        {
            var now = await ExecuteScalarAsync<DateTime>("SELECT now();", null, ct);
            _logger.LogInformation("PostgreSQL connectivity OK. Server time: {Now}", now);
        }
    }
}