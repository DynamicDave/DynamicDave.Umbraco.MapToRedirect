import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';

/**
 * Pre-configure the generated client by copying the Umbraco HTTP client's config.
 *
 * Called automatically by the generated client during initialization (via runtimeConfigPath).
 * Inherits baseUrl, credentials, auth, and headers from umbHttpClient — which is already
 * configured by the backoffice before extensions load.
 *
 * Typed loosely so the project compiles before `npm run generate-client` has produced src/api.
 *
 * @see https://heyapi.dev/openapi-ts/clients/fetch#configuration
 */
export const createClientConfig = <T extends object>(config: T): T => ({
	...config,
	...umbHttpClient.getConfig(),
});
