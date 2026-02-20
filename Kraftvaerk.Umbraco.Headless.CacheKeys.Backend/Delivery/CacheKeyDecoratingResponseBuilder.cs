using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models.DeliveryApi;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

public class CacheKeyDecoratingResponseBuilder : IApiContentResponseBuilder
{
    private readonly IApiContentResponseBuilder _inner;
    private readonly ICacheKeyDependencyResolver _cacheKeyDependencyResolver;
    private readonly IContentService _contentService;
    private readonly IVariationContextAccessor _variationContextAccessor;

    public CacheKeyDecoratingResponseBuilder(
        IApiContentResponseBuilder inner,
        IContentService contentService,
        ICacheKeyDependencyResolver cacheKeyDependencyResolver,
        IVariationContextAccessor variationContextAccessor)
    {
        _inner = inner;
        _cacheKeyDependencyResolver = cacheKeyDependencyResolver;
        _contentService = contentService;
        _variationContextAccessor = variationContextAccessor;
    }

    public IApiContentResponse? Build(IPublishedContent content)
    {
        var response = _inner.Build(content);

        var iContent = _contentService.GetById(content.Id);
        if (iContent == null)
            return response;

        // This is the culture the Delivery API / published pipeline is currently using
        var culture = _variationContextAccessor?.VariationContext?.Culture;

        var keys = _cacheKeyDependencyResolver.GetDependencies(iContent, culture);

        response?.Properties.TryAdd("cacheKeys", keys);
        return response;
    }
}