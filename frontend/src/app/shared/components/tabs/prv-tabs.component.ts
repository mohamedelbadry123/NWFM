import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

export interface PrvTab {
  readonly id: string;
  readonly label: string;
}

/**
 * prv-tabs — segmented control / pill tabs. Adapted from TailAdmin's chart-tab.
 * Two-way bind the selected id via [(value)]; RTL-safe.
 *
 *   <prv-tabs [tabs]="[{id:'m',label:'Monthly'},{id:'q',label:'Quarterly'}]" [(value)]="range" />
 */
@Component({
  selector: 'prv-tabs',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="inline-flex items-center gap-0.5 rounded-lg bg-ink-100 p-0.5 dark:bg-surface-700" role="tablist">
      @for (t of tabs(); track t.id) {
        <button
          type="button"
          role="tab"
          [attr.aria-selected]="value() === t.id"
          (click)="value.set(t.id)"
          [class]="'px-3 py-2 font-medium rounded-md text-sm transition ' + btnClass(t.id)"
        >{{ t.label }}</button>
      }
    </div>
  `,
  styles: [':host { display: inline-block; }'],
})
export class PrvTabsComponent {
  readonly tabs = input.required<readonly PrvTab[]>();
  readonly value = model<string>('');

  protected btnClass(id: string): string {
    return this.value() === id
      ? 'bg-white text-ink-900 shadow-sm dark:bg-surface-800 dark:text-white'
      : 'text-ink-500 hover:text-ink-900 dark:text-ink-300 dark:hover:text-white';
  }
}
