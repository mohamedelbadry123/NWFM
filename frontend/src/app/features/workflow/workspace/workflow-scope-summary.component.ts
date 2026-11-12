import { Component, Input, inject, signal } from '@angular/core';
import { LocaleService } from '@core/i18n/locale.service';

let nextId = 0;

/** A compact, read-only view of scope entries: the first few as chips, the rest behind a "+N more" button. */
@Component({
  selector: 'app-workflow-scope-summary',
  standalone: true,
  template: `
    <ul
      class="scope-summary"
      [id]="listId"
      [attr.aria-label]="t('Organization scope', 'النطاق التنظيمي')"
    >
      @for (label of shown(); track $index) {
        <li class="scope-chip">{{ label }}</li>
      } @empty {
        <li class="scope-empty">{{ empty }}</li>
      }
    </ul>
    @if (labels.length > limit) {
      <button
        type="button"
        class="scope-more"
        [attr.aria-expanded]="expanded()"
        [attr.aria-controls]="listId"
        (click)="expanded.set(!expanded())"
      >
        {{
          expanded()
            ? t('Show fewer', 'عرض أقل')
            : t('+' + (labels.length - limit) + ' more', 'و' + (labels.length - limit) + ' أخرى')
        }}
      </button>
    }
  `,
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      .scope-summary {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        margin: 0;
        padding: 0;
        list-style: none;
      }
      .scope-chip {
        max-width: 100%;
        border: 1px solid var(--ds-border, #dce4ea);
        border-radius: 6px;
        padding: 2px 10px;
        overflow-wrap: anywhere;
      }
      .scope-empty {
        overflow-wrap: anywhere;
      }
      .scope-more {
        margin-block-start: 6px;
        padding: 2px 0;
        font-size: 12px;
        font-weight: 600;
        color: var(--ds-primary, #5040d6);
        cursor: pointer;
      }
      .scope-more:hover {
        text-decoration: underline;
      }
      .scope-more:focus-visible {
        outline: 2px solid var(--ds-focus, #5040d6);
        outline-offset: 3px;
      }
    `,
  ],
})
export class WorkflowScopeSummaryComponent {
  @Input() labels: string[] = [];
  /** How many entries show before the "+N more" button. */
  @Input() limit = 2;
  /** Shown when there are no entries. */
  @Input() empty = '';
  private readonly locale = inject(LocaleService);
  readonly expanded = signal(false);
  readonly listId = 'workflow-scope-summary-' + nextId++;
  shown() {
    return this.expanded() ? this.labels : this.labels.slice(0, this.limit);
  }
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
}
