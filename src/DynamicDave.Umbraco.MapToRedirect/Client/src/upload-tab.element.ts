import { css, html, customElement, property, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbConfirmModal } from '@umbraco-cms/backoffice/modal';
import type { UUIBooleanInputEvent, UUIPaginationEvent } from '@umbraco-cms/backoffice/external/uui';
import { MapToRedirectService } from './api/index.js';
import type { JobStateModel, PlanOutcome, PlanResponse, PlanRowModel, SiteModel } from './api/index.js';
import {
  IMPORTS_CHANGED_EVENT,
  PAGE_SIZE,
  canDownload,
  errorCode,
  errorTerm,
  failureTemplate,
  fileNameFrom,
  isJob,
  jobChanged,
  parseSummary,
  planOutcomeColor,
  planOutcomeTerm,
  progressTemplate,
  saveBlob,
  sharedStyles,
} from './shared.js';
import type { ApplySummary } from './shared.js';
import './history-tab.element.js';


/** Upload tab: test run of a CSV (spec §4.4), then Apply as a job (spec §4.5) with its actual results. */
@customElement('dd-map-to-redirect-upload-tab')
export class DdMapToRedirectUploadTabElement extends UmbLitElement {
  @property({ attribute: false }) site?: SiteModel;
  @property({ attribute: false }) culture?: string;
  @property({ attribute: false }) job?: JobStateModel;
  @property({ attribute: false }) userKey?: string;

  @state() private _plan?: PlanResponse;
  @state() private _rows: PlanRowModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _filter?: PlanOutcome;
  @state() private _overwrite = false;
  @state() private _uploading = false;
  @state() private _loadingRows = false;
  @state() private _message?: string;
  @state() private _applying = false;
  /** The finished apply job started from this tab; its results are shown below the test run. */
  @state() private _applied?: JobStateModel;
  @state() private _downloading = false;

  /** Plans an apply was started for: a stored plan stays applicable, so a second import is prevented here. */
  #appliedPlans = new Set<string>();
  #watching?: string;
  #rowsRequest = 0;

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#rowsRequest++;
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    super.willUpdate(changed);
    // A test run belongs to one site and language.
    const siteChanged = changed.has('site') && changed.get('site')?.rootKey !== this.site?.rootKey && changed.get('site') !== undefined;
    const cultureChanged = changed.has('culture') && changed.get('culture') !== undefined && changed.get('culture') !== this.culture;
    if ((siteChanged || cultureChanged) && this._plan) this.#clearPlan();
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    if (!changed.has('job')) return;
    const job = this.job;
    if (job?.kind !== 'Apply' || job.id !== this.#watching || job.status === 'Running') return;
    this.#watching = undefined;
    this._applied = job;
    this.dispatchEvent(new CustomEvent(IMPORTS_CHANGED_EVENT, { bubbles: true, composed: true }));
  }

  #clearPlan() {
    this.#rowsRequest++;
    this._plan = undefined;
    this._rows = [];
    this._total = 0;
    this._page = 1;
    this._filter = undefined;
    this._overwrite = false;
    this._applied = undefined;
  }

  #chooseFile() {
    this.shadowRoot?.querySelector<HTMLInputElement>('#file')?.click();
  }

  async #upload(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // the same file can be chosen again
    if (!file || !this.site || !this.culture) return;
    this.#clearPlan();
    this._message = undefined;
    this._uploading = true;
    try {
      const { data, error } = await MapToRedirectService.createPlan({
        throwOnError: false,
        body: { file },
        query: { rootKey: this.site.rootKey, culture: this.culture },
      });
      if (data && 'planId' in data) {
        this._plan = data;
        this._rows = data.firstPage.items;
        this._total = data.firstPage.total;
      } else {
        this._message = this.localize.term(errorTerm(errorCode(error)));
      }
    } catch {
      this._message = this.localize.term('ddMapToRedirect_loadFailed');
    }
    this._uploading = false;
  }

  async #loadRows() {
    const plan = this._plan;
    if (!plan) return;
    const requestId = ++this.#rowsRequest;
    this._loadingRows = true;
    let failed = false;
    let expired = false;
    let rows: PlanRowModel[] = [];
    let total = 0;
    try {
      const { data, response } = await MapToRedirectService.getPlanRows({
        throwOnError: false,
        path: { id: plan.planId },
        query: { outcome: this._filter ? [this._filter] : undefined, skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      });
      expired = response?.status === 404;
      failed = !data || !('items' in data);
      rows = data?.items ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#rowsRequest) return; // stale response or detached
    this._loadingRows = false;
    if (expired) {
      this.#clearPlan();
      this._message = this.localize.term('ddMapToRedirect_planExpired');
      return;
    }
    if (failed) {
      this._message = this.localize.term('ddMapToRedirect_loadFailed');
      return;
    }
    this._message = undefined;
    this._rows = rows;
    this._total = total;
  }

  #setFilter(outcome: PlanOutcome | undefined) {
    this._filter = outcome;
    this._page = 1;
    void this.#loadRows();
  }

  #count(outcome: PlanOutcome) {
    return this._plan?.counts.find((c) => c.outcome === outcome)?.count ?? 0;
  }

  async #apply() {
    const plan = this._plan;
    if (!plan) return;
    const add = this.#count('Add');
    const replace = this._overwrite ? this.#count('Conflict') : 0;
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term('ddMapToRedirect_apply'),
        content: this.localize.term('ddMapToRedirect_applyConfirm', add, replace),
        confirmLabel: this.localize.term('ddMapToRedirect_apply'),
        color: 'positive',
      });
    } catch {
      return; // cancelled
    }
    if (this.#appliedPlans.has(plan.planId)) return;
    this._applying = true;
    this._message = undefined;
    try {
      const { data, error, response } = await MapToRedirectService.applyPlan({
        throwOnError: false,
        path: { id: plan.planId },
        body: { overwrite: this._overwrite },
      });
      if (isJob(data)) {
        this.#appliedPlans.add(plan.planId);
        this.#watching = data.id;
        this._applied = undefined;
        this.dispatchEvent(jobChanged(data));
      } else if (response?.status === 404) {
        this.#clearPlan();
        this._message = this.localize.term('ddMapToRedirect_planExpired');
      } else if (response?.status === 409) {
        if (isJob(error)) this.dispatchEvent(jobChanged(error));
        else this._message = this.localize.term('ddMapToRedirect_jobRunning');
      } else {
        this._message = this.localize.term(errorTerm(errorCode(error)));
      }
    } catch {
      this._message = this.localize.term('ddMapToRedirect_loadFailed');
    }
    this._applying = false;
  }

  async #downloadReport() {
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
    saveBlob(blob, fileNameFrom(response, 'redirect-report.csv'));
  }

  override render() {
    const running = this.job?.status === 'Running';
    return html`
      <p class="hint">${this.localize.term('ddMapToRedirect_uploadHint')}</p>
      <div class="actions">
        <input id="file" type="file" accept=".csv,text/csv" hidden @change=${this.#upload} />
        <uui-button
          look="primary"
          label=${this.localize.term('ddMapToRedirect_chooseFile')}
          ?disabled=${!this.site || !this.culture}
          .state=${this._uploading ? 'waiting' : undefined}
          @click=${this.#chooseFile}></uui-button>
        ${this._plan ? html`<span class="muted">${this._plan.fileName}</span>` : nothing}
      </div>
      ${this._message ? html`<p class="error">${this._message}</p>` : nothing}
      ${this.#renderApplyJob()} ${this._plan ? this.#renderPlan(this._plan, running) : nothing}
    `;
  }

  #renderApplyJob() {
    const job = this.job;
    if (job?.kind === 'Apply' && job.status === 'Running' && job.id === this.#watching) return progressTemplate(this.localize, job);
    const applied = this._applied;
    if (!applied) return nothing;
    const summary = parseSummary<ApplySummary>(applied.summary);
    const downloadable = canDownload(applied, this.userKey) && this.job?.id === applied.id;
    return html`
      <h4>${this.localize.term('ddMapToRedirect_results')}</h4>
      ${failureTemplate(this.localize, applied)}
      ${downloadable
        ? html`<uui-button
            look="outline"
            label=${this.localize.term('ddMapToRedirect_downloadReport')}
            .state=${this._downloading ? 'waiting' : undefined}
            @click=${this.#downloadReport}></uui-button>`
        : nothing}
      ${applied.status === 'Completed' && applied.resultKey
        ? html`<dd-map-to-redirect-import-rows .importKey=${applied.resultKey} .counts=${summary}></dd-map-to-redirect-import-rows>`
        : nothing}
    `;
  }

  #renderPlan(plan: PlanResponse, running: boolean) {
    const conflicts = this.#count('Conflict');
    const toExecute = this.#count('Add') + (this._overwrite ? conflicts : 0);
    const applied = this.#appliedPlans.has(plan.planId);
    const all = plan.counts.reduce((sum, c) => sum + c.count, 0);
    return html`
      <h4>${this.localize.term('ddMapToRedirect_testRun')}</h4>
      <div class="tags">
        <button type="button" class="filter ${this._filter === undefined ? 'active' : ''}" @click=${() => this.#setFilter(undefined)}>
          <uui-tag look="secondary">${this.localize.term('ddMapToRedirect_all')}: ${all}</uui-tag>
        </button>
        ${plan.counts.map(
          (c) => html`<button
            type="button"
            class="filter ${this._filter === c.outcome ? 'active' : ''}"
            @click=${() => this.#setFilter(this._filter === c.outcome ? undefined : c.outcome)}>
            <uui-tag color=${planOutcomeColor(c.outcome)} look="secondary">${this.localize.term(planOutcomeTerm(c.outcome))}: ${c.count}</uui-tag>
          </button>`,
        )}
      </div>
      ${this.#renderRows()}
      <div class="actions">
        ${conflicts > 0
          ? html`<uui-checkbox
              label=${this.localize.term('ddMapToRedirect_overwrite')}
              ?checked=${this._overwrite}
              ?disabled=${applied}
              @change=${(e: UUIBooleanInputEvent) => (this._overwrite = e.target.checked)}>${this.localize.term('ddMapToRedirect_overwrite')}</uui-checkbox>`
          : nothing}
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('ddMapToRedirect_apply')}
          ?disabled=${running || applied || this._applying || toExecute === 0}
          .state=${this._applying ? 'waiting' : undefined}
          @click=${this.#apply}></uui-button>
      </div>
    `;
  }

  #renderRows() {
    if (this._loadingRows && this._rows.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._rows.length === 0) return html`<p class="muted">${this.localize.term('ddMapToRedirect_noRows')}</p>`;
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    return html`
      <div class="table-wrap"><uui-table>
        <uui-table-head>
          <uui-table-head-cell>#</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_oldUrl')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_newUrl')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_page')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_outcome')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_reason')}</uui-table-head-cell>
        </uui-table-head>
        ${repeat(
          this._rows,
          (r) => r.rowNumber,
          (r) => html`
            <uui-table-row>
              <uui-table-cell>${r.rowNumber}</uui-table-cell>
              <uui-table-cell class="url">${r.oldUrl}</uui-table-cell>
              <uui-table-cell class="url">${r.newUrl}</uui-table-cell>
              <uui-table-cell class="url">${r.pagePath ?? '—'}</uui-table-cell>
              <uui-table-cell><uui-tag color=${planOutcomeColor(r.outcome)}>${this.localize.term(planOutcomeTerm(r.outcome))}</uui-tag></uui-table-cell>
              <uui-table-cell>${this.#reason(r)}</uui-table-cell>
            </uui-table-row>
          `,
        )}
      </uui-table></div>
      ${pages > 1
        ? html`<uui-pagination
            .total=${pages}
            .current=${this._page}
            @change=${(e: UUIPaginationEvent) => {
              this._page = e.target.current;
              void this.#loadRows();
            }}></uui-pagination>`
        : nothing}
    `;
  }

  #reason(row: PlanRowModel) {
    if (row.outcome === 'Conflict' || row.outcome === 'AlreadyPresent') {
      if (!row.currentPagePath) return nothing;
      const all = row.currentAllCultures ? html` <span class="muted">(${this.localize.term('ddMapToRedirect_allLanguages')})</span>` : nothing;
      return html`${this.localize.term('ddMapToRedirect_currentTarget', row.currentPagePath)}${all}`;
    }
    return row.reason === 'None' ? nothing : this.localize.term(`ddMapToRedirect_reason${row.reason}`);
  }

  static override styles = [
    sharedStyles,
    css`
      h4 {
        margin: var(--uui-size-space-5) 0 0;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    'dd-map-to-redirect-upload-tab': DdMapToRedirectUploadTabElement;
  }
}
