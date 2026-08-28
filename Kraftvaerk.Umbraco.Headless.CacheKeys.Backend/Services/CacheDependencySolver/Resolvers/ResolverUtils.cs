using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Umbraco.Cms.Core;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

internal static class ResolverUtils
{
    /// <summary>Maps a <see cref="Udi"/> entity type to a cache key prefix.</summary>
    public static string GetPrefixFromUdi(Udi udi) => udi.EntityType switch
    {
        "document" => "content",
        "media" => "media",
        _ => "unknown"
    };

    /// <summary>
    /// Attempts to deserialize <paramref name="raw"/> as <typeparamref name="T"/>.
    /// Returns <see langword="false"/> without throwing if the input is not valid JSON.
    /// </summary>
    /// <param name="logger">When provided, logs a warning on parse failure.</param>
    public static bool TryParseJson<T>(string raw, out T? result, ILogger? logger = null) where T : class
    {
        try
        {
            result = JsonConvert.DeserializeObject<T>(raw);
            return result is not null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse value as {TargetType}: {Value}", typeof(T).Name, raw);
            result = null;
            return false;
        }
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="value"/> starts with <c>[</c> after trimming.</summary>
    public static bool StartsWithJsonArray(string value)
        => value.AsSpan().TrimStart().StartsWith("[");
}
