import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import {
  I18nDirective,
  LanguageService,
  PageHeaderService,
  ThemeService,
  NWFM_LOGO_MARK,
} from '@platform/shared';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';

interface PlatformNavItem {
  readonly labelKey: string;
  readonly icon: string;
  readonly route: string;
  /** Mirrors `platformAdminGuard` on the route — support staff never see an item the route refuses. */
  readonly adminOnly: boolean;
}

/**
 * The NWFM staff console. Same shell as the tenant app (`layout/_app-shell.scss`), gated by the
 * platform role rather than tenant permissions.
 */
@Component({
  selector: 'app-platform-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, I18nDirective, Menu],
  templateUrl: './platform-layout.component.html',
  styleUrl: './platform-layout.component.scss',
})
export class PlatformLayoutComponent {
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  protected readonly store = inject(PlatformAuthStore);
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);
  protected readonly pageHeader = inject(PageHeaderService);
  protected readonly logoMark = NWFM_LOGO_MARK;

  protected readonly sidebarOpen = signal(true);

  private readonly allNavItems: readonly PlatformNavItem[] = [
    {
      labelKey: 'platform.nav.tenants',
      icon: 'pi pi-building',
      route: '/console/tenants',
      adminOnly: false,
    },
    {
      labelKey: 'platform.nav.signups',
      icon: 'pi pi-inbox',
      route: '/console/signups',
      adminOnly: false,
    },
    {
      labelKey: 'platform.nav.plans',
      icon: 'pi pi-tags',
      route: '/console/plans',
      adminOnly: false,
    },
    {
      labelKey: 'platform.nav.databaseServers',
      icon: 'pi pi-database',
      route: '/console/database-servers',
      adminOnly: true,
    },
    {
      labelKey: 'platform.nav.users',
      icon: 'pi pi-users',
      route: '/console/users',
      adminOnly: true,
    },
    {
      labelKey: 'platform.nav.audit',
      icon: 'pi pi-history',
      route: '/console/audit',
      adminOnly: false,
    },
  ];

  protected readonly navItems = computed(() => {
    const isAdmin = this.store.isAdmin();
    return this.allNavItems.filter((item) => isAdmin || !item.adminOnly);
  });

  protected readonly roleKey = computed(() =>
    this.store.isAdmin()
      ? 'platform.users.roles.PlatformAdmin'
      : 'platform.users.roles.PlatformSupport',
  );

  protected readonly userMenu = computed<MenuItem[]>(() => {
    // Read the active language so the menu re-translates on language change.
    this.language.current();
    return [
      {
        label: this.store.displayName() ?? this.store.email() ?? '',
        items: [
          {
            label: this.translate.instant('common.logout'),
            icon: 'pi pi-sign-out',
            command: () => this.logout(),
          },
        ],
      },
    ];
  });

  protected readonly initials = computed(() => {
    const name = this.store.displayName() ?? this.store.email() ?? 'E';
    return name.slice(0, 2).toUpperCase();
  });

  protected toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  protected logout(): void {
    this.store.clear();
    void this.router.navigate(['/console/login']);
  }
}
