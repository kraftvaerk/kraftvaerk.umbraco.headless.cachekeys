using System;
using System.Collections.Generic;
using System.Globalization;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;

public class CacheKeyDependencyResolver : ICacheKeyDependencyResolver
{
    private readonly PickerDependencyResolver _pickerResolver;
    private readonly BlockDependencyResolver _blockResolver;
    private readonly RelationDependencyResolver _relationResolver;
    private readonly IContentService _contentService;

    public CacheKeyDependencyResolver(
        PickerDependencyResolver pickerResolver,
        BlockDependencyResolver blockResolver,
        RelationDependencyResolver relationResolver,
        IContentService contentService)
    {
        _pickerResolver = pickerResolver;
        _blockResolver = blockResolver;
        _relationResolver = relationResolver;
        _contentService = contentService;
    }

    // Backwards compatible entry point (no culture passed in)
    public IEnumerable<string> GetDependencies(IContent content)
        => GetDependencies(content, culture: null);

    // New entry point: deterministic per-culture dependency calculation
    public IEnumerable<string> GetDependencies(IContent content, string? culture = null)
    {
        // IMPORTANT:
        // - Do not use CultureInfo.CurrentCulture here (thread culture != request culture).
        // - Prefer explicit culture passed in from request / Delivery API.
        // - If none is provided, fall back to invariant (null) or CultureInfo.CurrentUICulture.Name if it exists.
        var cultureName = NormalizeCulture(culture) ?? NormalizeCulture(CultureInfo.CurrentUICulture.Name);

        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"content-{content.Key}"
        };

        // Pass culture through to every resolver that reads property values
        dependencies.UnionWith(_pickerResolver.GetPickerDependencies(content, cultureName));
        dependencies.UnionWith(_blockResolver.GetBlockDependencies(content, cultureName));
        dependencies.UnionWith(_relationResolver.GetRelationDependencies(content, cultureName));

        // Optional recursion: include child dependencies if "childKeys" is enabled
        if (content.HasProperty("childKeys"))
        {
            if (TryGetBool(content, "childKeys", cultureName, out var includeChildren) && includeChildren)
            {
                // NOTE: choose a sensible page size or loop pages if needed
                var children = _contentService.GetPagedChildren(content.Id, 0, 100, out _);

                foreach (var child in children)
                {
                    // If you want to be strict per-culture publish state, you may need a culture-aware check here.
                    // For now, keep the original behavior.
                    if (child.Published)
                        dependencies.UnionWith(GetDependencies(child, cultureName));
                }
            }
        }

        return dependencies;
    }

    private static string? NormalizeCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return null;

        return culture.Trim();
    }

    /// <summary>
    /// Reads a boolean property value with culture awareness if available.
    /// Falls back to invariant value if the culture value is missing or API overloads behave differently.
    /// </summary>
    private static bool TryGetBool(IContent content, string alias, string? culture, out bool value)
    {
        // Default
        value = false;

        try
        {
            // Many Umbraco versions support this overload
            var obj = content.GetValue(alias, culture);

            if (obj is null)
                return false;

            if (obj is bool b)
            {
                value = b;
                return true;
            }

            // Handles "1"/"0", "true"/"false"
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
            // Fallback: invariant read
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
