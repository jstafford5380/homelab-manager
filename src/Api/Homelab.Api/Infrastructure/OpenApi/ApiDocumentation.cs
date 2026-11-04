using FastEndpoints.OpenApi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
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
            options.AddAuth(ApiAccess.KeyScheme, new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "API key issued in Settings → API keys. Use the full hmk_ key. Administrative operations require an owner browser session." });
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
        // Run after FastEndpoints' document enrichment, which otherwise assigns every registered scheme.
        services.PostConfigure<OpenApiOptions>("v1", options => options.AddDocumentTransformer((document, context, ct) =>
        {
            var descriptions = context.ApplicationServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
            foreach (var description in descriptions.ApiDescriptionGroups.Items.SelectMany(group => group.Items))
            {
                if (description.HttpMethod is null || !document.Paths.TryGetValue("/" + description.RelativePath, out var path) ||
                    path.Operations is null || !path.Operations.TryGetValue(new HttpMethod(description.HttpMethod), out var operation)) continue;
                var metadata = description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any()) { operation.Security = []; continue; }
                operation.Security = [new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(CookieAuthenticationDefaults.AuthenticationScheme, document)] = []
                }];
                if (metadata.OfType<IAuthorizeData>().Any(data => data.Policy is ApiAccess.Read or ApiAccess.Write))
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(ApiAccess.KeyScheme, document)] = []
                    });
            }
            return Task.CompletedTask;
        }));
        return services;
    }
}
