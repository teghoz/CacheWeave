using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheWeave.DynamoDB;

/// <summary>
/// CacheWeave provider backed by AWS DynamoDB.
/// Requires a DynamoDB table with TTL enabled on the configured TtlAttribute.
/// </summary>
public sealed class DynamoDbCacheProvider : ICacheProviderInner
{
    private readonly IAmazonDynamoDB _client;
    private readonly DynamoDbCacheOptions _options;

    public DynamoDbCacheProvider(IAmazonDynamoDB client, IOptions<DynamoDbCacheOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = _options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [_options.KeyAttribute] = new AttributeValue { S = key }
            }
        }, cancellationToken);

        if (!response.IsItemSet || !response.Item.TryGetValue(_options.ValueAttribute, out var attr))
            return null;

        // Respect TTL — DynamoDB TTL deletion is eventual (up to 48h lag)
        if (response.Item.TryGetValue(_options.TtlAttribute, out var ttlAttr))
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(ttlAttr.N));
            if (expiresAt <= DateTimeOffset.UtcNow)
                return null;
        }

        return attr.S;
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

        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = _options.TableName,
            Item = item
        }, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [_options.KeyAttribute] = new AttributeValue { S = key }
            }
        }, cancellationToken);
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // DynamoDB does not support prefix scans on partition keys without a full table scan.
        // Use a GSI with a prefix-friendly sort key design, or maintain a separate key index.
        throw new NotSupportedException(
            "DynamoDB does not support prefix-based key scanning efficiently. " +
            "Design your key schema with a GSI or maintain a separate index for prefix invalidation.");
    }
}
