using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models.DeliveryApi;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;
using Umbraco.Cms.Core.Models;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

/// <summary>
/// Decorates Delivery API content responses with a <c>cacheKeys</c> property listing the cache
/// keys the response depends on.
/// </summary>
public sealed class CacheKeyDecoratingResponseBuilder(
    IApiContentResponseBuilder inner,
    IContentService contentService,
    ICacheKeyDependencyResolver cacheKeyDependencyResolver,
    IVariationContextAccessor variationContextAccessor,
    IIdKeyMap idKeyMap,
    ILogger<CacheKeyDecoratingResponseBuilder> logger) : IApiContentResponseBuilder
{
    /// <summary>
    /// Builds the inner response, then resolves and appends <c>cacheKeys</c> for <paramref name="content"/>.
    /// </summary>
    /// <remarks>
    /// Looks up the <see cref="IContent"/> via <see cref="IIdKeyMap"/> rather than <c>content.Id</c>,
    /// which was deprecated in Umbraco v15 and removed in v18.
    /// </remarks>
    public IApiContentResponse? Build(IPublishedContent content)
    {
        var response = inner.Build(content);

        var idAttempt = idKeyMap.GetIdForKey(content.Key, UmbracoObjectTypes.Document);
        if (!idAttempt.Success)
        {
            logger.LogWarning("Could not resolve an IContent id for content key {ContentKey}; cacheKeys will be omitted", content.Key);
            return response;
        }

        var iContent = contentService.GetById(idAttempt.Result);
        if (iContent == null)
        {
            logger.LogWarning("No IContent found for id {ContentId} (key {ContentKey}); cacheKeys will be omitted", idAttempt.Result, content.Key);
            return response;
        }

        var culture = variationContextAccessor.VariationContext?.Culture;

        var keys = cacheKeyDependencyResolver.GetDependencies(iContent, culture);

        response?.Properties.TryAdd("cacheKeys", keys);
        return response;
    }
}
