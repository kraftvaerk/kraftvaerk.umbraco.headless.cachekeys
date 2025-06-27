using Umbraco.Cms.Core.Models;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;
public interface ICacheKeyDependencyResolver
{
    /// <summary>
    /// Resolves all cache keys that should be associated with the given content item.
    /// Includes direct and indirect dependencies.
    /// </summary>
    /// <param name="content">The published content item.</param>
    /// <returns>A collection of cache keys as strings.</returns>
    IEnumerable<string> GetDependencies(IContent content);

}

