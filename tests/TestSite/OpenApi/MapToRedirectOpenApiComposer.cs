using Asp.Versioning;
using DynamicDave.Umbraco.MapToRedirect;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace TestSite.OpenApi;

// Registers the package's Swagger document, used by `npm run generate-client` to generate the TypeScript client.
// Lives in the TestSite rather than the package: Umbraco 17 uses Swashbuckle, Umbraco 18 replaced it with
// Microsoft.AspNetCore.OpenApi, and the package has to run on both. Only compiled when the TestSite runs on
// Umbraco 17 (see TestSite.csproj), so generate the client on Umbraco 17.
public class MapToRedirectOpenApiComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IOperationIdHandler, CustomOperationHandler>();

        builder.Services.Configure<SwaggerGenOptions>(opt =>
        {
            // Related documentation:
            // https://docs.umbraco.com/umbraco-cms/tutorials/creating-a-backoffice-api
            // https://docs.umbraco.com/umbraco-cms/tutorials/creating-a-backoffice-api/adding-a-custom-swagger-document
            opt.SwaggerDoc(Constants.ApiName, new OpenApiInfo
            {
                Title = "Dynamic Dave Umbraco Map to Redirect Backoffice API",
                Version = "1.0",
            });

            // Enable Umbraco backoffice authentication for the "dynamicdave-maptoredirect" Swagger document
            opt.OperationFilter<MapToRedirectOperationSecurityFilter>();
        });
    }

    public class MapToRedirectOperationSecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
    {
        protected override string ApiName => Constants.ApiName;
    }

    // Operation IDs are the action names, so the generated TypeScript client has short method names.
    // https://docs.umbraco.com/umbraco-cms/tutorials/creating-a-backoffice-api/umbraco-schema-and-operation-ids#operation-ids
    public class CustomOperationHandler(IOptions<ApiVersioningOptions> apiVersioningOptions) : OperationIdHandler(apiVersioningOptions)
    {
        protected override bool CanHandle(ApiDescription apiDescription, ControllerActionDescriptor controllerActionDescriptor)
            => controllerActionDescriptor.ControllerTypeInfo.Namespace?.StartsWith("DynamicDave.Umbraco.MapToRedirect.Controllers", StringComparison.InvariantCultureIgnoreCase) is true;

        public override string Handle(ApiDescription apiDescription) => $"{apiDescription.ActionDescriptor.RouteValues["action"]}";
    }
}
