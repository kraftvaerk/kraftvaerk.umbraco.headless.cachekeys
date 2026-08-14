using Microsoft.OpenApi;
#if NET10_0_OR_GREATER

#else
using Microsoft.OpenApi.Models;
#endif

using Swashbuckle.AspNetCore.SwaggerGen;
namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

/// <summary>
/// Adds a <c>cacheKeys</c> property to every <c>*PropertiesModel</c> schema in the generated
/// Swagger/OpenAPI document.
/// </summary>
public sealed class CustomSchemaFilter : IDocumentFilter
{
    /// <summary>
    /// Adds the <c>cacheKeys</c> array-of-uuid property to each matching schema in <paramref name="context"/>.
    /// </summary>
    /// <remarks>
    /// Targets .NET 10+'s <see cref="JsonSchemaType"/> enum where available, falling back to the
    /// string-based <c>Type</c>/<c>Format</c> representation used by Microsoft.OpenApi.Models on earlier targets.
    /// </remarks>
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var (_, schema) in context.SchemaRepository.Schemas
                     .Where(x => x.Key.InvariantEndsWith("PropertiesModel")))
        {
            if(schema.Properties is null)
            {
                continue;
            }
#if NET10_0_OR_GREATER
            schema.Properties["cacheKeys"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "uuid"
                }
            };
#else
            schema.Properties["cacheKeys"] = new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema
                {
                    Type = "string",
                    Format = "uuid"
                }
            };
#endif

        }

    }
}
