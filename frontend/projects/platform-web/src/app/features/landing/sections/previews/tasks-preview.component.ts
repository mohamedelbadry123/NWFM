import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** Feature-tile drawing: field tasks as the task list shows them — each with its team, place and status. */
@Component({
  selector: 'app-tasks-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      @for (task of tasks; track task.key) {
        <div class="task">
          <strong>{{ t('landing.features.teams.tasks.' + task.key + '.title') }}</strong>
          <span [class]="'app-badge app-badge--' + task.tone">{{
            t('landing.features.teams.status.' + task.status)
          }}</span>
          <small
            ><i class="pi pi-map-marker"></i
            >{{ t('landing.features.teams.tasks.' + task.key + '.meta') }}</small
          >
        </div>
      }
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.67rem;
    }

    // Title and status share the first line; the team and place get the whole second line.
    .task {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      align-items: center;
      gap: 0.27rem 0.67rem;
      padding: 0.73rem 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.87rem;

      strong,
      small {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      strong {
        font-size: 1rem;
      }

      small {
        grid-column: 1 / -1;
        font-size: 0.88rem;
        color: var(--p-text-muted-color);

        i {
          margin-inline-end: 0.33rem;
          font-size: 0.8rem;
          color: var(--p-primary-color);
        }
      }

      .app-badge {
        font-size: 0.8rem;
      }
    }
  `,
})
export class TasksPreviewComponent {
  protected readonly tasks = [
    { key: 'one', status: 'filled', tone: 'brand' },
    { key: 'two', status: 'returned', tone: 'danger' },
    { key: 'three', status: 'completed', tone: 'success' },
  ];
}
