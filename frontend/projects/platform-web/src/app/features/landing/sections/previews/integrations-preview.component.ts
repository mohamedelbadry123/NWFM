import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** Feature-tile drawing: the connection types an activity can use, and one delivered API request. */
@Component({
  selector: 'app-integrations-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="chips">
        @for (item of connectors; track item.key) {
          <span class="chip"
            ><i [class]="item.icon"></i
            ><span class="chip-label">{{
              t('landing.features.integrations.connectors.' + item.key)
            }}</span></span
          >
        }
      </div>
      <div class="log">
        <span class="method">POST</span>
        <code>/orders</code>
        <span class="app-badge app-badge--success">201</span>
        <small>184 ms</small>
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.87rem;
    }

    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.53rem;
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 0.53rem;
      min-width: 0;
      max-width: 100%;
      padding: 0.47rem 0.8rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.67rem;
      font-size: 0.96rem;
      font-weight: 500;

      i {
        color: var(--p-primary-color);
        font-size: 0.93rem;
      }
    }

    .chip-label {
      min-width: 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    // A request line reads left to right in both languages.
    .log {
      direction: ltr;
      display: flex;
      align-items: center;
      gap: 0.67rem;
      padding: 0.67rem 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.73rem;
      font-size: 0.93rem;

      code {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      }

      small {
        font-variant-numeric: tabular-nums;
        color: var(--p-text-muted-color);
      }
    }

    .method {
      padding: 0.13rem 0.47rem;
      border-radius: 0.4rem;
      background: var(--acc-brand-bg);
      color: var(--acc-brand-fg);
      font-size: 0.8rem;
      font-weight: 700;
    }
  `,
})
export class IntegrationsPreviewComponent {
  protected readonly connectors = [
    { key: 'rest', icon: 'pi pi-globe' },
    { key: 'soap', icon: 'pi pi-code' },
    { key: 'email', icon: 'pi pi-envelope' },
    { key: 'sms', icon: 'pi pi-comment' },
    { key: 'webhook', icon: 'pi pi-bolt' },
  ];
}
