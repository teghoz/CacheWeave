using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using Microsoft.Extensions.Options;
using System.Data.SQLite;

namespace CacheWeave.Legacy.Providers
{
    public sealed class SQLiteCacheProvider : ICacheProviderInner, IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly SQLiteCacheOptions _options;

        public SQLiteCacheProvider(IOptions<SQLiteCacheOptions> options)
        {
            _options = options.Value;
            _connection = new SQLiteConnection($"Data Source={_options.DatabasePath};Version=3;");
            _connection.Open();
            EnsureTable();
        }

        private void EnsureTable()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {_options.TableName} (
                    Key TEXT PRIMARY KEY NOT NULL,
                    Value TEXT NOT NULL,
                    ExpiresAt INTEGER
                )";
            cmd.ExecuteNonQuery();
        }

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT Value, ExpiresAt FROM {_options.TableName} WHERE Key = $key";
            cmd.Parameters.AddWithValue("$key", key);

            using var reader = await Task.Run(() => cmd.ExecuteReader(), cancellationToken).ConfigureAwait(false);
            if (!reader.Read())
                return null;

            var value = reader.GetString(0);
            if (!reader.IsDBNull(1))
            {
                var expiresAt = reader.GetInt64(1);
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
                {
                    await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                    return null;
                }
            }

            return value;
        }

        public async Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO {_options.TableName} (Key, Value, ExpiresAt)
                VALUES ($key, $value, $expiresAt)
                ON CONFLICT(Key) DO UPDATE SET Value = $value, ExpiresAt = $expiresAt";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.Parameters.AddWithValue("$expiresAt", expiry.HasValue
                ? (object)DateTimeOffset.UtcNow.Add(expiry.Value).ToUnixTimeSeconds()
                : DBNull.Value);

            await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE Key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            using var cmd = _connection.CreateCommand();
            // Escape LIKE special characters in the prefix before appending the wildcard
            var escapedPrefix = prefix.Replace("%", "\\%").Replace("_", "\\_");
            cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE Key LIKE $prefix ESCAPE '\\'";
            cmd.Parameters.AddWithValue("$prefix", escapedPrefix + "%");
            await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _connection.Dispose();
    }
}
