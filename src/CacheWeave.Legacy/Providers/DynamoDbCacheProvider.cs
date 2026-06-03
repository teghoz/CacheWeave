using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CacheWeave.Legacy.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheWeave.Legacy.Providers
{
    public sealed class DynamoDbCacheProvider : ICacheProviderInner
    {
        private readonly IAmazonDynamoDB _dynamoDb;
        private readonly DynamoDbCacheOptions _options;

        public DynamoDbCacheProvider(IAmazonDynamoDB dynamoDb, IOptions<DynamoDbCacheOptions> options)
        {
            _dynamoDb = dynamoDb;
            _options = options.Value;
        }

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var request = new GetItemRequest
            {
                TableName = _options.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    [_options.KeyAttribute] = new AttributeValue { S = key }
                }
            };

            var response = await _dynamoDb.GetItemAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsItemSet)
                return null;

            // Client-side TTL check — DynamoDB TTL deletion can lag up to 48h
            if (response.Item.TryGetValue(_options.TtlAttribute, out var ttlAttr) &&
                long.TryParse(ttlAttr.N, out var ttlUnix))
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > ttlUnix)
                {
                    await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
                    return null;
                }
            }

            return response.Item.TryGetValue(_options.ValueAttribute, out var valueAttr)
                ? valueAttr.S
                : null;
        }

        public async Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                [_options.KeyAttribute] = new AttributeValue { S = key },
                [_options.ValueAttribute] = new AttributeValue { S = value }
            };

            if (expiry.HasValue)
            {
                var ttl = DateTimeOffset.UtcNow.Add(expiry.Value).ToUnixTimeSeconds();
                item[_options.TtlAttribute] = new AttributeValue { N = ttl.ToString() };
            }

            await _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _options.TableName,
                Item = item
            }, cancellationToken).ConfigureAwait(false);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            _dynamoDb.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = _options.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    [_options.KeyAttribute] = new AttributeValue { S = key }
                }
            }, cancellationToken);

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "DynamoDB does not support prefix scans on partition keys. " +
                "Use Redis or SQLite if you need prefix eviction.");
    }
}
