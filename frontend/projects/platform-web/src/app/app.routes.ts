import { Routes } from '@angular/router';
import {
  platformAdminGuard,
  platformGuard,
  platformGuestGuard,
} from './core/guards/platform.guard';

/**
 * The platform web app: the public landing page and self-service signup on the apex host, and the
 * staff console under /console. Workspace sign-in is never here — each workspace signs in on its
 * own host, served by the tenant app.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./features/landing/landing.component').then((m) => m.LandingComponent),
  },
  {
    path: 'signup',
    loadComponent: () =>
      import('./features/signup/signup.component').then((m) => m.SignupComponent),
  },
  {
    path: 'signup/verify',
    loadComponent: () =>
      import('./features/signup/signup-verify.component').then((m) => m.SignupVerifyComponent),
  },
  {
    path: 'signup/status/:id',
    loadComponent: () =>
      import('./features/signup/signup-status.component').then((m) => m.SignupStatusComponent),
  },
  {
    path: 'signup/database/:id',
    loadComponent: () =>
      import('./features/signup/signup-database.component').then((m) => m.SignupDatabaseComponent),
  },
  {
    path: 'console/login',
    canActivate: [platformGuestGuard],
    loadComponent: () =>
      import('./features/console/platform-login.component').then((m) => m.PlatformLoginComponent),
  },
  {
    path: 'console',
    canActivate: [platformGuard],
    loadComponent: () =>
      import('./features/console/platform-layout.component').then((m) => m.PlatformLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'tenants' },
      {
        path: 'tenants',
        data: { titleKey: 'platform.nav.tenants', subtitleKey: 'platform.tenants.subtitle' },
        loadComponent: () =>
          import('./features/console/platform-tenants.component').then(
            (m) => m.PlatformTenantsComponent,
          ),
      },
      {
        path: 'tenants/:id',
        data: {
          titleKey: 'platform.tenants.detail',
          subtitleKey: 'platform.tenants.detailSubtitle',
        },
        loadComponent: () =>
          import('./features/console/platform-tenant-detail.component').then(
            (m) => m.PlatformTenantDetailComponent,
          ),
      },
      {
        path: 'plans',
        data: { titleKey: 'platform.nav.plans', subtitleKey: 'platform.plans.subtitle' },
        loadComponent: () =>
          import('./features/console/platform-plans.component').then(
            (m) => m.PlatformPlansComponent,
          ),
      },
      {
        path: 'signups',
        data: { titleKey: 'platform.nav.signups', subtitleKey: 'platform.signups.subtitle' },
        loadComponent: () =>
          import('./features/console/platform-signups.component').then(
            (m) => m.PlatformSignupsComponent,
          ),
      },
      {
        path: 'database-servers',
        canActivate: [platformAdminGuard],
        data: {
          titleKey: 'platform.nav.databaseServers',
          subtitleKey: 'platform.databaseServers.subtitle',
        },
        loadComponent: () =>
          import('./features/console/platform-database-servers.component').then(
            (m) => m.PlatformDatabaseServersComponent,
          ),
      },
      {
        path: 'users',
        canActivate: [platformAdminGuard],
        data: { titleKey: 'platform.nav.users', subtitleKey: 'platform.users.subtitle' },
        loadComponent: () =>
          import('./features/console/platform-users.component').then(
            (m) => m.PlatformUsersComponent,
          ),
      },
      {
        path: 'audit',
        data: { titleKey: 'platform.nav.audit', subtitleKey: 'platform.audit.subtitle' },
        loadComponent: () =>
          import('./features/console/platform-audit.component').then(
            (m) => m.PlatformAuditComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
