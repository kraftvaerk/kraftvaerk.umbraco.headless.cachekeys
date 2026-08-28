#if NET10_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

/// <summary>
/// Adds a <c>cacheKeys</c> property to the Delivery API's generated OpenAPI schema (.NET 10+ Microsoft.AspNetCore.OpenApi).
/// </summary>
internal static class CacheKeysSchemaTransformer
{
    extension(IUmbracoBuilder builder)
    {
        /// <summary>
        /// Registers a schema transformer that adds <c>cacheKeys</c> to every <c>*PropertiesModel</c>
        /// schema in the Delivery API's OpenAPI document.
        /// </summary>
        /// <remarks>The document name <c>"delivery"</c> matches the one Umbraco.Cms.Api.Delivery registers.</remarks>
        public IUmbracoBuilder AddCacheKeysToDeliverySchema()
        {
            builder.Services.AddOpenApi("delivery", options =>
            {
                options.AddSchemaTransformer((schema, context, cancellationToken) =>
                {
                    var type = context.JsonTypeInfo.Type;
                    if (schema?.Properties is null || !type.Name.InvariantEndsWith("PropertiesModel"))
                    {
                        return Task.CompletedTask;
                    }

                    schema.Properties["cacheKeys"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "uuid"
                        }
                    };

                    return Task.CompletedTask;
                });
            });

            return builder;
        }
    }
}
#endif
