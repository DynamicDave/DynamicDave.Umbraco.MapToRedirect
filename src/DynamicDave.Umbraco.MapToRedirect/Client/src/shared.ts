import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { ApplyOutcome, JobStateModel, PlanOutcome } from './api/index.js';

export const PAGE_SIZE = 25;
export const POLL_MS = 2000;
export const MAX_POLL_MS = 10_000;

/** Fired by a tab when it started a job or learned about a running one (409); the dashboard polls it. */
export const JOB_CHANGED_EVENT = 'dd-map-to-redirect-job-changed';

/** Fired by the upload tab when an apply job finished, so the history reloads. */
export const IMPORTS_CHANGED_EVENT = 'dd-map-to-redirect-imports-changed';

export type JobChangedEvent = CustomEvent<JobStateModel>;

export const jobChanged = (job: JobStateModel) =>
  new CustomEvent<JobStateModel>(JOB_CHANGED_EVENT, { detail: job, bubbles: true, composed: true });

/** A JobStateModel, as opposed to an empty body (204) or an error body. */
export const isJob = (value: unknown): value is JobStateModel =>
  typeof value === 'object' && value !== null && 'id' in value && 'status' in value;

/** The stable error code of an API error body (`{ error: "planExpired" }`); undefined for ProblemDetails or empty bodies. */
export function errorCode(error: unknown): string | undefined {
  if (typeof error !== 'object' || error === null || !('error' in error)) return undefined;
  const code = (error as { error: unknown }).error;
  return typeof code === 'string' && code.length > 0 ? code : undefined;
}

const ERROR_TERMS: Record<string, string> = {
  // API error codes (ApiErrors)
  siteNotFound: 'ddMapToRedirect_errorSiteNotFound',
  cultureNotFound: 'ddMapToRedirect_errorCultureNotFound',
  sitemapUrlInvalid: 'ddMapToRedirect_errorSitemapUrlInvalid',
  planExpired: 'ddMapToRedirect_planExpired',
  importNotFound: 'ddMapToRedirect_errorImportNotFound',
  alreadyUndone: 'ddMapToRedirect_errorAlreadyUndone',
  resultNotFound: 'ddMapToRedirect_errorResultNotFound',
  noJobRunning: 'ddMapToRedirect_errorNoJobRunning',
  // Job error codes
  SiteNotFound: 'ddMapToRedirect_errorSiteNotFound',
  Unexpected: 'ddMapToRedirect_errorUnexpected',
  Cancelled: 'ddMapToRedirect_errorCancelled',
  // SitemapError
  Unreachable: 'ddMapToRedirect_sitemapErrorUnreachable',
  NotASitemap: 'ddMapToRedirect_sitemapErrorNotASitemap',
  InternalAddress: 'ddMapToRedirect_sitemapErrorInternalAddress',
  // CsvReadError
  Unreadable: 'ddMapToRedirect_csvErrorUnreadable',
  MissingColumns: 'ddMapToRedirect_csvErrorMissingColumns',
  TooLarge: 'ddMapToRedirect_csvErrorTooLarge',
  TooManyRows: 'ddMapToRedirect_csvErrorTooManyRows',
};

/** The localized text of an error code; "could not load" for unknown codes and ProblemDetails. */
export const errorTerm = (code: string | null | undefined) =>
  (code && ERROR_TERMS[code]) || 'ddMapToRedirect_loadFailed';

export const planOutcomeTerm = (outcome: PlanOutcome) => `ddMapToRedirect_outcome${outcome}`;
export const applyOutcomeTerm = (outcome: ApplyOutcome) => `ddMapToRedirect_applied${outcome}`;
export const matchKindTerm = (kind: string) => `ddMapToRedirect_match${kind}`;

export function planOutcomeColor(outcome: PlanOutcome): 'positive' | 'warning' | 'danger' | 'default' {
  if (outcome === 'Add') return 'positive';
  if (outcome === 'Conflict') return 'warning';
  return outcome === 'Error' ? 'danger' : 'default';
}

export function applyOutcomeColor(outcome: ApplyOutcome): 'positive' | 'warning' | 'danger' | 'default' {
  if (outcome === 'Added' || outcome === 'Replaced') return 'positive';
  if (outcome === 'Changed') return 'warning';
  return outcome === 'Failed' ? 'danger' : 'default';
}

/** Parses a job summary (JSON); undefined when it is missing or not JSON. */
export function parseSummary<T>(summary: string | null | undefined): T | undefined {
  if (!summary) return undefined;
  try {
    return JSON.parse(summary) as T;
  } catch {
    return undefined;
  }
}

export interface GenerateSummary {
  urls: number;
  exact: number;
  likely: number;
  guess: number;
  none: number;
  limits: string[];
  error?: string | null;
}

export interface ApplySummary {
  added: number;
  replaced: number;
  alreadyPresent: number;
  skipped: number;
  failed: number;
  changed: number;
}

export interface UndoSummary {
  removed: number;
  restored: number;
  leftAlone: number;
}

/** Whether the job's result (generated CSV or apply report) can be downloaded by the current user (the API gives 403 otherwise). */
export const canDownload = (job: JobStateModel | undefined, userKey: string | undefined) =>
  job !== undefined && job.status === 'Completed' && !!job.resultKey && !!userKey && job.userKey === userKey;

/** The file name from a Content-Disposition header, or the fallback. */
export function fileNameFrom(response: Response | undefined, fallback: string) {
  const header = response?.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      // Fall through to the plain file name.
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

/** Saves a blob through a temporary link; the object URL is revoked after the browser has started the download. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** The progress line of a running job, e.g. "Matching 312 / 1240". */
export function phaseText(localize: UmbLocalizationController, job: JobStateModel) {
  switch (job.phase) {
    case 'readingSitemap':
      return localize.term('ddMapToRedirect_phaseReadingSitemap', job.done);
    case 'matching':
      return localize.term('ddMapToRedirect_phaseMatching', job.done, job.total);
    case 'fetchingTitles':
      return localize.term('ddMapToRedirect_phaseFetchingTitles', job.done, job.total);
    case 'writingCsv':
      return localize.term('ddMapToRedirect_phaseWritingCsv');
    case 'applying':
      return localize.term('ddMapToRedirect_phaseApplying', job.done, job.total);
    case 'undoing':
      return localize.term('ddMapToRedirect_phaseUndoing', job.done, job.total);
    default:
      return localize.term('ddMapToRedirect_phaseStarting');
  }
}

/** Progress text and bar of a running job. */
export function progressTemplate(localize: UmbLocalizationController, job: JobStateModel) {
  const progress = job.total > 0 ? Math.round((job.done / job.total) * 100) : 0;
  return html`
    <div class="progress">
      <span>${phaseText(localize, job)}</span>
      ${job.total > 0 ? html`<uui-loader-bar progress=${progress}></uui-loader-bar>` : html`<uui-loader-bar></uui-loader-bar>`}
    </div>
  `;
}

/** The status of a job that has ended without success: the localized error, or "aborted". */
export function failureTemplate(localize: UmbLocalizationController, job: JobStateModel) {
  if (job.status === 'Aborted') {
    return html`<p class="error"><uui-tag color="danger">${localize.term('ddMapToRedirect_statusAborted')}</uui-tag></p>`;
  }
  if (job.status !== 'Failed') return nothing;
  return html`<p class="error">
    <uui-tag color="danger">${localize.term('ddMapToRedirect_statusFailed')}</uui-tag>
    ${localize.term(errorTerm(job.error))}
  </p>`;
}

export const sharedStyles = css`
  :host {
    display: block;
  }
  .hint {
    color: var(--uui-color-text-alt);
    font-size: var(--uui-type-small-size);
    margin: var(--uui-size-space-1) 0 0;
  }
  .progress {
    display: flex;
    flex-direction: column;
    gap: var(--uui-size-space-2);
    margin: var(--uui-size-space-4) 0;
  }
  .error {
    color: var(--uui-color-danger);
  }
  .actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-4);
    margin-top: var(--uui-size-space-4);
  }
  .tags {
    display: flex;
    flex-wrap: wrap;
    gap: var(--uui-size-space-2);
    margin: var(--uui-size-space-4) 0;
  }
  .filter {
    padding: 0;
    border: 0;
    background: none;
    font: inherit;
    cursor: pointer;
    border-radius: var(--uui-border-radius);
  }
  .filter.active uui-tag {
    box-shadow: 0 0 0 2px var(--uui-color-selected);
  }
  .url {
    overflow-wrap: anywhere;
    min-width: 10ch;
  }
  .muted {
    color: var(--uui-color-text-alt);
  }
  .table-wrap uui-table {
    width: max-content;
    min-width: 100%;
  }
  .table-wrap {
    overflow-x: auto;
  }
  uui-table-cell uui-tag {
    white-space: nowrap;
  }
  uui-pagination {
    display: block;
    margin-top: var(--uui-size-space-4);
  }
`;

/** Codes of import row messages (RowMessages on the server); each has a "ddMapToRedirect_rowMessage<Code>" term. */
const ROW_MESSAGE_CODES = new Set([
  'PageExistsAtPath',
  'EmptyNewUrl',
  'SameAsTarget',
  'Duplicate',
  'InvalidOldUrl',
  'InvalidNewUrl',
  'NotPublished',
  'OtherSite',
  'OutsideLanguagePath',
  'Skipped',
  'Error',
  'SiteRemoved',
  'TargetUnpublished',
  'PageAtOldUrl',
  'ConflictKept',
  'OverridesAllLanguages',
  'RedirectRemoved',
  'RedirectChanged',
]);

/** The localized message of an import row; the stored English text for an unknown code. */
export function rowMessage(localize: UmbLocalizationController, row: { message?: string | null; messageCode?: string | null }) {
  const code = row.messageCode;
  return code && ROW_MESSAGE_CODES.has(code) ? localize.term(`ddMapToRedirect_rowMessage${code}`) : (row.message ?? '');
}
