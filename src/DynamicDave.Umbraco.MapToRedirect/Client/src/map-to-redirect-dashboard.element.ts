import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import type { UUISelectEvent } from '@umbraco-cms/backoffice/external/uui';
import { MapToRedirectService } from './api/index.js';
import type { JobStateModel, SiteModel } from './api/index.js';
import { JOB_CHANGED_EVENT, IMPORTS_CHANGED_EVENT, MAX_POLL_MS, POLL_MS, isJob, sharedStyles } from './shared.js';
import type { JobChangedEvent } from './shared.js';
import './generate-tab.element.js';
import './upload-tab.element.js';
import './history-tab.element.js';
import type { DdMapToRedirectHistoryTabElement } from './history-tab.element.js';

type Tab = 'generate' | 'upload' | 'history';

const TABS: Array<{ tab: Tab; term: string }> = [
  { tab: 'generate', term: 'ddMapToRedirect_tabGenerate' },
  { tab: 'upload', term: 'ddMapToRedirect_tabUpload' },
  { tab: 'history', term: 'ddMapToRedirect_tabHistory' },
];

/**
 * The "Import redirects" dashboard (spec §4.2): site and language pickers, and the generate, upload and history tabs.
 * Owns the one job of the installation: it polls it every 2 s while it runs and hands it to the tabs.
 */
@customElement('dd-map-to-redirect-dashboard')
export class DdMapToRedirectDashboardElement extends UmbLitElement {
  @state() private _sites: SiteModel[] = [];
  @state() private _rootKey?: string;
  @state() private _culture?: string;
  @state() private _loading = true;
  @state() private _error?: 'noAccess' | 'loadFailed';
  @state() private _tab: Tab = 'generate';
  @state() private _job?: JobStateModel;
  @state() private _userKey?: string;

  /** Incremented per request so responses of superseded requests are ignored. */
  #jobRequest = 0;
  #pollTimer?: ReturnType<typeof setTimeout>;
  /** Consecutive failed job requests, for the back-off. */
  #failures = 0;
  #connected = false;

  constructor() {
    super();
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
      this.observe(context?.currentUser, (user) => (this._userKey = user?.unique), '_currentUser');
    });
    this.addEventListener(JOB_CHANGED_EVENT, (e) => this.#setJob((e as JobChangedEvent).detail));
    this.addEventListener(IMPORTS_CHANGED_EVENT, () => this.#history()?.reload());
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#connected = true;
    void this.#loadSites();
    void this.#loadJob();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#connected = false;
    clearTimeout(this.#pollTimer);
    this.#jobRequest++;
  }

  async #loadSites() {
    this._loading = true;
    let sites: SiteModel[] | undefined;
    let status: number | undefined;
    try {
      const { data, response } = await MapToRedirectService.getSites({ throwOnError: false });
      sites = Array.isArray(data) ? data : undefined;
      status = response?.status;
    } catch {
      sites = undefined;
    }
    if (!this.#connected) return;
    this._loading = false;
    if (!sites) {
      this._error = status === 403 ? 'noAccess' : 'loadFailed';
      return;
    }
    this._error = undefined;
    this._sites = sites;
    const site = sites.find((s) => s.rootKey === this._rootKey) ?? sites[0];
    this.#selectSite(site);
  }

  #selectSite(site: SiteModel | undefined) {
    this._rootKey = site?.rootKey;
    this._culture = site && site.cultures.includes(this._culture ?? '') ? this._culture : site?.cultures[0];
  }

  async #loadJob() {
    const requestId = ++this.#jobRequest;
    clearTimeout(this.#pollTimer);
    let job: JobStateModel | undefined;
    let failed = false;
    let status: number | undefined;
    try {
      const { data, error, response } = await MapToRedirectService.getJob({ throwOnError: false });
      failed = error !== undefined;
      status = response?.status;
      job = isJob(data) ? data : undefined; // 204: there has been no job yet
    } catch {
      failed = true;
    }
    if (requestId !== this.#jobRequest || !this.#connected) return; // stale response or detached
    if (!failed) {
      this._job = job;
      this.#failures = 0;
    }
    // No access: polling cannot help. Other failures are retried with a growing delay (2, 4, 8, 10 s).
    if (status === 403) return;
    if (failed) {
      this.#failures++;
      this.#pollTimer = setTimeout(() => void this.#loadJob(), Math.min(POLL_MS * 2 ** (this.#failures - 1), MAX_POLL_MS));
    } else if (job?.status === 'Running') {
      this.#pollTimer = setTimeout(() => void this.#loadJob(), POLL_MS);
    }
  }

  #setJob(job: JobStateModel) {
    // Invalidate an in-flight poll that may carry an older state.
    this.#jobRequest++;
    this._job = job;
    clearTimeout(this.#pollTimer);
    if (job.status === 'Running') this.#pollTimer = setTimeout(() => void this.#loadJob(), POLL_MS);
  }

  #history() {
    return this.shadowRoot?.querySelector<DdMapToRedirectHistoryTabElement>('dd-map-to-redirect-history-tab') ?? undefined;
  }

  #selectTab(tab: Tab) {
    this._tab = tab;
    // Someone else may have started a job meanwhile.
    if (this._job?.status !== 'Running') void this.#loadJob();
  }

  override render() {
    if (this._loading) return html`<uui-loader></uui-loader>`;
    if (this._error) {
      return html`<uui-box headline=${this.localize.term('ddMapToRedirect_title')}>
        <p>${this.localize.term(`ddMapToRedirect_${this._error}`)}</p>
      </uui-box>`;
    }
    const site = this._sites.find((s) => s.rootKey === this._rootKey);
    const job = this._job;
    const busyByOther = job?.status === 'Running' && job.userKey !== this._userKey;
    return html`
      <uui-box headline=${this.localize.term('ddMapToRedirect_title')}>
        ${this.#renderPickers(site)}
        ${busyByOther ? html`<p class="busy"><uui-tag color="warning">${this.localize.term('ddMapToRedirect_jobBusy', job.userName)}</uui-tag></p>` : nothing}
        <uui-tab-group>
          ${TABS.map(
            (t) => html`<uui-tab
              label=${this.localize.term(t.term)}
              ?active=${this._tab === t.tab}
              @click=${() => this.#selectTab(t.tab)}>${this.localize.term(t.term)}</uui-tab>`,
          )}
        </uui-tab-group>
        <div class="panel">
          <dd-map-to-redirect-generate-tab
            ?hidden=${this._tab !== 'generate'}
            .site=${site}
            .culture=${this._culture}
            .job=${job}
            .userKey=${this._userKey}></dd-map-to-redirect-generate-tab>
          <dd-map-to-redirect-upload-tab
            ?hidden=${this._tab !== 'upload'}
            .site=${site}
            .culture=${this._culture}
            .job=${job}
            .userKey=${this._userKey}></dd-map-to-redirect-upload-tab>
          <dd-map-to-redirect-history-tab
            ?hidden=${this._tab !== 'history'}
            .sites=${this._sites}
            .job=${job}
            .userKey=${this._userKey}></dd-map-to-redirect-history-tab>
        </div>
      </uui-box>
    `;
  }

  #renderPickers(site: SiteModel | undefined) {
    const showSites = this._sites.length > 1;
    const showCultures = (site?.cultures.length ?? 0) > 1;
    if (!site) return html`<p>${this.localize.term('ddMapToRedirect_noSites')}</p>`;
    if (!showSites && !showCultures) return nothing;
    return html`
      <div class="pickers">
        ${showSites
          ? html`<uui-label for="site">${this.localize.term('ddMapToRedirect_site')}</uui-label>
              <uui-select
                id="site"
                label=${this.localize.term('ddMapToRedirect_site')}
                .options=${this._sites.map((s) => ({
                  name: s.domains.length > 0 ? `${s.name} – ${s.domains[0]}` : s.name,
                  value: s.rootKey,
                  selected: s.rootKey === this._rootKey,
                }))}
                @change=${(e: UUISelectEvent) => this.#selectSite(this._sites.find((s) => s.rootKey === e.target.value))}></uui-select>`
          : nothing}
        ${showCultures
          ? html`<uui-label for="culture">${this.localize.term('ddMapToRedirect_language')}</uui-label>
              <uui-select
                id="culture"
                label=${this.localize.term('ddMapToRedirect_language')}
                .options=${site.cultures.map((c) => ({ name: c, value: c, selected: c === this._culture }))}
                @change=${(e: UUISelectEvent) => (this._culture = e.target.value as string)}></uui-select>`
          : nothing}
      </div>
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }
      .pickers {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }
      .pickers uui-select {
        min-width: 240px;
        margin-right: var(--uui-size-space-4);
      }
      .panel {
        padding-top: var(--uui-size-space-5);
      }
      [hidden] {
        display: none;
      }
    `,
  ];
}

export default DdMapToRedirectDashboardElement;
