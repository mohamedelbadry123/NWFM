import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HugeiconsIconComponent } from '@hugeicons/angular';
import { ArrowRight01Icon } from '@hugeicons/core-free-icons';

export interface PrvCrumb {
  readonly label: string;
  /** Router link; omit for the current (last) page. */
  readonly link?: string;
}

/**
 * prv-breadcrumb — page title + trail. Adapted from TailAdmin's page-breadcrumb.
 * The chevron is a HugeIcons glyph and mirrors under RTL automatically.
 *
 *   <prv-breadcrumb pageTitle="RoPA Registry"
 *     [crumbs]="[{label:'Home',link:'/'},{label:'RoPA Registry'}]" />
 */
@Component({
  selector: 'prv-breadcrumb',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, HugeiconsIconComponent],
  styles: [':host { display: block; }'],
  template: `
    <div class="flex flex-wrap items-center justify-between gap-3 mb-6">
      <h2 class="text-xl font-semibold text-ink-900 dark:text-white/90">{{ pageTitle() }}</h2>
      <nav aria-label="Breadcrumb">
        <ol class="flex items-center gap-1.5">
          @for (c of crumbs(); track $index; let last = $last) {
            <li class="flex items-center gap-1.5">
              @if (c.link && !last) {
                <a [routerLink]="c.link" class="text-sm text-ink-500 hover:text-primary dark:text-ink-300">{{ c.label }}</a>
                <hugeicons-icon [icon]="chevron" [size]="14" class="text-ink-300 rtl:-scale-x-100" />
              } @else {
                <span class="text-sm text-ink-800 dark:text-white/90">{{ c.label }}</span>
              }
            </li>
          }
        </ol>
      </nav>
    </div>
  `,
})
export class PrvBreadcrumbComponent {
  readonly pageTitle = input('');
  readonly crumbs = input<readonly PrvCrumb[]>([]);
  protected readonly chevron = ArrowRight01Icon;
}
