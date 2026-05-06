using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.DynamoDB.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a DynamoDB backing store.
    /// AWS credentials are resolved via the default credential chain (env vars, IAM role, ~/.aws/credentials).
    /// </summary>
    public static IServiceCollection AddCacheWeaveDynamoDB(
        this IServiceCollection services,
        Action<DynamoDbCacheOptions>? configure = null)
    {
        services.AddCacheWeave();
        services.AddAWSService<IAmazonDynamoDB>();

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<DynamoDbCacheOptions>();

        services.AddSingleton<ICacheProvider, DynamoDbCacheProvider>();
        return services;
    }
}
