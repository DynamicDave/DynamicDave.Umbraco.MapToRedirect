import { css, html, customElement, property, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbConfirmModal } from '@umbraco-cms/backoffice/modal';
import type { UUIPaginationEvent } from '@umbraco-cms/backoffice/external/uui';
import { MapToRedirectService } from './api/index.js';
import type { ApplyOutcome, ImportModel, ImportRowModel, JobStateModel, SiteModel } from './api/index.js';
import {
  PAGE_SIZE,
  applyOutcomeColor,
  applyOutcomeTerm,
  errorCode,
  errorTerm,
  dismissJob,
  dismissTemplate,
  failureTemplate,
  isBannerDismissed,
  isJob,
  jobChanged,
  parseSummary,
  progressTemplate,
  rowMessage,
  sharedStyles,
} from './shared.js';
import type { UndoSummary } from './shared.js';

const APPLY_OUTCOMES: ApplyOutcome[] = ['Added', 'Replaced', 'AlreadyPresent', 'Skipped', 'Failed', 'Changed'];

const countOf = (counts: ImportModel['counts'], outcome: ApplyOutcome) =>
  counts[(outcome.charAt(0).toLowerCase() + outcome.slice(1)) as keyof ImportModel['counts']];

/** The stored result rows of one import, paged and filterable by outcome. Used by the history and after an apply. */
@customElement('dd-map-to-redirect-import-rows')
export class DdMapToRedirectImportRowsElement extends UmbLitElement {
  @property({ attribute: false }) importKey?: string;
  @property({ attribute: false }) counts?: ImportModel['counts'];

  @state() private _rows: ImportRowModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _filter?: ApplyOutcome;
  @state() private _loading = false;
  @state() private _failed = false;

  #request = 0;

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#request++;
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    super.willUpdate(changed);
    if (changed.has('importKey')) {
      this._page = 1;
      this._filter = undefined;
      void this.#load();
    }
  }

  async #load() {
    const key = this.importKey;
    if (!key) return;
    const requestId = ++this.#request;
    this._loading = true;
    let rows: ImportRowModel[] = [];
    let total = 0;
    let failed = false;
    try {
      const { data } = await MapToRedirectService.getImportRows({
        throwOnError: false,
        path: { key },
        query: { outcome: this._filter ? [this._filter] : undefined, skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      });
      failed = !data || !('items' in data);
      rows = data?.items ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#request) return; // stale response or detached
    this._rows = rows;
    this._total = total;
    this._failed = failed;
    this._loading = false;
  }

  #setFilter(outcome: ApplyOutcome | undefined) {
    this._filter = outcome;
    this._page = 1;
    void this.#load();
  }

  override render() {
    return html`${this.#renderFilters()} ${this.#renderRows()}`;
  }

  #renderFilters() {
    const counts = this.counts;
    if (!counts) return nothing;
    return html`<div class="tags">
      <button type="button" class="filter ${this._filter === undefined ? 'active' : ''}" @click=${() => this.#setFilter(undefined)}>
        <uui-tag look="secondary">${this.localize.term('ddMapToRedirect_all')}</uui-tag>
      </button>
      ${APPLY_OUTCOMES.filter((o) => countOf(counts, o) > 0).map(
        (o) => html`<button
          type="button"
          class="filter ${this._filter === o ? 'active' : ''}"
          @click=${() => this.#setFilter(this._filter === o ? undefined : o)}>
          <uui-tag color=${applyOutcomeColor(o)} look="secondary">${this.localize.term(applyOutcomeTerm(o))}: ${countOf(counts, o)}</uui-tag>
        </button>`,
      )}
    </div>`;
  }

  #renderRows() {
    if (this._loading && this._rows.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._failed) return html`<p class="error">${this.localize.term('ddMapToRedirect_loadFailed')}</p>`;
    if (this._rows.length === 0) return html`<p class="muted">${this.localize.term('ddMapToRedirect_noRows')}</p>`;
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    return html`
      <div class="table-wrap"><uui-table>
        <uui-table-head>
          <uui-table-head-cell>#</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_oldUrl')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_newUrl')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_outcome')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_message')}</uui-table-head-cell>
        </uui-table-head>
        ${repeat(
          this._rows,
          (r) => r.rowNumber,
          (r) => html`
            <uui-table-row>
              <uui-table-cell>${r.rowNumber}</uui-table-cell>
              <uui-table-cell class="url">${r.oldUrl ?? r.oldRoute}</uui-table-cell>
              <uui-table-cell class="url">${r.newUrl ?? '—'}</uui-table-cell>
              <uui-table-cell>
                <uui-tag color=${applyOutcomeColor(r.outcome)}>${this.localize.term(applyOutcomeTerm(r.outcome))}</uui-tag>
                ${r.undone ? html`<uui-tag look="outline">${this.localize.term('ddMapToRedirect_stateUndone')}</uui-tag>` : nothing}
              </uui-table-cell>
              <uui-table-cell>${rowMessage(this.localize, r)}</uui-table-cell>
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
              void this.#load();
            }}></uui-pagination>`
        : nothing}
    `;
  }

  static override styles = [sharedStyles];
}

/** History tab: the imports with their results, and undo (spec §4.6). */
@customElement('dd-map-to-redirect-history-tab')
export class DdMapToRedirectHistoryTabElement extends UmbLitElement {
  @property({ attribute: false }) sites: SiteModel[] = [];
  @property({ attribute: false }) job?: JobStateModel;
  @property({ attribute: false }) userKey?: string;

  @state() private _items: ImportModel[] = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _loading = true;
  @state() private _failed = false;
  @state() private _open?: string;
  @state() private _undoing?: string;
  @state() private _message?: string;
  /** The finished undo job started from this tab. */
  @state() private _undone?: JobStateModel;

  #request = 0;
  #watching?: string;

  override connectedCallback() {
    super.connectedCallback();
    void this.#load();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#request++;
  }

  /** Reloads the current page (after an import or undo). */
  reload() {
    void this.#load();
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    if (!changed.has('job')) return;
    const job = this.job;
    if (job?.kind !== 'Undo' || job.id !== this.#watching || job.status === 'Running') return;
    this.#watching = undefined;
    this._undone = job;
    void this.#load();
  }

  async #load(clamped = false) {
    const requestId = ++this.#request;
    this._loading = true;
    let items: ImportModel[] = [];
    let total = 0;
    let failed = false;
    try {
      const { data } = await MapToRedirectService.getImports({ throwOnError: false, query: { skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE } });
      failed = !data || !('items' in data);
      items = data?.items ?? [];
      total = data?.total ?? 0;
    } catch {
      failed = true;
    }
    if (requestId !== this.#request) return; // stale response or detached
    const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (!failed && !clamped && total > 0 && items.length === 0 && this._page > lastPage) {
      this._page = lastPage;
      void this.#load(true);
      return;
    }
    this._items = items;
    this._total = total;
    this._failed = failed;
    this._loading = false;
  }

  async #undo(item: ImportModel) {
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term('ddMapToRedirect_undo'),
        content: this.localize.term('ddMapToRedirect_undoConfirm'),
        confirmLabel: this.localize.term('ddMapToRedirect_undo'),
        color: 'danger',
      });
    } catch {
      return; // cancelled
    }
    this._undoing = item.key;
    this._message = undefined;
    try {
      const { data, error, response } = await MapToRedirectService.undoImport({ throwOnError: false, path: { key: item.key } });
      if (isJob(data)) {
        this.#watching = data.id;
        this._undone = undefined;
        this.dispatchEvent(jobChanged(data));
      } else if (response?.status === 409) {
        // Either the import was undone meanwhile ({ error: "alreadyUndone" }) or another job runs (the job itself).
        if (isJob(error)) this.dispatchEvent(jobChanged(error));
        else if (errorCode(error) === 'alreadyUndone') {
          this._message = this.localize.term('ddMapToRedirect_errorAlreadyUndone');
          void this.#load();
        } else this._message = this.localize.term('ddMapToRedirect_jobRunning');
      } else if (response?.status === 404) {
        this._message = this.localize.term('ddMapToRedirect_errorImportNotFound');
        void this.#load();
      } else {
        this._message = this.localize.term(errorTerm(errorCode(error)));
      }
    } catch {
      this._message = this.localize.term('ddMapToRedirect_loadFailed');
    }
    this._undoing = undefined;
  }

  #siteName(rootKey: string) {
    return this.sites.find((s) => s.rootKey === rootKey)?.name ?? '—';
  }

  override render() {
    return html`${this.#renderUndoJob()} ${this._message ? html`<p class="error">${this._message}</p>` : nothing} ${this.#renderList()}`;
  }

  #renderUndoJob() {
    const job = this.job;
    if (job?.kind === 'Undo' && job.status === 'Running' && job.id === this.#watching) return progressTemplate(this.localize, job);
    const undone = this._undone;
    if (!undone || isBannerDismissed(undone.id)) return nothing;
    const summary = parseSummary<UndoSummary>(undone.summary);
    return html`
      ${dismissTemplate(this.localize, () => {
        dismissJob(undone.id);
        this._undone = undefined;
      })}
      ${failureTemplate(this.localize, undone)}
      ${undone.status === 'Completed' && summary
        ? html`<p><uui-tag color="positive" look="secondary">${this.localize.term('ddMapToRedirect_undoSummary', summary.removed, summary.restored, summary.leftAlone)}</uui-tag></p>`
        : nothing}
    `;
  }

  #renderList() {
    if (this._loading && this._items.length === 0) return html`<uui-loader></uui-loader>`;
    if (this._failed) return html`<p class="error">${this.localize.term('ddMapToRedirect_loadFailed')}</p>`;
    if (this._items.length === 0) return html`<p class="muted">${this.localize.term('ddMapToRedirect_noImports')}</p>`;
    const running = this.job?.status === 'Running';
    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
    return html`
      <div class="table-wrap"><uui-table>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_date')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_user')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_file')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_site')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_language')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_counts')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('ddMapToRedirect_state')}</uui-table-head-cell>
          <uui-table-head-cell></uui-table-head-cell>
        </uui-table-head>
        ${repeat(
          this._items,
          (i) => i.key,
          (i) => html`
            <uui-table-row>
              <uui-table-cell>${this.localize.date(i.createdUtc, { dateStyle: 'medium', timeStyle: 'short' })}</uui-table-cell>
              <uui-table-cell>${i.userName}</uui-table-cell>
              <uui-table-cell class="url">${i.fileName}</uui-table-cell>
              <uui-table-cell>${this.#siteName(i.rootKey)}</uui-table-cell>
              <uui-table-cell>${i.culture}</uui-table-cell>
              <uui-table-cell>
                ${APPLY_OUTCOMES.filter((o) => countOf(i.counts, o) > 0).map(
                  (o) => html`<uui-tag color=${applyOutcomeColor(o)} look="secondary">${this.localize.term(applyOutcomeTerm(o))}: ${countOf(i.counts, o)}</uui-tag> `,
                )}
              </uui-table-cell>
              <uui-table-cell>
                <uui-tag look="outline">${this.localize.term(`ddMapToRedirect_state${i.state}`)}</uui-tag>
                ${i.state === 'Undone' && i.undoneUtc
                  ? html`<div class="muted">${this.localize.term(
                      'ddMapToRedirect_undoneBy',
                      i.undoneBy ?? '—',
                      this.localize.date(i.undoneUtc, { dateStyle: 'medium', timeStyle: 'short' }),
                    )}</div>`
                  : nothing}
              </uui-table-cell>
              <uui-table-cell class="row-actions">
                <uui-button
                  look="secondary"
                  compact
                  label=${this.localize.term(this._open === i.key ? 'ddMapToRedirect_hide' : 'ddMapToRedirect_view')}
                  @click=${() => (this._open = this._open === i.key ? undefined : i.key)}></uui-button>
                ${i.state !== 'Undone'
                  ? html`<uui-button
                      look="secondary"
                      color="danger"
                      compact
                      label=${this.localize.term('ddMapToRedirect_undo')}
                      ?disabled=${running}
                      .state=${this._undoing === i.key ? 'waiting' : undefined}
                      @click=${() => this.#undo(i)}></uui-button>`
                  : nothing}
              </uui-table-cell>
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
              this._open = undefined;
              void this.#load();
            }}></uui-pagination>`
        : nothing}
      ${this.#renderOpen()}
    `;
  }

  #renderOpen() {
    const item = this._items.find((i) => i.key === this._open);
    if (!item) return nothing;
    return html`
      <h4>${item.fileName} · ${this.localize.date(item.createdUtc, { dateStyle: 'medium', timeStyle: 'short' })}</h4>
      <dd-map-to-redirect-import-rows .importKey=${item.key} .counts=${item.counts}></dd-map-to-redirect-import-rows>
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      h4 {
        margin: var(--uui-size-space-6) 0 0;
      }
      .row-actions uui-button {
        display: block;
        margin: 2px 0;
      }
      .row-actions {
        min-width: 5em;
      }
      uui-table-cell uui-tag {
        margin: 1px 0;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    'dd-map-to-redirect-import-rows': DdMapToRedirectImportRowsElement;
    'dd-map-to-redirect-history-tab': DdMapToRedirectHistoryTabElement;
  }
}
