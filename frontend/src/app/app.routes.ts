import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent) },
  { path: 'auth/saml-callback', loadComponent: () => import('./features/auth/saml-callback/saml-callback.component').then(m => m.SamlCallbackComponent) },
  { path: 'auth/access-denied', loadComponent: () => import('./features/auth/access-denied/access-denied.component').then(m => m.AccessDeniedComponent) },
  { path: '', pathMatch: 'full', redirectTo: 'org/workflow' },
  { path: 'admin/workflow', pathMatch: 'full', redirectTo: 'admin/workflow/hub' },
  { path: 'workflow/start', canActivate: [authGuard], loadComponent: () => import('./features/workflow/start/workflow-start.component').then(m => m.WorkflowStartComponent) },
  { path: 'admin/workflow/hub', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-hub/admin-workflow-hub-page.component').then(m => m.AdminWorkflowHubPageComponent) },
  { path: 'admin/workflow/bindings', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-bindings/workflow-bindings-list.component').then(m => m.WorkflowBindingsListComponent) },
  { path: 'admin/workflow/bindings/new', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-bindings/workflow-binding-wizard.component').then(m => m.WorkflowBindingWizardComponent) },
  { path: 'admin/workflow/bindings/:bindingId', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-bindings/workflow-binding-detail.component').then(m => m.WorkflowBindingDetailComponent) },
  { path: 'admin/workflow/definitions', canActivate: [authGuard], loadComponent: () => import('./features/workflow/definitions/workflow-definitions.component').then(m => m.WorkflowDefinitionsComponent) },
  { path: 'admin/workflow/definitions/:definitionId/versions', canActivate: [authGuard], loadComponent: () => import('./features/workflow/versions/workflow-versions.component').then(m => m.WorkflowVersionsComponent) },
  { path: 'admin/workflow/definitions/:definitionId/versions/:versionId/designer', canActivate: [authGuard], loadComponent: () => import('./features/workflow/designer/workflow-designer.component').then(m => m.WorkflowDesignerComponent) },
  { path: 'admin/workflow/instances', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-instances/workflow-instances.component').then(m => m.WorkflowInstancesComponent) },
  { path: 'admin/workflow/instances/:id', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-instance-detail/workflow-instance-detail.component').then(m => m.WorkflowInstanceDetailComponent) },
  { path: 'admin/workflow/calendars', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-calendars/workflow-calendars.component').then(m => m.WorkflowCalendarsComponent) },
  { path: 'admin/workflow/sla-policies', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-sla-policies/workflow-sla-policies.component').then(m => m.WorkflowSlaPoliciesComponent) },
  { path: 'admin/workflow/incidents', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-incidents/workflow-incidents.component').then(m => m.WorkflowIncidentsComponent) },
  { path: 'admin/workflow/incidents/:id', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-incident-detail/workflow-incident-detail.component').then(m => m.WorkflowIncidentDetailComponent) },
  { path: 'admin/workflow/dead-letters', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-dead-letters/workflow-dead-letters.component').then(m => m.WorkflowDeadLettersComponent) },
  { path: 'admin/workflow/workload', canActivate: [authGuard], loadComponent: () => import('./features/workflow/workload/workflow-workload.component').then(m => m.WorkflowWorkloadComponent) },
  { path: 'admin/workflow/actions-catalog', canActivate: [authGuard], loadComponent: () => import('./features/admin/workflow-actions-catalog/workflow-actions-catalog.component').then(m => m.WorkflowActionsCatalogComponent) },
  { path: 'admin/workflow/systems', canActivate: [authGuard], data: { catalogView: 'systems' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
  { path: 'admin/workflow/modules', canActivate: [authGuard], data: { catalogView: 'modules' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
  { path: 'admin/workflow/screens', canActivate: [authGuard], data: { catalogView: 'screens' }, loadComponent: () => import('./features/admin/workflow-catalog/workflow-catalog.component').then(m => m.WorkflowCatalogComponent) },
  { path: 'admin/workflow/help', canActivate: [authGuard], loadComponent: () => import('./features/workflow/help/workflow-help-center.component').then(m => m.WorkflowHelpCenterComponent) },
  { path: 'org/workflow', canActivate: [authGuard], loadChildren: () => import('./features/workflow/routes').then(m => m.WORKFLOW_ROUTES) },
  { path: '**', redirectTo: 'org/workflow' }
];
