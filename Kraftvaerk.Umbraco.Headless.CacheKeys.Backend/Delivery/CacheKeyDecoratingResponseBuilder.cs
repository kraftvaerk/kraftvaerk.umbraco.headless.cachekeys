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

    public CacheKeyDecoratingResponseBuilder(
        IApiContentResponseBuilder inner,
        IContentService contentService,
        ICacheKeyDependencyResolver cacheKeyDependencyResolver)
    {
        _inner = inner;
        _cacheKeyDependencyResolver = cacheKeyDependencyResolver;
        _contentService = contentService;
    }


    public IApiContentResponse? Build(IPublishedContent content)
    {
        var response = _inner.Build(content);
        var i_content = _contentService.GetById(content.Id);
        var keys = _cacheKeyDependencyResolver.GetDependencies(i_content!);
        response?.Properties.TryAdd("cacheKeys", keys);

        return response;
    }
}
