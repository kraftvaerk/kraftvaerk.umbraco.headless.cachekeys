using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

/// <summary>
/// Resolves cache key dependencies from Umbraco relations pointing at a content item.
/// </summary>
internal sealed class RelationDependencyResolver(IRelationService relationService, IContentService contentService)
{
    /// <summary>
    /// Resolves cache keys for content related to <paramref name="content"/> through relation types
    /// explicitly marked as dependencies, excluding housekeeping types like <c>relateOnCopy</c>/<c>relateOnTrash</c>.
    /// </summary>
    /// <param name="content">The content node to inspect.</param>
    /// <param name="culture">Culture to filter related content by; unfiltered when <see langword="null"/>.</param>
    public IEnumerable<string> GetRelationDependencies(IContent content, string? culture = null)
    {
        var relations = relationService.GetByChildId(content.Id);

        foreach (var relation in relations)
        {
            if (relation.RelationType is not IRelationTypeWithIsDependency { IsDependency: true })
                continue;

            var related = contentService.GetById(relation.ParentId);
            if (related == null)
                continue;

            if (!string.IsNullOrEmpty(culture) &&
                related.AvailableCultures.Any() &&
                !related.AvailableCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
                continue;

            yield return $"content-{related.Key}";
        }
    }
}
