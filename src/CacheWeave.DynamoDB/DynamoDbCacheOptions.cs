namespace CacheWeave.DynamoDB;

public sealed class DynamoDbCacheOptions
{
    /// <summary>DynamoDB table name used for cache storage.</summary>
    public string TableName { get; set; } = "CacheWeaveCache";

    /// <summary>Partition key attribute name.</summary>
    public string KeyAttribute { get; set; } = "CacheKey";

    /// <summary>Value attribute name.</summary>
    public string ValueAttribute { get; set; } = "CacheValue";

    /// <summary>TTL attribute name. Must match the TTL attribute configured on the DynamoDB table.</summary>
    public string TtlAttribute { get; set; } = "ExpiresAt";
}
