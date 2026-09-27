import { Injectable, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

export interface PageHeader {
  readonly titleKey?: string;
  readonly titleText?: string;
  readonly subtitleKey?: string;
  readonly subtitleText?: string;
}

export const PAGE_HEADER_ROUTE_DATA = {
  title: 'titleKey',
  subtitle: 'subtitleKey',
} as const;

@Injectable({ providedIn: 'root' })
export class PageHeaderService {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly routeHeader = signal<PageHeader>({});
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

  set(header: PageHeader): void {
    this.override.set(header);
  }

  private readDeepestHeader(): PageHeader {
    let route = this.route.snapshot;
    let header: PageHeader = {};

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
