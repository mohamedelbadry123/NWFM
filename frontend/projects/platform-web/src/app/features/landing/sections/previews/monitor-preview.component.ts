import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

type Tone = 'brand' | 'danger' | 'success';

/** Feature-tile drawing: the workload dashboard — totals, then pending and overdue work per assignment group. */
@Component({
  selector: 'app-monitor-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="kpis">
        @for (kpi of kpis; track kpi.key) {
          <div class="kpi">
            <small>{{ t('landing.features.monitoring.kpi.' + kpi.key) }}</small>
            <strong [class]="'tone-' + kpi.tone">{{ kpi.value }}</strong>
          </div>
        }
      </div>
      <div class="groups">
        @for (group of groups; track group.key) {
          <div class="group">
            <span class="name">{{ t('landing.features.monitoring.groups.' + group.key) }}</span>
            <span class="bar">
              <span class="pending" [style.width.%]="group.pending * 5"></span>
              <span class="overdue" [style.width.%]="group.overdue * 5"></span>
            </span>
            <small>{{
              t('landing.features.monitoring.rowMeta', {
                pending: group.pending,
                overdue: group.overdue,
              })
            }}</small>
          </div>
        }
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.87rem;
    }

    .kpis {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 0.67rem;
    }

    .kpi {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
      min-width: 0;
      padding: 0.67rem 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.8rem;

      small {
        font-size: 0.85rem;
        color: var(--p-text-muted-color);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      strong {
        font-size: 1.47rem;
        line-height: 1.2;
      }
    }

    .tone-brand {
      color: var(--p-primary-color);
    }

    .tone-danger {
      color: var(--acc-danger-fg);
    }

    .tone-success {
      color: var(--acc-success-fg);
    }

    .groups {
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
      padding: 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.8rem;
    }

    .group {
      display: grid;
      grid-template-columns: minmax(0, 8rem) minmax(0, 1fr) auto;
      align-items: center;
      gap: 0.8rem;

      @media (max-width: 520px) {
        grid-template-columns: minmax(0, 1fr) auto;

        .bar {
          display: none;
        }
      }

      .name {
        font-size: 0.93rem;
        font-weight: 600;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      small {
        font-size: 0.85rem;
        color: var(--p-text-muted-color);
        white-space: nowrap;
      }
    }

    .bar {
      display: flex;
      height: 0.47rem;
      overflow: hidden;
      border-radius: 999px;
      background: var(--app-hover);
    }

    .pending {
      background: var(--p-primary-color);
    }

    .overdue {
      background: var(--acc-danger-fg);
    }
  `,
})
export class MonitorPreviewComponent {
  protected readonly kpis: { key: string; value: number; tone: Tone }[] = [
    { key: 'open', value: 128, tone: 'brand' },
    { key: 'overdue', value: 7, tone: 'danger' },
    { key: 'completedToday', value: 42, tone: 'success' },
  ];

  /** Pending and overdue counts per group; each unit is 5% of the bar. */
  protected readonly groups = [
    { key: 'one', pending: 12, overdue: 1 },
    { key: 'two', pending: 7, overdue: 0 },
    { key: 'three', pending: 15, overdue: 4 },
  ];
}
