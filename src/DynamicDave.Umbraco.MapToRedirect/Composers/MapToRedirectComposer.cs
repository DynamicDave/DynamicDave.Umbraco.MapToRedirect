using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace DynamicDave.Umbraco.MapToRedirect.Composers;

public class MapToRedirectComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<MapToRedirectOptions>().Configure(options => MapToRedirectOptions.Bind(builder.Config.GetSection(Constants.OptionsSection), options));
    }
}
