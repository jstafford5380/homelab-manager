using FastEndpoints.OpenApi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Homelab.Api.Infrastructure.OpenApi;
public static class ApiDocumentation
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.OpenApiDocument(options =>
        {
            options.DocumentName = "v1";
            options.Title = "Homelab Manager API";
            options.Version = "v1";
            options.AutoTagPathSegmentIndex = 0;
            options.EnableJWTBearerAuth = false;
            options.AddAuth(CookieAuthenticationDefaults.AuthenticationScheme, new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = "homelab.session", Description = "Sign in through /api/v1/login. The browser sends the HttpOnly session cookie." });
            options.ConfigureOpenApi = openApi =>
            {
                // NSwag clients and Scalar consume this single Microsoft OpenAPI document.
                openApi.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                openApi.AddOperationTransformer((operation, context, ct) =>
                {
                    if (context.Description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true && context.Description.HttpMethod != "GET")
                    {
                        operation.Parameters ??= [];
                        operation.Parameters.Add(new OpenApiParameter { Name = "X-Homelab-Request", In = ParameterLocation.Header, Required = true, Description = "Required mutation guard; send the literal value 1.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create("1"), Enum = [JsonValue.Create("1")] } });
                    }

                    return Task.CompletedTask;
                });
            };
        });
        return services;
    }
}
