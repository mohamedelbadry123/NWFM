import { workflowHomeGuard, workflowWorkspaceGuard } from './core/guards/workflow-workspace.guard';
import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard } from './core/guards/auth.guard';
import { PERMISSIONS } from './core/auth/permissions';
import { DashboardLayoutComponent } from './layout/dashboard-layout/dashboard-layout.component';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent) },
  { path: 'auth/saml-callback', loadComponent: () => import('./features/auth/saml-callback/saml-callback.component').then(m => m.SamlCallbackComponent) },
  { path: 'auth/access-denied', loadComponent: () => import('./features/auth/access-denied/access-denied.component').then(m => m.AccessDeniedComponent) },
  {
    path: '',
    component: DashboardLayoutComponent,
    canActivate: [authGuard],
    canActivateChild: [workflowWorkspaceGuard],
    children: [
      { path: 'admin/workflow/integrations', canActivate: [permissionGuard(PERMISSIONS.manageDefinitions)], loadComponent: () => import('./features/workflow/integrations/workflow-connections.component').then(m => m.WorkflowConnectionsComponent) },
      { path: '', pathMatch: 'full', canActivate: [workflowHomeGuard], loadComponent: () => import('./features/workflow/workspace/workflow-workspace.component').then(m => m.WorkflowWorkspaceComponent) },
      { path: 'admin/workflow', pathMatch: 'full', redirectTo: 'admin/workflow/hub' },
      { path: 'workflow/start', data: {workspaceView: 'start'}, canActivate: [permissionGuard(PERMISSIONS.startWorkflows)], loadComponent: () => import('./features/workflow/workspace/workflow-workspace.component').then(m => m.WorkflowWorkspaceComponent) },
      { path: 'admin/workflow/hub', loadComponent: () => import('./features/admin/workflow-hub/admin-workflow-hub-page.component').then(m => m.AdminWorkflowHubPageComponent) },
      { path: 'admin/workflow/bindings', loadComponent: () => import('./features/admin/workflow-bindings/workflow-bindings-list.component').then(m => m.WorkflowBindingsListComponent) },
      { path: 'admin/workflow/bindings/new', loadComponent: () => import('./features/admin/workflow-bindings/workflow-binding-wizard.component').then(m => m.WorkflowBindingWizardComponent) },
      { path: 'admin/workflow/bindings/:bindingId', loadComponent: () => import('./features/admin/workflow-bindings/workflow-binding-detail.component').then(m => m.WorkflowBindingDetailComponent) },
      { path: 'admin/workflow/definitions', data: {workspaceView: 'workflows'}, canActivate: [permissionGuard(PERMISSIONS.manageDefinitions)], loadComponent: () => import('./features/workflow/workspace/workflow-workspace.component').then(m => m.WorkflowWorkspaceComponent) },
      { path: 'admin/workflow/definitions/:definitionId/versions', loadComponent: () => import('./features/workflow/versions/workflow-versions.component').then(m => m.WorkflowVersionsComponent) },
      { path: 'admin/workflow/definitions/:definitionId/versions/:versionId/designer', loadComponent: () => import('./features/workflow/designer/workflow-designer.component').then(m => m.WorkflowDesignerComponent) },
      { path: 'admin/workflow/instances', data: {workspaceView: 'instances'}, canActivate: [permissionGuard(PERMISSIONS.viewInstances)], loadComponent: () => import('./features/workflow/workspace/workflow-workspace.component').then(m => m.WorkflowWorkspaceComponent) },
      { path: 'admin/workflow/instances/:id', canActivate: [permissionGuard(PERMISSIONS.viewInstances)], loadComponent: () => import('./features/workflow/workspace/workflow-instance-workspace.component').then(m => m.WorkflowInstanceWorkspaceComponent) },
      { path: 'admin/workflow/calendars', loadComponent: () => import('./features/admin/workflow-calendars/workflow-calendars.component').then(m => m.WorkflowCalendarsComponent) },
      { path: 'admin/workflow/sla-policies', canActivate: [permissionGuard(PERMISSIONS.manageSlaPolicies)], loadComponent: () => import('./features/workflow/workspace/workflow-sla-rules.component').then(m => m.WorkflowSlaRulesComponent) },
      { path: 'admin/workflow/incidents', loadComponent: () => import('./features/admin/workflow-incidents/workflow-incidents.component').then(m => m.WorkflowIncidentsComponent) },
      { path: 'admin/workflow/incidents/:id', loadComponent: () => import('./features/admin/workflow-incident-detail/workflow-incident-detail.component').then(m => m.WorkflowIncidentDetailComponent) },
      { path: 'admin/workflow/dead-letters', loadComponent: () => import('./features/admin/workflow-dead-letters/workflow-dead-letters.component').then(m => m.WorkflowDeadLettersComponent) },
      { path: 'admin/workflow/workload', loadComponent: () => import('./features/workflow/workload/workflow-workload.component').then(m => m.WorkflowWorkloadComponent) },
      { path: 'admin/workflow/actions-catalog', loadComponent: () => import('./features/admin/workflow-actions-catalog/workflow-actions-catalog.component').then(m => m.WorkflowActionsCatalogComponent) },
      { path: 'admin/workflow/systems', data: { catalogView: 'systems' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
      { path: 'admin/workflow/modules', data: { catalogView: 'modules' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
      { path: 'admin/workflow/screens', data: { catalogView: 'screens' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
      { path: 'admin/workflow/help', loadComponent: () => import('./features/workflow/help/workflow-help-center.component').then(m => m.WorkflowHelpCenterComponent) },
      { path: 'org/workflow', loadChildren: () => import('./features/workflow/routes').then(m => m.WORKFLOW_ROUTES) },
      {
        path: 'lookups',
        canActivate: [permissionGuard(PERMISSIONS.manageLookups)],
        data: { titleKey: 'lookups.title', subtitleKey: 'lookups.subtitle' },
        loadComponent: () => import('./features/lookups/lookups.component').then(m => m.LookupsComponent),
      },
      {
        path: 'admin/users',
        canActivate: [permissionGuard(PERMISSIONS.manageUsers)],
        data: { titleKey: 'users.title', subtitleKey: 'users.subtitle' },
        loadComponent: () => import('./features/admin/users/user-list.component').then(m => m.UserListComponent),
      },
      {
        path: 'admin/roles',
        canActivate: [permissionGuard(PERMISSIONS.manageRolePermissions)],
        data: { titleKey: 'admin.rolePermissions.title', subtitleKey: 'admin.rolePermissions.subtitle' },
        loadComponent: () => import('./features/admin/role-permissions/role-permissions.component').then(m => m.RolePermissionsComponent),
      },
      { path: '**', redirectTo: 'admin/workflow/definitions' },
    ],
  },
];
