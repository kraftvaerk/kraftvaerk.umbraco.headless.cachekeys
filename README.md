# Kraftvaerk.Umbraco.Headless.CacheKeys

Adds a `cacheKey` field to responses from the [Umbraco Delivery API](https://docs.umbraco.com/umbraco-cms/api-delivery/index), making it easier for headless consumers to handle caching and invalidation strategies.

This package also extends the generated OpenAPI (Swagger) schema to include the new field for improved developer experience and tooling support.

## Installation

Install via NuGet:

dotnet add package Kraftvaerk.Umbraco.Headless.CacheKeys


## What it does

This package traverses the properties of your content and ad
s any dependency as a cache-key to the Content Delivery Api response.

If you tag or cache your pages with the resolved keys, and invalidate pages containing any of the keys, you should have a very snappy website without invalidating too much of your front-end on publish.

- Appends a `cacheKey` property to all Delivery API content responses on the property-list.
- Modifies the OpenAPI schema to document the new field.

Example response:

```json
{
  "contentType": "homePage",
  "name": "kjeldsen.dev",
  "createDate": "2025-03-25T20:12:39",
  "updateDate": "2025-05-08T10:25:10.1649117",
  "route": {
    "path": "/",
    "startItem": {}
  },
  "id": "35a65b13-4b94-4830-a6ca-53bc9f321544",
  "properties": {
    "childKeys": null,
    "links": [],
    "seoTitle": "Kjeldsen.dev",
    "seoDescription": "Nuxt blog",
    "seoPublishingDate": null,
    "seoListImage": null,
    "grid": {
      "gridColumns": 12,
      "items": [...]
    },
    "cacheKeys": [
      "content-35a65b13-4b94-4830-a6ca-53bc9f321544",
      "content-1d649970-2c0a-4df0-8422-94492edf6e9f"
    ]
  },
  "cultures": {}
}
```

It also supports media and will add media dependencies as cachekey too. Making it possible to invalidate any page containing a certain image or piece of media.

## Usage Notes

For simple cache keys you don't have to do anything.

To include cache keys for child content items, simply add a property with the alias childKeys (type: boolean) to your document type.

This can be a label or toggle depending on how you want to set it up. It is probably a good idea to make it as a composition so you can easily add it to any page you want.

When this property is set to true, the response will automatically include cache keys for all child items as well (and their dependencies).

This is particularly useful for pages like /blog or /news, where you want the cache to be invalidated not only when the listing changes, but also when individual blog posts are updated.

Note that this is recursive, child items which also implement "childKeys" with the value true will result in further traversal.

## Invalidation

Invalidation is deemed to be too project specific to implement in this package in a way that will fit most scenarios.

In my own Nuxt app I do the following in Umbraco

```csharp
public class ContentPublishedCacheKeyLogger : INotificationAsyncHandler<ContentSavedNotification>
{
    private readonly ICacheKeyDependencyResolver _resolver;
    private readonly ILogger<ContentPublishedCacheKeyLogger> _logger;
    private readonly string _nuxtHost;
    private readonly string _nuxtApiKey;
    private readonly HttpClient _httpClient;

    public ContentPublishedCacheKeyLogger(
        ICacheKeyDependencyResolver resolver,
        IOptions<NuxtSettings> nuxtSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<ContentPublishedCacheKeyLogger> logger)
    {
        _resolver = resolver;
        _logger = logger;
        _nuxtHost = nuxtSettings.Value.Host.TrimEnd('/');
        _nuxtApiKey = nuxtSettings.Value.ApiKey;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task HandleAsync(ContentSavedNotification notification, CancellationToken cancellationToken)
    {
        var tags = new HashSet<string>();

        foreach (var content in notification.SavedEntities)
        {
            var keys = _resolver.GetDependencies(content);
            foreach (var key in keys)
            {
                tags.Add(key);
            }
        }

        if (tags.Count > 0)
        {
            await InvalidateFrontendAsync(tags); // Fire and forget
        }
    }

    private async Task InvalidateFrontendAsync(IEnumerable<string> tags)
    {
        var payload = JsonConvert.SerializeObject(tags);
        var request = new StringContent(payload, Encoding.UTF8, "application/json");

        // Add the API key header here
        request.Headers.Add("x-nuxt-multi-cache-token", _nuxtApiKey);

        var url = $"{_nuxtHost}/__nuxt_multi_cache/purge/tags";

        try
        {
            var response = await _httpClient.PostAsync(url, request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nuxt cache invalidation failed: {StatusCode} - {Reason}", response.StatusCode, response.ReasonPhrase);
            }
            else
            {
                _logger.LogInformation("Nuxt cache invalidation triggered for tags: {Tags}", string.Join(", ", tags));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Nuxt cache invalidation endpoint");
        }
    }
}
```

and this in [...slug].vue (with nuxt-multi-cache)

```ts
if (data.value?.properties.cacheKeys) {
  const cacheKeys = data.value.properties.cacheKeys || [];
  const tags = ["reset", ...cacheKeys];
  const timestamp = new Date().toISOString();

console.log(`\n?? [${timestamp}] Cache Miss! These keys were not found: ??\n`);
console.table(
  cacheKeys.map((key, index) => ({
    '#': index + 1,
    'Cache Key': key,
  }))
);


  useRouteCache((helper) => {
    helper
      .setMaxAge(3600 * 24)
      .setCacheable()
      .addTags(tags);
  });
}
```
