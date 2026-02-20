using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers
{
    public class BlockDependencyResolver
    {
        // Matches umb://document/<guid> or umb://media/<guid>
        // Supports both 32-char GUIDs (N format) and 36-char GUIDs (D format).
        private static readonly Regex UdiRegex =
            new(
                @"umb://(?<entityType>\w+)/(?<guid>[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})",
                RegexOptions.Compiled
            );

        // Matches {localLink:<guid>} in RTE markup. Supports both 32/36-char GUID formats.
        private static readonly Regex LocalLinkRegex =
            new(
                @"{localLink:(?<guid>[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})}",
                RegexOptions.Compiled
            );

        private readonly IContentTypeService _contentTypeService;

        // Cache: elementTypeKey (contentTypeKey in block JSON) -> IContentType
        private readonly ConcurrentDictionary<Guid, IContentType?> _contentTypeCache = new();

        // Cache: (elementTypeKey, propertyAlias) -> property editor alias
        private readonly ConcurrentDictionary<string, string?> _propertyEditorAliasCache = new();

        public BlockDependencyResolver(IContentTypeService contentTypeService)
        {
            _contentTypeService = contentTypeService;
        }

        public IEnumerable<string> GetBlockDependencies(IContent content, string? culture = null)
        {
            foreach (var property in content.Properties)
            {
                var rawValue = property.GetValue(culture)?.ToString();
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                var editorAlias = property.PropertyType.PropertyEditorAlias;

                /*
                 * NESTED BLOCK EDITORS (Umbraco.BlockList / Umbraco.BlockGrid)
                 *
                 * When a block contains another BlockList/BlockGrid property, the "value"
                 * is a JSON string with contentData/settingsData/etc.
                 *
                 * We must parse and run the same block extraction recursively.
                 */
                if (editorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
                {
                    JObject? nested;
                    try
                    {
                        nested = JObject.Parse(rawValue);
                    }
                    catch
                    {
                        nested = null;
                    }

                    if (nested != null)
                    {
                        // IMPORTANT: reuse the block parser so it can find MediaPicker3 etc.
                        foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)nested))
                            yield return dep;
                    }

                    // Extra safety: regex scan too (in case the nested structure includes UDIs/localLinks)
                    foreach (var dep in ExtractByRegex(rawValue))
                        yield return dep;

                    yield break;
                }


                // Optional: also scan non-block properties (e.g. RTE containing localLink or data-udi).
                foreach (var dep in ExtractDependenciesFromPickerValue(editorAlias, rawValue))
                    yield return dep;
            }
        }

        // Parse block editor JSON from a string and delegate to the JToken overload.
        private IEnumerable<string> ExtractDependenciesFromBlockEditorJson(string rawBlockJson)
        {
            if (string.IsNullOrWhiteSpace(rawBlockJson))
                yield break;

            JObject json;
            try
            {
                json = JObject.Parse(rawBlockJson);
            }
            catch
            {
                yield break;
            }

            foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)json))
                yield return dep;
        }

        // Extract dependencies from a block editor JSON token (BlockList/BlockGrid or embedded RTE blocks).
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
                    var editor = valueEntry?["editorAlias"]?.ToString(); // may be null in some payloads
                    var rawPickerValue = valueEntry?["value"]?.ToString();

                    if (string.IsNullOrWhiteSpace(rawPickerValue) || string.IsNullOrWhiteSpace(propAlias))
                        continue;

                    // If editorAlias is missing, try to resolve it via elementTypeKey + property alias.
                    if (string.IsNullOrWhiteSpace(editor) && elementTypeKey.HasValue)
                        editor = GetEditorAliasFromElementType(elementTypeKey.Value, propAlias);

                    // If still unknown, infer based on value shape.
                    if (string.IsNullOrWhiteSpace(editor))
                        editor = InferEditorAliasFromValue(rawPickerValue);

                    foreach (var dep in ExtractDependenciesFromPickerValue(editor, rawPickerValue))
                        yield return dep;
                }
            }
        }

        private string? GetEditorAliasFromElementType(Guid elementTypeKey, string propertyAlias)
        {
            var cacheKey = $"{elementTypeKey:N}|{propertyAlias}";
            if (_propertyEditorAliasCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var ct = _contentTypeCache.GetOrAdd(elementTypeKey, key => _contentTypeService.Get(key));

            // Umbraco 17: resolve property type from PropertyTypes (no GetPropertyType(string) method).
            var pt = ct?.PropertyTypes?
                .FirstOrDefault(x => x.Alias.Equals(propertyAlias, StringComparison.OrdinalIgnoreCase));

            var editorAlias = pt?.PropertyEditorAlias;

            _propertyEditorAliasCache[cacheKey] = editorAlias;
            return editorAlias;
        }

        private static string? InferEditorAliasFromValue(string rawValue)
        {
            // If it looks like a MediaPicker3 JSON array containing "mediaKey"
            if (LooksLikeMediaPicker3(rawValue))
                return "Umbraco.MediaPicker3";

            // If it looks like a JSON array of UDI strings
            if (LooksLikeUdiStringArray(rawValue))
                return "Umbraco.MultiNodeTreePicker";

            return null;
        }

        private static bool LooksLikeMediaPicker3(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.TrimStart().StartsWith("["))
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
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool LooksLikeUdiStringArray(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.TrimStart().StartsWith("["))
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
            catch
            {
                // ignore
            }

            return false;
        }

        private IEnumerable<string> ExtractDependenciesFromPickerValue(string? editorAlias, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                yield break;

            /*
             * RICH TEXT EDITOR (Umbraco.RichText)
             *
             * Stored as JSON string containing:
             *  - "markup" (HTML with data-udi / localLink)
             *  - optional embedded "blocks" (RTE blocks)
             *
             * We must parse "blocks" as a block editor structure to capture e.g. MediaPicker3 values.
             *
             * NOTE: C# does NOT allow 'yield return' inside a try block that has a catch clause.
             * Therefore we collect dependencies in a list and yield them afterwards.
             */
            if (editorAlias == "Umbraco.RichText")
            {
                var deps = new List<string>();

                // Safety scan: catches localLink/UDI references anywhere in the JSON.
                deps.AddRange(ExtractByRegex(rawValue));

                try
                {
                    var obj = JsonConvert.DeserializeObject<JObject>(rawValue);

                    // Scan markup HTML for data-udi or localLink.
                    var markup = obj?["markup"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(markup))
                        deps.AddRange(ExtractByRegex(markup));

                    // IMPORTANT: parse embedded RTE blocks as block editor JSON
                    var blocksToken = obj?["blocks"];
                    if (blocksToken != null)
                        deps.AddRange(ExtractDependenciesFromBlockEditorJson(blocksToken));
                }
                catch
                {
                    // Ignore parsing errors and rely on regex scan fallback.
                }

                foreach (var dep in deps)
                    yield return dep;

                yield break;
            }

            /*
             * MEDIAPICKER v3
             *
             * Stored as JSON array of objects containing "mediaKey".
             */
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
                catch
                {
                    // ignore
                }

                // Extra safety: also scan for UDI/localLink in case formats are mixed.
                deps.AddRange(ExtractByRegex(rawValue));

                foreach (var dep in deps)
                    yield return dep;

                yield break;
            }

            /*
             * MULTI NODE TREE PICKER / LEGACY MEDIA PICKER
             *
             * Can be:
             *  - a single UDI string
             *  - a JSON array of UDI strings
             */
            if (editorAlias is "Umbraco.MultiNodeTreePicker" or "Umbraco.MediaPicker")
            {
                var deps = new List<string>();

                if (rawValue.TrimStart().StartsWith("["))
                {
                    try
                    {
                        var udiStrings = JsonConvert.DeserializeObject<IEnumerable<string>>(rawValue);
                        if (udiStrings != null)
                        {
                            foreach (var udiStr in udiStrings)
                            {
                                if (UdiParser.TryParse(udiStr, out var udi) && udi is GuidUdi guidUdi)
                                    deps.Add($"{GetPrefixFromUdi(udi)}-{guidUdi.Guid}");
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
                else
                {
                    if (UdiParser.TryParse(rawValue, out var udi) && udi is GuidUdi guidUdi)
                        deps.Add($"{GetPrefixFromUdi(udi)}-{guidUdi.Guid}");
                }

                // Extra safety: regex scan.
                deps.AddRange(ExtractByRegex(rawValue));

                foreach (var dep in deps)
                    yield return dep;

                yield break;
            }

            // Nested block editors: BlockList / BlockGrid inside another block (recursive case).
            if (editorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
            {
                JObject nested;
                try
                {
                    nested = JObject.Parse(rawValue);
                }
                catch
                {
                    yield break;
                }

                foreach (var dep in ExtractDependenciesFromBlockEditorJson((JToken)nested))
                    yield return dep;

                yield break;
            }


            /*
             * UNKNOWN EDITOR
             *
             * Heuristic:
             *  - If it looks like MediaPicker3 JSON, parse as MediaPicker3
             *  - Otherwise, brute-force regex scan
             */
            if (LooksLikeMediaPicker3(rawValue))
            {
                foreach (var dep in ExtractDependenciesFromPickerValue("Umbraco.MediaPicker3", rawValue))
                    yield return dep;

                yield break;
            }

            // Final fallback: brute-force regex scan.
            foreach (var dep in ExtractByRegex(rawValue))
                yield return dep;
        }

        private static IEnumerable<string> ExtractByRegex(string rawValue)
        {
            // Scan for umb://document/... and umb://media/... references.
            foreach (Match match in UdiRegex.Matches(rawValue))
            {
                var entityType = match.Groups["entityType"].Value;
                var guidRaw = match.Groups["guid"].Value;

                Guid guid;

                // 36-char GUID with dashes
                if (!Guid.TryParse(guidRaw, out guid))
                {
                    // 32-char GUID without dashes (N-format)
                    if (!Guid.TryParseExact(guidRaw, "N", out guid))
                        continue;
                }

                var prefix = entityType switch
                {
                    "document" => "content",
                    "media" => "media",
                    _ => "unknown"
                };

                yield return $"{prefix}-{guid}";
            }

            // Scan for {localLink:<guid>} references.
            foreach (Match match in LocalLinkRegex.Matches(rawValue))
            {
                var guidRaw = match.Groups["guid"].Value;

                Guid guid;
                if (!Guid.TryParse(guidRaw, out guid))
                {
                    if (!Guid.TryParseExact(guidRaw, "N", out guid))
                        continue;
                }

                yield return $"content-{guid}";
            }
        }

        private static string GetPrefixFromUdi(Udi udi)
        {
            return udi.EntityType switch
            {
                "document" => "content",
                "media" => "media",
                _ => "unknown"
            };
        }
    }
}
