import { css, html, customElement, property, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UUIInputEvent, UUIRadioGroupEvent } from '@umbraco-cms/backoffice/external/uui';
import { MapToRedirectService } from './api/index.js';
import type { JobStateModel, MatchMethod, SiteModel } from './api/index.js';
import {
  canDownload,
  errorCode,
  errorTerm,
  failureTemplate,
  fileNameFrom,
  isJob,
  dismissJob,
  dismissTemplate,
  isBannerDismissed,
  jobChanged,
  matchKindTerm,
  parseSummary,
  progressTemplate,
  saveBlob,
  sharedStyles,
} from './shared.js';
import type { GenerateSummary } from './shared.js';

/** Generate tab: reads the old site's sitemap and downloads a CSV with a suggested new URL per old URL (spec §4.3). */
@customElement('dd-map-to-redirect-generate-tab')
export class DdMapToRedirectGenerateTabElement extends UmbLitElement {
  @property({ attribute: false }) site?: SiteModel;
  @property({ attribute: false }) culture?: string;
  @property({ attribute: false }) job?: JobStateModel;
  @property({ attribute: false }) userKey?: string;

  @state() private _sitemapUrl = '';
  @state() private _method: MatchMethod = 'Url';
  @state() private _starting = false;
  @state() private _message?: string;
  @state() private _downloading = false;

  /** The id of a Generate job seen running here: its CSV is downloaded automatically when it completes. */
  #watching?: string;
  #downloaded = new Set<string>();

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    if (!changed.has('job') && !changed.has('userKey')) return;
    const job = this.job;
    if (job?.kind !== 'Generate' || job.userKey !== this.userKey || !this.userKey) return;
    if (job.status === 'Running') {
      this.#watching = job.id;
    } else if (job.id === this.#watching) {
      this.#watching = undefined;
      if (canDownload(job, this.userKey) && !this.#downloaded.has(job.id)) void this.#download();
    }
  }

  async #generate() {
    if (!this.site || !this.culture || !this._sitemapUrl.trim()) return;
    this._starting = true;
    this._message = undefined;
    try {
      const { data, error, response } = await MapToRedirectService.generate({
        throwOnError: false,
        body: { sitemapUrl: this._sitemapUrl.trim(), method: this._method, rootKey: this.site.rootKey, culture: this.culture },
      });
      if (isJob(data)) {
        this.#watching = data.id;
        this.dispatchEvent(jobChanged(data));
      } else if (response?.status === 409) {
        if (isJob(error)) this.dispatchEvent(jobChanged(error));
        else this._message = this.localize.term('ddMapToRedirect_jobRunning');
      } else {
        this._message = this.localize.term(errorTerm(errorCode(error)));
      }
    } catch {
      this._message = this.localize.term('ddMapToRedirect_loadFailed');
    }
    this._starting = false;
  }

  async #download() {
    const job = this.job;
    if (!job) return;
    this.#downloaded.add(job.id);
    this._downloading = true;
    let blob: Blob | undefined;
    let response: Response | undefined;
    try {
      const result = await MapToRedirectService.getJobResult({ throwOnError: false, parseAs: 'blob' });
      blob = result.data instanceof Blob ? result.data : undefined;
      response = result.response;
    } catch {
      blob = undefined;
    }
    this._downloading = false;
    if (!blob) {
      this._message = this.localize.term('ddMapToRedirect_downloadFailed');
      return;
    }
    saveBlob(blob, fileNameFrom(response, 'redirects.csv'));
  }

  override render() {
    const job = this.job?.kind === 'Generate' ? this.job : undefined;
    const running = this.job?.status === 'Running';
    const mine = job !== undefined && job.userKey === this.userKey;
    return html`
      <uui-form-layout-item>
        <uui-label slot="label" for="sitemap" required>${this.localize.term('ddMapToRedirect_sitemapUrl')}</uui-label>
        <uui-input
          id="sitemap"
          type="url"
          placeholder="https://www.example.com/"
          label=${this.localize.term('ddMapToRedirect_sitemapUrl')}
          .value=${this._sitemapUrl}
          @input=${(e: UUIInputEvent) => (this._sitemapUrl = String(e.target.value))}></uui-input>
      </uui-form-layout-item>
      <uui-radio-group
        .value=${this._method}
        @change=${(e: UUIRadioGroupEvent) => (this._method = e.target.value as MatchMethod)}>
        <uui-radio value="Url" label=${this.localize.term('ddMapToRedirect_methodUrl')}></uui-radio>
        <p class="hint method-hint">${this.localize.term('ddMapToRedirect_methodUrlHint')}</p>
        <uui-radio value="UrlAndTitle" label=${this.localize.term('ddMapToRedirect_methodTitle')}></uui-radio>
        <p class="hint method-hint">${this.localize.term('ddMapToRedirect_methodTitleHint')}</p>
      </uui-radio-group>
      <div class="actions">
        <uui-button
          look="primary"
          label=${this.localize.term('ddMapToRedirect_generate')}
          ?disabled=${running || !this.site || !this.culture || !this._sitemapUrl.trim()}
          .state=${this._starting ? 'waiting' : undefined}
          @click=${this.#generate}></uui-button>
        ${canDownload(job, this.userKey)
          ? html`<uui-button
              look="outline"
              label=${this.localize.term('ddMapToRedirect_downloadCsv')}
              .state=${this._downloading ? 'waiting' : undefined}
              @click=${this.#download}></uui-button>`
          : nothing}
      </div>
      ${this._message ? html`<p class="error">${this._message}</p>` : nothing}
      ${job && mine ? this.#renderJob(job) : nothing}
    `;
  }

  #renderJob(job: JobStateModel) {
    if (job.status === 'Running') return progressTemplate(this.localize, job);
    if (isBannerDismissed(job.id)) return nothing;
    const summary = parseSummary<GenerateSummary>(job.summary);
    return html`
      ${dismissTemplate(this.localize, () => {
        dismissJob(job.id);
        this.requestUpdate();
      })}
      ${failureTemplate(this.localize, job)}
      ${job.status === 'Completed' && summary
        ? html`<p>${this.localize.term('ddMapToRedirect_generateSummary', summary.urls)}</p>
            <div class="tags">
              ${(['Exact', 'Likely', 'Guess', 'None'] as const).map(
                (kind) => html`<uui-tag look="secondary">${this.localize.term(matchKindTerm(kind))}: ${summary[kind.toLowerCase() as 'exact']}</uui-tag>`,
              )}
            </div>`
        : nothing}
      ${(summary?.limits ?? []).map((limit) => html`<p><uui-tag color="warning">${this.localize.term(`ddMapToRedirect_limit${limit}`)}</uui-tag></p>`)}
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      uui-input {
        width: 100%;
        max-width: 640px;
      }
      uui-radio-group {
        display: block;
        margin-top: var(--uui-size-space-4);
      }
      .method-hint {
        margin: 0 0 var(--uui-size-space-3) calc(var(--uui-size-8) + var(--uui-size-space-1));
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    'dd-map-to-redirect-generate-tab': DdMapToRedirectGenerateTabElement;
  }
}
