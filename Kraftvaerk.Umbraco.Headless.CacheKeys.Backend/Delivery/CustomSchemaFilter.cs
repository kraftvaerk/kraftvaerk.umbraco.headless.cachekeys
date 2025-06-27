using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Delivery;

public class CustomSchemaFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach ((string schemaId, OpenApiSchema schema) in context.SchemaRepository.Schemas.Where(x => x.Key.InvariantEndsWith("PropertiesModel")))
        {
            schema.Properties["cacheKeys"] = new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema
                {
                    Type = "string",
                    Format = "uuid"
                }
            };
        }
    }
}

