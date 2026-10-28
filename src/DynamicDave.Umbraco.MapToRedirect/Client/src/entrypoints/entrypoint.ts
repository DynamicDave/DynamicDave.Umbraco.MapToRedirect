import type {
  UmbEntryPointOnInit,
  UmbEntryPointOnUnload,
} from "@umbraco-cms/backoffice/extension-api";

// TODO(api client): once `npm run generate-client` has produced src/api, wire the generated client into the
// backoffice auth context here (UMB_AUTH_CONTEXT -> authContext.configureClient(client), with the
// getOpenApiConfiguration() fallback for Umbraco 17.0), exactly as the other DynamicDave packages do.
export const onInit: UmbEntryPointOnInit = async (_host, _extensionRegistry) => {
};

export const onUnload: UmbEntryPointOnUnload = (_host, _extensionRegistry) => {
};
