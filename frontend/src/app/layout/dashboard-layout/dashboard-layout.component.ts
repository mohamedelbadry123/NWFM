import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';

import { AuthService } from '../../core/auth/auth.service';
import { AuthStore } from '../../core/auth/auth.store';
import { ADMINISTRATOR_ROLE, PERMISSIONS } from '../../core/auth/permissions';
import { LocaleService } from '../../core/i18n/locale.service';
import { PageHeaderService } from '../../core/layout/page-header.service';
import { ThemeService } from '../../core/theme/theme.service';
import { ToastService } from '../../core/notifications/toast.service';

interface NavChild {
  readonly labelKey: string;
  readonly route: string;
  readonly permissions?: readonly string[];
  readonly exact?: boolean;
}

interface NavItem {
  readonly labelKey: string;
  readonly icon: string;
  readonly route?: string;
  readonly permissions?: readonly string[];
  readonly children?: readonly NavChild[];
}

@Component({
  selector: 'app-dashboard-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslateModule, Menu],
  templateUrl: './dashboard-layout.component.html',
  styleUrl: './dashboard-layout.component.scss',
})
export class DashboardLayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly store = inject(AuthStore);
  protected readonly locale = inject(LocaleService);
  protected readonly theme = inject(ThemeService);
  protected readonly pageHeader = inject(PageHeaderService);
  protected readonly toast = inject(ToastService);
  private readonly translate = inject(TranslateService);

  protected readonly sidebarOpen = signal(true);
  private readonly expandedGroups = signal<ReadonlySet<string>>(new Set());

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly navItems: readonly NavItem[] = [
    {
      labelKey: 'nav.operations',
      icon: 'pi pi-th-large',
      children: [
        { labelKey: 'nav.overview', route: '/org/workflow', exact: true },
        { labelKey: 'nav.startWorkflow', route: '/workflow/start' },
        { labelKey: 'nav.tasks', route: '/org/workflow/tasks' },
        { labelKey: 'nav.requests', route: '/org/workflow/requests' },
        { labelKey: 'nav.workload', route: '/org/workflow/workload' },
        { labelKey: 'nav.notifications', route: '/org/workflow/notifications' },
        { labelKey: 'nav.help', route: '/org/workflow/help' },
      ],
    },
    {
      labelKey: 'nav.organization',
      icon: 'pi pi-sitemap',
      children: [
        { labelKey: 'nav.participants', route: '/org/workflow/participants' },
        { labelKey: 'nav.assignmentGroups', route: '/org/workflow/assignment-groups' },
        { labelKey: 'nav.departments', route: '/org/workflow/departments' },
      ],
    },
    {
      labelKey: 'nav.administration',
      icon: 'pi pi-cog',
      children: [
        { labelKey: 'nav.definitions', route: '/admin/workflow/definitions' },
        { labelKey: 'nav.bindings', route: '/admin/workflow/bindings' },
        { labelKey: 'nav.instances', route: '/admin/workflow/instances' },
        { labelKey: 'nav.incidents', route: '/admin/workflow/incidents' },
        { labelKey: 'nav.deadLetters', route: '/admin/workflow/dead-letters' },
        { labelKey: 'nav.calendars', route: '/admin/workflow/calendars' },
        { labelKey: 'nav.slaPolicies', route: '/admin/workflow/sla-policies' },
        { labelKey: 'nav.actionsCatalog', route: '/admin/workflow/actions-catalog' },
        { labelKey: 'nav.workflowCatalog', route: '/admin/workflow/modules' },
      ],
    },
    {
      labelKey: 'nav.fieldTasks',
      icon: 'pi pi-map-marker',
      children: [
        {
          labelKey: 'nav.fieldTaskList',
          route: '/tasks',
          exact: true,
          permissions: [PERMISSIONS.viewTasks, ADMINISTRATOR_ROLE],
        },
        {
          labelKey: 'nav.taskTypes',
          route: '/tasks/types',
          permissions: [PERMISSIONS.manageTaskTypes, ADMINISTRATOR_ROLE],
        },
      ],
    },
    {
      labelKey: 'nav.forms',
      icon: 'pi pi-file-edit',
      children: [
        {
          labelKey: 'nav.formsList',
          route: '/forms',
          exact: true,
          permissions: [PERMISSIONS.viewForms, ADMINISTRATOR_ROLE],
        },
        {
          labelKey: 'nav.fillForm',
          route: '/forms/published',
          permissions: [PERMISSIONS.submitForms, PERMISSIONS.viewForms, ADMINISTRATOR_ROLE],
        },
        {
          labelKey: 'nav.fieldCatalog',
          route: '/forms/field-catalog',
          permissions: [PERMISSIONS.viewForms, ADMINISTRATOR_ROLE],
        },
      ],
    },
    {
      labelKey: 'nav.lookups',
      icon: 'pi pi-list',
      route: '/lookups',
      permissions: [PERMISSIONS.manageLookups, ADMINISTRATOR_ROLE],
    },
    {
      labelKey: 'nav.teams',
      icon: 'pi pi-users',
      route: '/admin/teams',
      permissions: [PERMISSIONS.manageTeams, ADMINISTRATOR_ROLE],
    },
    {
      labelKey: 'nav.users',
      icon: 'pi pi-user-edit',
      route: '/admin/users',
      permissions: [PERMISSIONS.manageUsers, ADMINISTRATOR_ROLE],
    },
    {
      labelKey: 'nav.rolePermissions',
      icon: 'pi pi-shield',
      route: '/admin/roles',
      permissions: [PERMISSIONS.manageRolePermissions, ADMINISTRATOR_ROLE],
    },
  ];

  protected readonly userMenu = computed<MenuItem[]>(() => {
    this.locale.locale();
    return [
      {
        label: this.store.userName() ?? 'User',
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
    const name = this.store.userName() ?? 'U';
    return name.slice(0, 2).toUpperCase();
  });

  protected canSee(permissions?: readonly string[]): boolean {
    if (!permissions || permissions.length === 0) return true;
    if (this.store.roles().includes(ADMINISTRATOR_ROLE)) return true;
    return this.store.hasAnyPermission(...permissions);
  }

  protected toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  protected isGroupActive(item: NavItem): boolean {
    const url = this.currentUrl();
    return (item.children ?? []).some((child) => url === child.route || url.startsWith(`${child.route}/`));
  }

  protected isGroupExpanded(item: NavItem): boolean {
    return this.isGroupActive(item) || this.expandedGroups().has(item.labelKey);
  }

  protected toggleGroup(item: NavItem): void {
    if (!this.sidebarOpen()) {
      this.sidebarOpen.set(true);
    }

    this.expandedGroups.update((current) => {
      const next = new Set(current);
      if (next.has(item.labelKey)) next.delete(item.labelKey);
      else next.add(item.labelKey);
      return next;
    });
  }

  protected toggleLanguage(): void {
    this.locale.toggle();
  }

  protected toggleTheme(): void {
    this.theme.toggle();
  }

  protected logout(): void {
    const refreshToken = this.store.refreshToken();
    this.auth.logout(refreshToken ?? undefined).subscribe({
      complete: () => this.finishLogout(),
      error: () => this.finishLogout(),
    });
  }

  private finishLogout(): void {
    this.store.clearSession();
    this.router.navigate(['/login']);
  }
}
