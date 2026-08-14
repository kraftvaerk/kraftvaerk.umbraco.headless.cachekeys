using System.Globalization;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;

/// <summary>
/// Default <see cref="ICacheKeyDependencyResolver"/> implementation. Aggregates dependencies from
/// picker, block, and relation properties, and optionally recurses into children.
/// </summary>
internal sealed class CacheKeyDependencyResolver(
    PickerDependencyResolver pickerResolver,
    BlockDependencyResolver blockResolver,
    RelationDependencyResolver relationResolver,
    IContentService contentService,
    IUmbracoContextFactory umbracoContextFactory,
    IDocumentNavigationQueryService documentNavigationQueryService,
    IIdKeyMap idKeyMap
    ) : ICacheKeyDependencyResolver
{
    /// <summary>Guards against unbounded recursion into deep or cyclic content trees.</summary>
    private const int MaxRecursionDepth = 20;

    /// <inheritdoc cref="ICacheKeyDependencyResolver.GetDependencies(IContent, string?)"/>
    /// <remarks>Backwards compatible overload - resolves dependencies without a specific culture</remarks>
    public IEnumerable<string> GetDependencies(IContent content) => GetDependencies(content, culture: null);

    /// <inheritdoc cref="ICacheKeyDependencyResolver.GetDependencies(IContent, string?)"/>
    public IEnumerable<string> GetDependencies(IContent content, string? culture = null) =>
        GetDependencies(content, culture, depth: 0);

    /// <summary>
    /// Resolves picker, block, and relation dependencies for <paramref name="content"/>, recursing into
    /// children when the content's <c>childKeys</c> property is enabled, up to <see cref="MaxRecursionDepth"/>.
    /// </summary>
    private IEnumerable<string> GetDependencies(IContent content, string? culture, int depth)
    {
        var cultureName = NormalizeCulture(culture) ?? NormalizeCulture(CultureInfo.CurrentUICulture.Name);

        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"content-{content.Key}"
        };

        dependencies.UnionWith(pickerResolver.GetPickerDependencies(content, cultureName));
        dependencies.UnionWith(blockResolver.GetBlockDependencies(content, cultureName));
        dependencies.UnionWith(relationResolver.GetRelationDependencies(content, cultureName));

        if (content.HasProperty("childKeys"))
        {
            if (!TryGetBool(content, "childKeys", cultureName, out bool includeChildren) || !includeChildren)
                return dependencies;

            if (depth >= MaxRecursionDepth)
                return dependencies;

            if (!documentNavigationQueryService.TryGetChildrenKeys(content.Key, out var childrenKeys))
                return dependencies;

            using var contextReference = umbracoContextFactory.EnsureUmbracoContext();
            var publishedCache = contextReference.UmbracoContext.Content;

            foreach (var childKey in childrenKeys)
            {
                var publishedChild = publishedCache?.GetById(childKey);
                if (publishedChild is null)
                    continue;

                if (!string.IsNullOrEmpty(cultureName) && !publishedChild.HasCulture(cultureName))
                    continue;

                var idAttempt = idKeyMap.GetIdForKey(childKey, UmbracoObjectTypes.Document);
                if (!idAttempt.Success)
                    continue;

                var childContent = contentService.GetById(idAttempt.Result);
                if (childContent is null)
                    continue;

                dependencies.UnionWith(GetDependencies(childContent, cultureName, depth + 1));
            }

            return dependencies;
        }

        return dependencies;
    }

    /// <summary>
    /// Normalizes a culture string by trimming whitespace, returning <see langword="null"/> for blank input.
    /// <paramref name="culture"/> should be a valid culture name (e.g., "en-US") or <see langword="null"/> for invariant.
    /// <returns>The normalized culture string, or <see langword="null"/> if the input is blank.</returns>
    /// </summary>
    private static string? NormalizeCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return null;

        return culture.Trim();
    }

    /// <summary>
    /// Attempts to read a boolean property value from <paramref name="content"/> with culture awareness.
    /// Supports <see langword="bool"/>, <c>"true"/"false"</c>, and integer (<c>1</c>/<c>0</c>) representations.
    /// </summary>
    /// <param name="content">The content node to read from.</param>
    /// <param name="alias">The property alias.</param>
    /// <param name="culture">
    ///     The culture to use. Falls back to an invariant read if the culture-specific overload
    ///     throws or is unavailable in the current Umbraco version.
    /// </param>
    /// <param name="value">The resolved boolean value, or <see langword="false"/> if resolution fails.</param>
    /// <returns><see langword="true"/> if a value was successfully resolved; otherwise <see langword="false"/>.</returns>
    private static bool TryGetBool(IContent content, string alias, string? culture, out bool value)
    {
        value = false;

        try
        {
            var obj = content.GetValue(alias, culture);

            if (obj is null)
                return false;

            if (obj is bool b)
            {
                value = b;
                return true;
            }

            if (bool.TryParse(obj.ToString(), out var parsedBool))
            {
                value = parsedBool;
                return true;
            }

            if (int.TryParse(obj.ToString(), out var parsedInt))
            {
                value = parsedInt != 0;
                return true;
            }

            return false;
        }
        catch
        {
            try
            {
                value = content.GetValue<bool>(alias);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
