using Microsoft.OpenApi;
#if NET10_0_OR_GREATER

#else
using Microsoft.OpenApi.Models;
#endif

using Swashbuckle.AspNetCore.SwaggerGen;
namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

public sealed class CustomSchemaFilter : IDocumentFilter
{

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var (schemaId, schema) in context.SchemaRepository.Schemas
                     .Where(x => x.Key.InvariantEndsWith("PropertiesModel")))
        {
            if(schema.Properties is null)
            {
                continue;
            }
#if NET10_0_OR_GREATER
            schema.Properties["cacheKeys"] = new OpenApiSchema
            {
                // JsonSchemaType is the new enum for OpenAPI schema types
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