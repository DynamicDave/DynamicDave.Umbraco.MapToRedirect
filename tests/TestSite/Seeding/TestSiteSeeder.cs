using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace TestSite.Seeding;

/// <summary>
/// Seeds three sites for the manual check of the map-to-redirect tool: "Klant NL" (klant-nl.localhost, nl-NL),
/// "Klant DE" (klant-de.localhost, en-US) and the bilingual "Bilingual" (bi-en.localhost en-US, bi-nl.localhost nl-NL).
/// Every site has the pages over-ons/ons-team, nieuws/zomer-actie, contact, producten/fiets and one unpublished page "concept".
/// </summary>
public class TestSiteSeeder(
    IRuntimeState runtimeState,
    IContentService contentService,
    IContentTypeService contentTypeService,
    ILanguageService languageService,
    ITemplateService templateService,
    IDataTypeService dataTypeService,
    IDomainService domainService,
    IShortStringHelper shortStringHelper,
    ILogger<TestSiteSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private static readonly Guid SuperUser = Constants.Security.SuperUserKey;

    private const string TemplateMarkup =
        "@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage\n@{ Layout = null; }\n<!DOCTYPE html>\n<html>\n<head><title>@Model.Name</title></head>\n<body><h1>@Model.Name</h1></body>\n</html>\n";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || contentService.GetRootContent().Any())
        {
            return; // not ready, or already seeded
        }

        logger.LogInformation("Seeding map-to-redirect test sites");
        await EnsureDutchAsync();
        var (testPage, variantPage) = await CreateContentTypesAsync();

        var klantNl = CreateSite("Klant NL", testPage, variant: false);
        await SetDomainsAsync(klantNl, "en-US", ("klant-nl.localhost:44416", "nl-NL"));

        var klantDe = CreateSite("Klant DE", testPage, variant: false);
        await SetDomainsAsync(klantDe, "en-US", ("klant-de.localhost:44416", "en-US"));

        var bilingual = CreateSite("Bilingual", variantPage, variant: true);
        await SetDomainsAsync(bilingual, "en-US", ("bi-en.localhost:44416", "en-US"), ("bi-nl.localhost:44416", "nl-NL"));
    }

    private async Task EnsureDutchAsync()
    {
        if (await languageService.GetAsync("nl-NL") is null)
        {
            LogIfFailed("Create language nl-NL", await languageService.CreateAsync(new Language("nl-NL", "Dutch (Netherlands)"), SuperUser));
        }
    }

    private async Task<(IContentType TestPage, IContentType VariantPage)> CreateContentTypesAsync()
    {
        var template = await templateService.GetAsync("testPage");
        if (template is null)
        {
            var created = await templateService.CreateAsync("Test page", "testPage", TemplateMarkup, SuperUser);
            LogIfFailed("Create template", created);
            template = created.Result;
        }

        var testPage = await CreatePageTypeAsync("testPage", "Test page", ContentVariation.Nothing, template);
        var variantPage = await CreatePageTypeAsync("testVariantPage", "Test variant page", ContentVariation.Culture, template);
        return (testPage, variantPage);
    }

    private async Task<IContentType> CreatePageTypeAsync(string alias, string name, ContentVariation variation, ITemplate? template)
    {
        var type = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = alias,
            Name = name,
            AllowedAsRoot = true,
            Variations = variation,
        };

        var textstring = await dataTypeService.GetAsync(Constants.DataTypes.Guids.TextstringGuid)
            ?? throw new InvalidOperationException("Default Textstring data type not found");
        type.AddPropertyType(
            new PropertyType(shortStringHelper, textstring, "title") { Name = "Title", Variations = variation },
            "content",
            "Content");
        type.AllowedContentTypes = [new ContentTypeSort(type.Key, 0, type.Alias)];
        if (template is not null)
        {
            type.AllowedTemplates = [template];
            type.SetDefaultTemplate(template);
        }

        LogIfFailed($"Create content type {alias}", await contentTypeService.CreateAsync(type, SuperUser));
        return type;
    }

    private IContent CreateSite(string name, IContentType type, bool variant)
    {
        var root = CreatePage(name, type, variant, parent: null);
        Publish(root);

        var about = CreatePage("Over ons", type, variant, root);
        Publish(about);
        Publish(CreatePage("Ons team", type, variant, about));

        var news = CreatePage("Nieuws", type, variant, root);
        Publish(news);
        Publish(CreatePage("Zomer actie", type, variant, news));

        Publish(CreatePage("Contact", type, variant, root));

        var products = CreatePage("Producten", type, variant, root);
        Publish(products);
        Publish(CreatePage("Fiets", type, variant, products));

        CreatePage("Concept", type, variant, root); // stays unpublished
        return root;
    }

    private IContent CreatePage(string name, IContentType type, bool variant, IContent? parent)
    {
        var content = contentService.Create(name, parent?.Id ?? Constants.System.Root, type);
        if (variant)
        {
            content.SetCultureName(name, "en-US");
            content.SetCultureName(name, "nl-NL");
            content.SetValue("title", name, "en-US");
            content.SetValue("title", name, "nl-NL");
        }
        else
        {
            content.SetValue("title", name);
        }

        contentService.Save(content);
        return content;
    }

    private void Publish(IContent content)
    {
        var result = contentService.Publish(content, ["*"]);
        if (!result.Success)
        {
            logger.LogWarning("Publishing {Name} failed: {Result}", content.Name, result.Result);
        }
    }

    // UpdateDomainsAsync exists on Umbraco 17 and 18 (GetAll / GetAssignedDomains were replaced by async variants in 17).
    private async Task SetDomainsAsync(IContent site, string defaultCulture, params (string Domain, string Culture)[] domains)
    {
        var model = new DomainsUpdateModel
        {
            DefaultIsoCode = defaultCulture,
            Domains = domains.Select(d => new DomainModel { DomainName = d.Domain, IsoCode = d.Culture }).ToArray(),
        };
        LogIfFailed($"Assign domains of {site.Name}", await domainService.UpdateDomainsAsync(site.Key, model));
    }

    private void LogIfFailed(string action, dynamic attempt)
    {
        if (!attempt.Success)
        {
            logger.LogWarning("{Action} failed: {Status}", action, (object?)attempt.Status);
        }
    }
}
