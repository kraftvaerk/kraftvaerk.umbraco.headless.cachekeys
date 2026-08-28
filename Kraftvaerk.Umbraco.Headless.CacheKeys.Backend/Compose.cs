using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DeliveryApi;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend;

/// <summary>
/// Registers the cache-key dependency resolvers, decorates the Delivery API response builder,
/// and wires the <c>cacheKeys</c> property into the OpenAPI/Swagger schema.
/// </summary>
internal sealed class Compose : IComposer
{
    /// <inheritdoc/>
    void IComposer.Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddTransient<ICacheKeyDependencyResolver, CacheKeyDependencyResolver>();
        builder.Services.Decorate<IApiContentResponseBuilder, CacheKeyDecoratingResponseBuilder>();
        builder.Services.AddTransient<RelationDependencyResolver>();
        builder.Services.AddTransient<BlockDependencyResolver>();
        builder.Services.AddTransient<PickerDependencyResolver>();
        builder.Services.PostConfigure<SwaggerGenOptions>(options =>
        {
            options.DocumentFilter<CustomSchemaFilter>();
        });
#if NET10_0_OR_GREATER
        builder.AddCacheKeysToDeliverySchema();
#endif
    }
}

