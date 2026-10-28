using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace TestSite.Seeding;

public class TestSiteComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Development only: never seed elsewhere.
        var environment = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(IHostEnvironment))?.ImplementationInstance as IHostEnvironment;
        if (environment is null || !environment.IsDevelopment())
        {
            return;
        }

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, TestSiteSeeder>();
    }
}
