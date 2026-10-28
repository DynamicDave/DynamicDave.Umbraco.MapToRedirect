# DynamicDave.Umbraco.MapToRedirect

Turns a CSV of old and new URLs into redirects in Umbraco's own redirect list (Content > Redirect URL Management), so visitors and search engines on the URLs of an old website get a 301 to the new page. Made for moving an old site to Umbraco.

- **Import redirects dashboard** in the Content section: upload a CSV with the columns Old URL and New URL, see a test run of every row, then apply it.
- **Generate** (optional): reads the old site's sitemap and proposes the CSV, matching every old URL to a page of the current site.
- **History**: every import is kept with its results and can be undone in one action.
- Old URLs with a file extension (`/about/team.html`, `/page.php`, `/default.aspx`) redirect too, which Umbraco does not do by itself.

Redirect targets are always published Umbraco pages. Sites with several root nodes, domains and languages are supported.

## Install

    dotnet add package DynamicDave.Umbraco.MapToRedirect

Supported Umbraco versions: **17.x and 18.x** (net10.0), tested on 17.0.0, 17.7.1, 17.7.2 and 18.2.2. The backoffice UI is available in English, Dutch, German, French and Danish.

The package creates its tables with a package migration at startup. Sites with `Umbraco:CMS:Unattended:PackageMigrationsUnattended` set to `false` must run it from the backoffice (Packages section) first.

## How it works

At the top of the dashboard you pick the **site** (root node) and, for a multilingual site, the **language**. Generate, upload and apply all work on that site and language.

### Generate (optional)

Enter the old site's sitemap URL, or just its address: the package then reads the `Sitemap:` lines of its `robots.txt`, or else `/sitemap.xml`. Sitemap indexes and gzip are supported. Choose a method:

- **By URL** (fast): compares the old paths with the pages of the site.
- **URL and page title** (slower, the old site must be online): also fetches every old page that has no exact match and compares its `<title>` (without a site suffix such as " | Company") or first `<h1>` with the page names and titles.

Every old URL gets a match:

| Match | When |
|---|---|
| Exact | A page exists at the same path; no redirect needed (the test run skips the row). |
| Likely | The same page under another file name (`/contact.html` for `/contact`), exactly one page with the same last URL segment, or a strong page title match. |
| Guess | Several pages with the same last segment (the most similar parent path wins), or a similar last segment or page name. |
| None | Nothing found; New URL is left empty. |

The CSV (columns Old URL, New URL, Match, Note; UTF-8 with BOM) is downloaded when the job is done, sorted None, Guess, Likely, Exact, so what needs review comes first. Check and edit it before you upload it. Generating is a helper only: any CSV with the two columns can be uploaded, however it was made.

### Upload and test run

The CSV needs the columns `Old URL` and `New URL` (or `Oude URL` and `Nieuwe URL`, case-insensitive); without a header row the first two columns are used, other columns are ignored. Comma, semicolon and tab are detected; UTF-8 (with or without BOM), UTF-16 with BOM and Windows-1252 (Excel "CSV (semicolon)" on Dutch Windows) are read.

- **Old URL**: a path or a full URL. Only the path counts.
- **New URL**: a path on the selected site, a full URL on one of its domains for the selected language, or a document key (GUID). It must be a published page of the site in that language.

Uploading starts a test run right away; nothing changes yet. Every row gets one outcome:

| Outcome | When |
|---|---|
| Add | A new redirect. |
| Already present | The same redirect exists; nothing changes. |
| Conflict | The old URL already redirects to another page; the current target is shown. Skipped, unless **Overwrite existing redirects** is on. |
| Skip | A published page exists at the old URL (Umbraco would never redirect it), New URL is empty, or the old URL appears earlier in the file (the first row wins). |
| Error | New URL is not a published page of the site in this language, belongs to another site or language, or a URL is invalid. |

### Apply

**Apply** runs the test run as a background job. Each row is checked again first (target still published, no page at the old URL, existing redirect unchanged); a row whose state changed since the test run is not executed and is reported as "Changed since the test run". The results can be downloaded as a CSV report.

How old URLs are stored:

- As requested: lower-case, without query string, fragment and trailing slash, with file names and extensions kept (`/about/team.html`). Matching when generating ignores extensions and index file names; the stored redirect does not.
- As a route of the selected site and language, in the format Umbraco's own redirect tracking writes (the root node id and the path when the site has a domain, relative to a domain with a path prefix such as `klant.nl/en`).
- In the selected language. When the old URL already has a redirect for all languages (one Umbraco created for an invariant page), overwriting adds a redirect for the selected language only; the all-languages redirect is kept and still applies to the other languages.

### History and undo

The History tab lists every import with date, user, file, site, language, counts and state; open one to see its rows again. **Undo** (once per import) removes the redirects the import added and brings back the ones it replaced, but only while the redirect for that old URL is still the one the import wrote. Anything changed since the import is left alone and counted. The import stays in the history as Undone.

Only one job (generate, apply or undo) runs at a time per installation; the dashboard shows who runs it. A generated CSV or apply report stays available for one hour, so closing the tab does not lose it. A job that stops (for example because the site restarted) counts as aborted after 2 minutes; an import that stopped midway is listed as Partly applied, and what was applied can be undone.

### Old URLs with a file extension

Umbraco does not route request paths whose last segment has a file extension (`.html`, `.php`, `.aspx`): they end as a 404 without looking at the redirect list. The package adds a small middleware that only acts on such a 404, for GET and HEAD requests, outside Umbraco's reserved paths. It works out the site and language from the request host, looks the route up in Umbraco's redirect list the same way Umbraco does, and answers a 301 to the target page (a relative `Location` on the same domain, the query string passed on). Without a redirect the 404 stays as it is.

It costs one redirect lookup per 404 with a file extension. On a host that is not a domain of any site, it uses the first site without domains, as Umbraco serves such a site on any host. Switch it off with `RedirectFileExtensionUrls` (below).

## Configuration

All settings are optional:

```json
{
  "DynamicDave": {
    "MapToRedirect": {
      "AllowedUserGroups": [ "admin", "editor" ],
      "AllowPrivateNetworkTargets": false,
      "RedirectFileExtensionUrls": true
    }
  }
}
```

| Setting | Default | |
|---|---|---|
| `AllowedUserGroups` | `["admin"]` | Aliases of the user groups that may use the dashboard (case-insensitive). A configured list replaces the default. |
| `AllowPrivateNetworkTargets` | allowed only in `Development` | Whether generating may read sitemaps and pages on internal network addresses (see below). |
| `RedirectFileExtensionUrls` | `true` | `false` switches off the middleware for old URLs with a file extension. |

### Internal network targets

On a production server, generating refuses sitemap and page addresses on internal networks (localhost, `10.x`, `172.16-31.x`, `192.168.x`, link-local such as the cloud metadata address `169.254.169.254`, and their IPv6 equivalents). The check runs on the resolved IP address and again after every redirect, so host names that point at internal addresses are refused too.

In the `Development` environment internal addresses are allowed, because a local copy of the old site usually runs on `localhost`. Override this in either direction with `AllowPrivateNetworkTargets`.

## Permissions

- The dashboard and its API need access to the Content section and membership of one of the `AllowedUserGroups`. For other users the dashboard is not shown and the API answers 403.
- A generated CSV or apply report can only be downloaded by the user who started the job.

## Limits

- Sitemaps: at most 50 sitemap files, 50,000 URLs, 50 MB per file (decompressed), 30 seconds per request and 5 redirects. Reaching a limit stops reading; what was read so far is used and the limit is reported.
- Page titles: 4 pages at a time, 15 seconds per page.
- CSV upload: at most 10 MB and 50,000 rows. A generated CSV for a very large site can be larger than 10 MB; split it and upload the parts one after another.
- A test run can be applied for one hour; after that, upload the CSV again.
- Redirects only: targets are published Umbraco pages, never external URLs; no wildcard or regex redirects. Edit or delete single redirects in Umbraco's Redirect URL Management.
- Umbraco only redirects when no published page exists at the requested path, and ignores the query string when looking up a redirect.

## Load balancing

A job runs on the server that received the request. Its state is stored in the database (`umbracoKeyValue`) and claimed with compare-and-set, so every server sees it and only one job runs at a time. Test runs and job results are stored in the database too, so any server can apply a test run or serve a download. Redirects go through Umbraco's redirect service and work on every server.

## Uninstall

Removing the package leaves its data in the database: the tables `dynamicDaveMapToRedirectImport`, `dynamicDaveMapToRedirectImportRow` and `dynamicDaveMapToRedirectTemp`, and the job state and package migration state in `umbracoKeyValue`. The redirects it created are ordinary entries in Umbraco's redirect list and stay; undo the imports first if you want them gone. To remove the data:

```sql
DROP TABLE dynamicDaveMapToRedirectImportRow;
DROP TABLE dynamicDaveMapToRedirectImport;
DROP TABLE dynamicDaveMapToRedirectTemp;
DELETE FROM umbracoKeyValue WHERE [key] = 'DynamicDave.MapToRedirect.Job' OR [key] = 'Umbraco.Core.Upgrader.State+DynamicDave.Umbraco.MapToRedirect';
```

## License

MIT
