import {
  ChangeDetectionStrategy,
  Component,
  input,
} from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';

/** Consistent bordered table surface for Workflow list screens. */
@Component({
  selector: 'app-workflow-table-shell',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule],
  template: `
    <div class="wf-card overflow-hidden">
      @if (titleKey()) {
        <div class="flex items-center justify-between gap-3 border-b border-ink-200 px-4 py-3 dark:border-dark-700">
          <h2 class="text-sm font-semibold text-ink-900 dark:text-white">{{ titleKey()! | translate }}</h2>
          <ng-content select="[toolbar]"></ng-content>
        </div>
      }
      <div class="overflow-x-auto">
        <ng-content></ng-content>
      </div>
      <ng-content select="[footer]"></ng-content>
    </div>
  `,
  styles: `
    :host ::ng-deep table {
      width: 100%;
      min-width: 52rem;
      font-size: 0.875rem;
      border-collapse: collapse;
    }
    :host ::ng-deep thead {
      position: sticky;
      top: 0;
      z-index: 1;
      background: #f3f4f6;
      color: #6b7280;
      font-size: 0.7rem;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    :host-context(html.dark) ::ng-deep thead {
      background: rgb(48 48 48 / 0.55);
      color: #9e9e9e;
    }
    :host ::ng-deep th {
      padding: 0.75rem 0.5rem;
      text-align: start;
      font-weight: 600;
    }
    :host ::ng-deep th:first-child,
    :host ::ng-deep td:first-child {
      padding-inline-start: 1rem;
    }
    :host ::ng-deep th:last-child,
    :host ::ng-deep td:last-child {
      padding-inline-end: 1rem;
    }
    :host ::ng-deep tbody tr {
      border-top: 1px solid #e5e7eb;
      transition: background-color 120ms ease;
    }
    :host-context(html.dark) ::ng-deep tbody tr {
      border-top-color: #303030;
    }
    :host ::ng-deep tbody tr:hover {
      background: #f9fafb;
    }
    :host-context(html.dark) ::ng-deep tbody tr:hover {
      background: rgb(48 48 48 / 0.45);
    }
    :host ::ng-deep td {
      padding: 0.75rem 0.5rem;
      vertical-align: middle;
      color: #111827;
    }
    :host-context(html.dark) ::ng-deep td {
      color: #e5e5e5;
    }
    :host ::ng-deep td .text-white,
    :host ::ng-deep td .font-medium.text-white {
      color: #111827;
    }
    :host-context(html.dark) ::ng-deep td .text-white,
    :host-context(html.dark) ::ng-deep td .font-medium.text-white {
      color: #fff;
    }
  `,
})
export class WorkflowTableShellComponent {
  readonly titleKey = input<string | null>(null);
}
