using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DeliveryApi;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend;
public class Compose : IComposer
{
    void IComposer.Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<ICacheKeyDependencyResolver, CacheKeyDependencyResolver>();
        builder.Services.Decorate<IApiContentResponseBuilder, CacheKeyDecoratingResponseBuilder>();
        builder.Services.PostConfigure<SwaggerGenOptions>(options =>
        {
            options.DocumentFilter<CustomSchemaFilter>();
        });
    }
}

