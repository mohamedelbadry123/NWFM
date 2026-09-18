import {
  ChangeDetectionStrategy,
  Component,
  input,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Consistent Workflow page header with eyebrow, title, subtitle, and action slot.
 * Surfaces follow html.dark so light mode is not stuck on charcoal panels.
 */
@Component({
  selector: 'app-workflow-page-header',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule, RouterLink],
  template: `
    <header class="relative mb-6 overflow-hidden rounded-2xl border border-ink-200 bg-white dark:border-dark-700 dark:bg-dark-800">
      <div class="pointer-events-none absolute inset-0 bg-gradient-to-br from-primary/10 via-transparent to-transparent dark:from-primary/20"></div>
      <div class="pointer-events-none absolute -end-8 -top-10 size-40 rounded-full bg-primary/10 blur-3xl dark:bg-primary/20"></div>
      <div class="relative flex flex-wrap items-end justify-between gap-4 px-5 py-5 sm:px-6">
        <div class="min-w-0">
          @if (eyebrowKey()) {
            <p class="mb-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-primary-600 dark:text-primary-200">
              {{ eyebrowKey()! | translate }}
            </p>
          }
          <h1 class="text-2xl font-bold tracking-tight text-ink-900 dark:text-white sm:text-[1.65rem]">{{ titleKey() | translate }}</h1>
          @if (subtitleKey()) {
            <p class="mt-1.5 max-w-2xl text-sm text-ink-500 dark:text-dark-300">{{ subtitleKey()! | translate }}</p>
          }
        </div>
        <div class="flex flex-wrap items-center gap-2">
          @if (helpLink()) {
            <a
              [routerLink]="helpLink()!"
              class="wf-btn-secondary !px-3.5 !py-2 text-xs">
              {{ 'workflow.ui.help' | translate }}
            </a>
          }
          <ng-content></ng-content>
        </div>
      </div>
    </header>
  `,
})
export class WorkflowPageHeaderComponent {
  readonly titleKey = input.required<string>();
  readonly subtitleKey = input<string | null>(null);
  readonly eyebrowKey = input<string | null>('workflow.ui.eyebrow');
  readonly helpLink = input<string | null>(null);
}
