# Map to Redirect

Moving an old website to Umbraco? Map to Redirect turns a CSV of old and new URLs into redirects in Umbraco's own redirect list, so visitors and search engines on old URLs get a 301 to the new page.

## Features

- Upload a CSV with **Old URL** and **New URL** and see a **test run** of every row before anything changes
- **Generate** the CSV from the old site's sitemap, matched to your pages by URL or by URL and page title
- **History** of every import, with **undo** in one action
- Old URLs with a file extension (`.html`, `.php`, `.aspx`) redirect too
- Multiple sites, domains and languages
- Access limited to administrators by default, configurable per user group

## Compatibility

- Umbraco **17 and 18** (.NET 10)
- Backoffice UI in English, Dutch, German, French and Danish
- Free and open source (MIT)

## Getting started

```
dotnet add package DynamicDave.Umbraco.MapToRedirect
```

Open **Map to Redirect** in the Content section. Full documentation and configuration: [README on GitHub](https://github.com/DynamicDave/DynamicDave.Umbraco.MapToRedirect/blob/main/src/DynamicDave.Umbraco.MapToRedirect/README.md).
