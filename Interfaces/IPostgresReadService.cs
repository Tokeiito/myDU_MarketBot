using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace MarketBot.Interfaces
{
    /// Read-only access to PostgreSQL. Only SELECT operations are allowed.
    public interface IPostgresReadService
    {
        Task<T> ExecuteScalarAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters = null, CancellationToken ct = default);
        Task<List<T>> QueryAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters, System.Func<NpgsqlDataReader, T> map, CancellationToken ct = default);
        Task<List<T>> QueryAsync<T>(string sql, System.Func<NpgsqlDataReader, T> map, CancellationToken ct = default);
    }
}