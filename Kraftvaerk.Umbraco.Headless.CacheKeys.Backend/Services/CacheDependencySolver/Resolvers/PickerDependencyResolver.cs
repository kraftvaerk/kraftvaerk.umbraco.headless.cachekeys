using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

/// <summary>
/// Resolves cache key dependencies for picker-type editors:
/// <c>Umbraco.MultiNodeTreePicker</c>, <c>Umbraco.MediaPicker</c>, and <c>Umbraco.MediaPicker3</c>.
/// </summary>
internal sealed class PickerDependencyResolver(ILogger<PickerDependencyResolver> logger)
{
    /// <summary>
    /// Resolves cache key dependencies from all picker properties on <paramref name="content"/>.
    /// </summary>
    /// <param name="content">The content node to inspect.</param>
    /// <param name="culture">
    ///     Culture to use when reading property values. Pass the request culture explicitly —
    ///     never rely on ambient thread culture.
    /// </param>
    public IEnumerable<string> GetPickerDependencies(IContent content, string? culture = null)
    {
        foreach (var property in content.Properties)
        {
            var rawValue = property.GetValue(culture)?.ToString();

            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            if (property.PropertyType.PropertyEditorAlias is not (
                "Umbraco.MultiNodeTreePicker" or
                "Umbraco.MediaPicker" or
                "Umbraco.MediaPicker3"))
                continue;

            foreach (var key in ExtractGuidsFromValue(rawValue))
                yield return key;
        }
    }

    /// <summary>
    /// Extracts cache key strings from a raw picker value.
    /// </summary>
    /// <remarks>
    /// Handles three value shapes:
    /// <list type="bullet">
    ///   <item>JSON array of objects with <c>mediaKey</c> or <c>key</c> (MediaPicker v3).</item>
    ///   <item>JSON array of UDI strings (MultiNodeTreePicker / legacy MediaPicker).</item>
    ///   <item>Single UDI string.</item>
    /// </list>
    /// Malformed values yield nothing; parse failures are logged as warnings.
    /// </remarks>
    private IEnumerable<string> ExtractGuidsFromValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        if (!ResolverUtils.StartsWithJsonArray(value))
        {
            if (UdiParser.TryParse(value, out Udi? udi) && udi is GuidUdi guidUdi)
                yield return $"{ResolverUtils.GetPrefixFromUdi(udi)}-{guidUdi.Guid}";

            yield break;
        }

        if (!ResolverUtils.TryParseJson<JArray>(value, out var array, logger) || array!.Count == 0)
            yield break;

        if (array[0] is JObject)
        {
            foreach (var item in array.OfType<JObject>())
            {
                var (key, type) = item["mediaKey"] is { } mediaKey
                    ? (mediaKey, "media")
                    : (item["key"], "content");

                if (key is not null)
                    yield return $"{type}-{key}";
            }
        }
        else
        {
            foreach (var udiStr in array.Values<string>())
            {
                if (string.IsNullOrWhiteSpace(udiStr))
                    continue;

                if (UdiParser.TryParse(udiStr, out var udi) && udi is GuidUdi guidUdi)
                    yield return $"{ResolverUtils.GetPrefixFromUdi(udi)}-{guidUdi.Guid}";
            }
        }
    }
}
