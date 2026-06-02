namespace CacheWeave.Legacy.Providers
{
    public sealed class DynamoDbCacheOptions
    {
        public string TableName { get; set; } = "CacheWeaveCache";
        public string KeyAttribute { get; set; } = "CacheKey";
        public string ValueAttribute { get; set; } = "CacheValue";
        public string TtlAttribute { get; set; } = "ExpiresAt";
    }
}
