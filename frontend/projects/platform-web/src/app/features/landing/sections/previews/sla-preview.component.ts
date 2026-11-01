import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

type Tone = 'success' | 'warn' | 'danger';

/** Feature-tile drawing: three tasks against their SLA — on track, reminded, and overdue and escalated. */
@Component({
  selector: 'app-sla-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      @for (row of rows; track row.key) {
        <div class="row">
          <span class="text">
            <strong>{{ t('landing.features.sla.rows.' + row.key) }}</strong>
            <small [class]="'tone-' + row.tone">{{
              t('landing.features.sla.state.' + row.key)
            }}</small>
          </span>
          <span class="track"
            ><span [class]="'tone-' + row.tone" [style.width.%]="row.used"></span
          ></span>
        </div>
      }
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.87rem;
    }

    .row {
      display: flex;
      flex-direction: column;
      gap: 0.47rem;
    }

    .text {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 0.67rem;
      min-width: 0;

      strong {
        font-size: 1rem;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      small {
        flex-shrink: 0;
        font-size: 0.88rem;
        font-weight: 600;
      }
    }

    .track {
      display: flex;
      height: 0.47rem;
      overflow: hidden;
      border-radius: 999px;
      background: var(--app-hover);

      span {
        border-radius: inherit;
      }
    }

    small.tone-success {
      color: var(--acc-success-fg);
    }

    small.tone-warn {
      color: var(--acc-warn-fg);
    }

    small.tone-danger {
      color: var(--acc-danger-fg);
    }

    .track .tone-success {
      background: var(--acc-success-fg);
    }

    .track .tone-warn {
      background: var(--acc-warn-fg);
    }

    .track .tone-danger {
      background: var(--acc-danger-fg);
    }
  `,
})
export class SlaPreviewComponent {
  /** Share of each task's SLA already used, as a percent. */
  protected readonly rows: { key: string; tone: Tone; used: number }[] = [
    { key: 'one', tone: 'success', used: 35 },
    { key: 'two', tone: 'warn', used: 82 },
    { key: 'three', tone: 'danger', used: 100 },
  ];
}
