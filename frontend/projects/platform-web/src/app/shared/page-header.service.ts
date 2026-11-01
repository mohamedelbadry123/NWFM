import { Injectable, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/**
 * Page header definition carried by a route (`data`) or pushed by a component at runtime.
 *
 * `*Key` values are transloco keys resolved by the layout template; `*Text` values are already
 * localized strings (used when the title comes from data, e.g. a template name).
 */
export interface PageHeader {
  readonly titleKey?: string;
  readonly titleText?: string;
  readonly subtitleKey?: string;
  readonly subtitleText?: string;
}

/** Route `data` keys read by {@link PageHeaderService}. */
export const PAGE_HEADER_ROUTE_DATA = {
  title: 'titleKey',
  subtitle: 'subtitleKey',
} as const;

/**
 * Single source of truth for the title/description shown in the dashboard topbar.
 *
 * Pages must not render their own title/description block — they declare `titleKey`/`subtitleKey`
 * in their route `data`, or call {@link set} for titles only known at runtime.
 */
@Injectable({ providedIn: 'root' })
export class PageHeaderService {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Header derived from the deepest activated route's `data`. */
  private readonly routeHeader = signal<PageHeader>({});

  /** Runtime override pushed by a page; cleared on every navigation. */
  private readonly override = signal<PageHeader | null>(null);

  readonly header = computed<PageHeader>(() => this.override() ?? this.routeHeader());

  constructor() {
    this.routeHeader.set(this.readDeepestHeader());

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => {
        this.override.set(null);
        this.routeHeader.set(this.readDeepestHeader());
      });
  }

  /**
   * Overrides the header for the current page (e.g. a loaded template name). The override is
   * dropped automatically on the next navigation.
   */
  set(header: PageHeader): void {
    this.override.set(header);
  }

  private readDeepestHeader(): PageHeader {
    let route = this.route.snapshot;
    let header: PageHeader = {};

    // Walk down the activated tree so a child route can refine its parent's header.
    while (route) {
      const titleKey = route.data[PAGE_HEADER_ROUTE_DATA.title] as string | undefined;
      const subtitleKey = route.data[PAGE_HEADER_ROUTE_DATA.subtitle] as string | undefined;

      if (titleKey || subtitleKey) {
        header = { titleKey: titleKey ?? header.titleKey, subtitleKey };
      }

      if (!route.firstChild) {
        break;
      }
      route = route.firstChild;
    }

    return header;
  }
}
