using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DeliveryApi;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend;
public class Compose : IComposer
{
    void IComposer.Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddTransient<ICacheKeyDependencyResolver, CacheKeyDependencyResolver>();
        builder.Services.Decorate<IApiContentResponseBuilder, CacheKeyDecoratingResponseBuilder>();
        builder.Services.AddTransient<PickerDependencyResolver>();
        builder.Services.AddTransient<RelationDependencyResolver>();
        builder.Services.AddTransient<BlockDependencyResolver>();
        builder.Services.PostConfigure<SwaggerGenOptions>(options =>
        {
            options.DocumentFilter<CustomSchemaFilter>();
        });
    }
}

