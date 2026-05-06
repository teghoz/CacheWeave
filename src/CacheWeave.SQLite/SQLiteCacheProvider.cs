using CacheWeave.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CacheWeave.SQLite;

/// <summary>
/// CacheWeave provider backed by SQLite.
/// Suitable for edge deployments, offline-capable scenarios, and local development.
/// </summary>
public sealed class SQLiteCacheProvider : ICacheProviderInner, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SQLiteCacheOptions _options;

    public SQLiteCacheProvider(IOptions<SQLiteCacheOptions> options)
    {
        _options = options.Value;
        _connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
        _connection.Open();
        EnsureTable();
    }

    private void EnsureTable()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {_options.TableName} (
                Key       TEXT PRIMARY KEY NOT NULL,
                Value     TEXT NOT NULL,
                ExpiresAt INTEGER
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT Value, ExpiresAt FROM {_options.TableName} WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var value = reader.GetString(0);
        if (!reader.IsDBNull(1))
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                await RemoveAsync(key, cancellationToken);
                return null;
            }
        }

        return value;
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        long? expiresAt = expiry.HasValue
            ? DateTimeOffset.UtcNow.Add(expiry.Value).ToUnixTimeSeconds()
            : null;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.TableName} (Key, Value, ExpiresAt)
            VALUES ($key, $value, $expiresAt)
            ON CONFLICT(Key) DO UPDATE SET Value = $value, ExpiresAt = $expiresAt;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$expiresAt", (object?)expiresAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE Key LIKE $prefix";
        cmd.Parameters.AddWithValue("$prefix", prefix.Replace("%", "\\%").Replace("_", "\\_") + "%");
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose() => _connection.Dispose();
}
