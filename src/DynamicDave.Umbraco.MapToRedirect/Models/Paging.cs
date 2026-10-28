namespace DynamicDave.Umbraco.MapToRedirect.Models;

/// <summary>Paging limits shared by the API endpoints that return pages.</summary>
public static class Paging
{
    public const int MaxTake = 100;

    /// <summary>The size of the first page of a test run (the UI's page size).</summary>
    public const int FirstPageSize = 25;

    public static bool IsValid(int skip, int take) => skip >= 0 && take >= 1 && take <= MaxTake;
}
