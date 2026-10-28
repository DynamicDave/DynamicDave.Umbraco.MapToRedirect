import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import type { UmbConditionConfigBase, UmbConditionControllerArguments, UmbExtensionCondition } from '@umbraco-cms/backoffice/extension-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { MapToRedirectService } from './api/index.js';

let access: Promise<boolean> | undefined;

/** Asks the API once per session whether the current user may import redirects; a failed request is asked again next time. */
function hasAccess(): Promise<boolean> {
  access ??= MapToRedirectService.getAccess({ throwOnError: false })
    .then(({ data }) => {
      if (!data) throw new Error('No access response');
      return data.hasAccess === true;
    })
    .catch(() => {
      access = undefined;
      return false;
    });
  return access;
}

/** Permits the dashboard only for users in one of the configured user groups (DynamicDave:MapToRedirect:AllowedUserGroups). */
export class DdMapToRedirectAccessCondition extends UmbConditionBase<UmbConditionConfigBase> implements UmbExtensionCondition {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<UmbConditionConfigBase>) {
    super(host, args);
    void hasAccess().then((permitted) => {
      this.permitted = permitted;
    });
  }
}

export { DdMapToRedirectAccessCondition as api };
