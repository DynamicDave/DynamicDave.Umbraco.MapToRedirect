using System.Reflection;
using DynamicDave.Umbraco.MapToRedirect.Controllers;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class ControllerLimitsTests
{
    // The CSV limit is 10 MB (CsvFormat): the request limits sit just above it, so a larger CSV still reaches the
    // reader and gets the TooLarge code instead of a server-level 413 or form error.
    [Fact]
    public void Upload_allows_requests_just_above_the_csv_limit()
    {
        var action = typeof(MapToRedirectController).GetMethod(nameof(MapToRedirectController.CreatePlan))!;

        var size = (IRequestSizeLimitMetadata)action.GetCustomAttribute<RequestSizeLimitAttribute>()!;
        var form = action.GetCustomAttribute<RequestFormLimitsAttribute>()!;

        Assert.Equal(11_000_000, size.MaxRequestBodySize);
        Assert.Equal(11_000_000, form.MultipartBodyLengthLimit);
    }
}
