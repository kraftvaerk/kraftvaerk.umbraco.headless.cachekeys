using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

/// <summary>
/// Extracts cache key dependencies embedded in Block List/Grid, Rich Text Editor, and picker
/// property values, including values nested inside blocks.
/// </summary>
internal sealed class BlockDependencyResolver(IContentTypeService contentTypeService, ILogger<BlockDependencyResolver> logger)
{
    private const int REGEX_TIMEOUT_MS = 1_000;

    /// <summary>
    /// Matches <c>umb://document/&lt;guid&gt;</c> or <c>umb://media/&lt;guid&gt;</c>, supporting both
    /// 32-char (N format) and 36-char (D format) GUIDs.
    /// </summary>
    private static readonly Regex UdiRegex =
        new(
            @"umb://(?<entityType>\w+)/(?<guid>[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(REGEX_TIMEOUT_MS)
        );

    /// <summary>Matches <c>{localLink:&lt;guid&gt;}</c> in RTE markup, supporting both 32/36-char GUID formats.</summary>
    private static readonly Regex LocalLinkRegex =
        new(
            @"{localLink:(?<guid>[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})}",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(REGEX_TIMEOUT_MS)
        );

    /// <summary>Caches resolved <see cref="IContentType"/> by element type key (contentTypeKey in block JSON).</summary>
    private readonly ConcurrentDictionary<Guid, IContentType?> _contentTypeCache = new();

    /// <summary>Caches resolved property editor alias by <c>(elementTypeKey, propertyAlias)</c>.</summary>
    private readonly ConcurrentDictionary<string, string?> _propertyEditorAliasCache = new();

    /// <summary>
    /// Resolves cache key dependencies from all properties on <paramref name="content"/>, including
    /// values nested inside Block List/Grid properties.
    /// </summary>
    public IEnumerable<string> GetBlockDependencies(IContent content, string? culture = null)
    {
        foreach (var property in content.Properties)
        {
            var rawValue = property.GetValue(culture)?.ToString();
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            var editorAlias = property.PropertyType.PropertyEditorAlias;

            if (editorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
            {
                JObject? nested;
                try
                {
                    nested = JObject.Parse(rawValue);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse {EditorAlias} property value as JSON", editorAlias);
                    nested = null;
                }

                if (nested != null)
                {
                    foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)nested))
                        yield return dep;
                }

                foreach (var dep in ExtractByRegex(rawValue))
                    yield return dep;

                yield break;
            }

            foreach (var dep in ExtractDependenciesFromPickerValue(editorAlias, rawValue))
                yield return dep;
        }
    }

    /// <summary>Parses raw block editor JSON and delegates to the <see cref="JToken"/> overload.</summary>
    private IEnumerable<string> ExtractDependenciesFromBlockEditorJson(string rawBlockJson)
    {
        if (string.IsNullOrWhiteSpace(rawBlockJson))
            yield break;

        JObject json;
        try
        {
            json = JObject.Parse(rawBlockJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse nested block editor JSON");
            yield break;
        }

        foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)json))
            yield return dep;
    }

    /// <summary>
    /// Extracts dependencies from a Block List/Grid JSON token by walking its <c>contentData</c>
    /// entries and resolving each block property's value through <see cref="ExtractDependenciesFromPickerValue"/>.
    /// </summary>
    private IEnumerable<string> ExtractDependenciesFromBlockEditorJson(JToken blocksToken)
    {
        var contentDataArray = blocksToken["contentData"] as JArray;
        if (contentDataArray == null)
            yield break;

        foreach (var block in contentDataArray)
        {
            var elementTypeKey = block?["contentTypeKey"]?.ToObject<Guid?>();
            var values = block?["values"] as JArray;
            if (values == null)
                continue;

            foreach (var valueEntry in values)
            {
                var propAlias = valueEntry?["alias"]?.ToString();
                var editor = valueEntry?["editorAlias"]?.ToString();
                var rawPickerValue = valueEntry?["value"]?.ToString();

                if (string.IsNullOrWhiteSpace(rawPickerValue) || string.IsNullOrWhiteSpace(propAlias))
                    continue;

                if (string.IsNullOrWhiteSpace(editor) && elementTypeKey.HasValue)
                    editor = GetEditorAliasFromElementType(elementTypeKey.Value, propAlias);

                if (string.IsNullOrWhiteSpace(editor))
                    editor = InferEditorAliasFromValue(rawPickerValue);

                foreach (var dep in ExtractDependenciesFromPickerValue(editor, rawPickerValue))
                    yield return dep;
            }
        }
    }

    /// <summary>
    /// Resolves a block property's editor alias from its element type and property alias, caching the result.
    /// </summary>
    /// <remarks>On Umbraco 17+, property types are resolved via <c>ContentType.PropertyTypes</c> since <c>GetPropertyType(string)</c> no longer exists.</remarks>
    private string? GetEditorAliasFromElementType(Guid elementTypeKey, string propertyAlias)
    {
        var cacheKey = $"{elementTypeKey:N}|{propertyAlias}";
        if (_propertyEditorAliasCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var ct = _contentTypeCache.GetOrAdd(elementTypeKey, key => contentTypeService.Get(key));

        var pt = ct?.PropertyTypes?
            .FirstOrDefault(x => x.Alias.Equals(propertyAlias, StringComparison.OrdinalIgnoreCase));

        var editorAlias = pt?.PropertyEditorAlias;

        _propertyEditorAliasCache[cacheKey] = editorAlias;
        return editorAlias;
    }

    /// <summary>Infers a property editor alias from the shape of its raw value when no alias is available.</summary>
    private string? InferEditorAliasFromValue(string rawValue)
    {
        if (LooksLikeMediaPicker3(rawValue))
            return "Umbraco.MediaPicker3";

        if (LooksLikeUdiStringArray(rawValue))
            return "Umbraco.MultiNodeTreePicker";

        return null;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="rawValue"/> looks like a MediaPicker v3 JSON array (objects containing <c>mediaKey</c>).</summary>
    private bool LooksLikeMediaPicker3(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.TrimStart().StartsWith('['))
            return false;

        try
        {
            var arr = JsonConvert.DeserializeObject<JArray>(rawValue);
            if (arr == null || arr.Count == 0)
                return false;

            foreach (var token in arr.Take(3))
            {
                if (token is JObject obj && obj["mediaKey"] != null)
                    return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse value while checking for MediaPicker3 shape");
        }

        return false;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="rawValue"/> looks like a JSON array of UDI strings.</summary>
    private bool LooksLikeUdiStringArray(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.TrimStart().StartsWith('['))
            return false;

        try
        {
            var arr = JsonConvert.DeserializeObject<JArray>(rawValue);
            if (arr == null || arr.Count == 0)
                return false;

            foreach (var token in arr.Take(3))
            {
                if (token.Type == JTokenType.String &&
                    token.ToString().Contains("umb://", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse value while checking for UDI-string-array shape");
        }

        return false;
    }

    /// <summary>
    /// Extracts cache key dependencies from a single property value, dispatching on <paramref name="editorAlias"/>.
    /// </summary>
    /// <remarks>
    /// Handles, in order:
    /// <list type="bullet">
    ///   <item><c>Umbraco.RichText</c> — parses the <c>markup</c> HTML and any embedded <c>blocks</c> as a block editor structure.</item>
    ///   <item><c>Umbraco.MediaPicker3</c> — parses a JSON array of objects containing <c>mediaKey</c>.</item>
    ///   <item><c>Umbraco.MultiNodeTreePicker</c> / <c>Umbraco.MediaPicker</c> — parses a single UDI string or a JSON array of UDI strings.</item>
    ///   <item><c>Umbraco.BlockList</c> / <c>Umbraco.BlockGrid</c> — recurses into a nested block editor structure.</item>
    ///   <item>Unknown editors — falls back to shape-based inference, then a brute-force regex scan.</item>
    /// </list>
    /// Every branch also runs a regex safety scan for <c>umb://</c>/<c>localLink</c> references, since editors
    /// can mix formats or nest structures the primary parser doesn't expect.
    /// C# does not allow <c>yield return</c> inside a try block that has a catch clause, so dependencies are
    /// collected into a list first and yielded afterwards.
    /// </remarks>
    private IEnumerable<string> ExtractDependenciesFromPickerValue(string? editorAlias, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            yield break;

        if (editorAlias == "Umbraco.RichText")
        {
            var deps = new List<string>();

            deps.AddRange(ExtractByRegex(rawValue));

            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(rawValue);

                var markup = obj?["markup"]?.ToString();
                if (!string.IsNullOrWhiteSpace(markup))
                    deps.AddRange(ExtractByRegex(markup));

                var blocksToken = obj?["blocks"];
                if (blocksToken != null)
                    deps.AddRange(ExtractDependenciesFromBlockEditorJson(blocksToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse RichText property value as JSON; falling back to regex scan");
            }

            foreach (var dep in deps)
                yield return dep;

            yield break;
        }

        if (editorAlias == "Umbraco.MediaPicker3")
        {
            var deps = new List<string>();

            try
            {
                var parsedArray = JsonConvert.DeserializeObject<JArray>(rawValue);
                if (parsedArray != null)
                {
                    foreach (var item in parsedArray)
                    {
                        var mediaKey = item?["mediaKey"]?.ToObject<Guid?>();
                        if (mediaKey.HasValue)
                            deps.Add($"media-{mediaKey.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse MediaPicker3 property value as JSON");
            }

            deps.AddRange(ExtractByRegex(rawValue));

            foreach (var dep in deps)
                yield return dep;

            yield break;
        }

        if (editorAlias is "Umbraco.MultiNodeTreePicker" or "Umbraco.MediaPicker")
        {
            var deps = new List<string>();

            if (rawValue.TrimStart().StartsWith('['))
            {
                try
                {
                    var udiStrings = JsonConvert.DeserializeObject<IEnumerable<string>>(rawValue);
                    if (udiStrings != null)
                    {
                        foreach (var udiStr in udiStrings)
                        {
                            if (UdiParser.TryParse(udiStr, out var udi) && udi is GuidUdi guidUdi)
                                deps.Add($"{ResolverUtils.GetPrefixFromUdi(udi)}-{guidUdi.Guid}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse {EditorAlias} property value as a JSON array", editorAlias);
                }
            }
            else
            {
                if (UdiParser.TryParse(rawValue, out var udi) && udi is GuidUdi guidUdi)
                    deps.Add($"{ResolverUtils.GetPrefixFromUdi(udi)}-{guidUdi.Guid}");
            }

            deps.AddRange(ExtractByRegex(rawValue));

            foreach (var dep in deps)
                yield return dep;

            yield break;
        }

        if (editorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
        {
            JObject nested;
            try
            {
                nested = JObject.Parse(rawValue);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse nested {EditorAlias} property value as JSON", editorAlias);
                yield break;
            }

            foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)nested))
                yield return dep;

            yield break;
        }

        if (LooksLikeMediaPicker3(rawValue))
        {
            foreach (var dep in ExtractDependenciesFromPickerValue("Umbraco.MediaPicker3", rawValue))
                yield return dep;

            yield break;
        }

        foreach (var dep in ExtractByRegex(rawValue))
            yield return dep;
    }

    /// <summary>
    /// Scans <paramref name="rawValue"/> for <c>umb://document/…</c>, <c>umb://media/…</c>, and
    /// <c>{localLink:&lt;guid&gt;}</c> references, independent of the property's editor or value shape.
    /// </summary>
    private IEnumerable<string> ExtractByRegex(string rawValue)
    {
        var deps = new List<string>();

        try
        {
            foreach (var (entityType, guidRaw) in
                from Match match in UdiRegex.Matches(rawValue)
                let entityType = match.Groups["entityType"].Value
                let guidRaw = match.Groups["guid"].Value
                select (entityType, guidRaw))
            {
                if (!Guid.TryParse(guidRaw, out var guid) && !Guid.TryParseExact(guidRaw, "N", out guid))
                    continue;

                var prefix = entityType switch
                {
                    "document" => "content",
                    "media" => "media",
                    _ => "unknown"
                };

                deps.Add($"{prefix}-{guid}");
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            logger.LogWarning(ex, "Timed out scanning property value for umb:// references");
        }

        try
        {
            foreach (Match match in LocalLinkRegex.Matches(rawValue))
            {
                var guidRaw = match.Groups["guid"].Value;

                if (!Guid.TryParse(guidRaw, out var guid) && !Guid.TryParseExact(guidRaw, "N", out guid))
                    continue;

                deps.Add($"content-{guid}");
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            logger.LogWarning(ex, "Timed out scanning property value for localLink references");
        }

        return deps;
    }
}
