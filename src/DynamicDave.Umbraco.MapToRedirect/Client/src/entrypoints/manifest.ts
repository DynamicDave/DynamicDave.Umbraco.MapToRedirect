export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Dynamic Dave Umbraco Map to Redirect Entrypoint",
    alias: "DynamicDave.Umbraco.MapToRedirect.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
