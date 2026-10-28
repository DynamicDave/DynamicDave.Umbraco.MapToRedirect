namespace TestSite.Seeding;

/// <summary>
/// Development only: answers every request for host 127.0.0.1 from the OldSite folder, so the fake old website lives at root
/// paths ("/contact") like a real one. The files are deliberately not in wwwroot, so they are not also served on the new
/// sites' own hosts. Other hosts are untouched.
/// </summary>
public class OldSiteMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private readonly string _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "OldSite"));

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Host.Host != "127.0.0.1")
        {
            await next(context);
            return;
        }

        var file = Resolve(context.Request.Path.Value ?? "/");
        if (file is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(file, context.RequestAborted);
        context.Response.ContentType = ContentTypeOf(file, bytes);
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private string? Resolve(string path)
    {
        var relative = Uri.UnescapeDataString(path).Trim('/');
        string[] candidates = relative.Length == 0
            ? ["index.html"]
            : [relative, relative + ".html", relative + "/index.html"];
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(Path.Combine(_root, candidate));
            if (full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private static string ContentTypeOf(string file, byte[] bytes) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".xml" => "text/xml",
        ".gz" => "application/x-gzip",
        ".txt" => "text/plain; charset=utf-8",
        ".html" => System.Text.Encoding.ASCII.GetString(bytes).Contains("windows-1252", StringComparison.Ordinal)
            ? "text/html; charset=windows-1252"
            : "text/html; charset=utf-8",
        _ => "application/octet-stream",
    };
}
