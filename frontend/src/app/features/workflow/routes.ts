import { Routes } from '@angular/router';

// Org-level workflow routes: participants, groups, departments, and runtime screens.
// Design-time routes (definitions, versions, designer) live under /admin/workflow.
export const WORKFLOW_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./hub/org-workflow-hub-page.component').then(
        m => m.OrgWorkflowHubPageComponent
      ),
  },
  {
    path: 'participants',    loadComponent: () =>
      import('./participants/workflow-participants.component').then(
        m => m.WorkflowParticipantsComponent
      ),
  },
  {
    path: 'assignment-groups',    loadComponent: () =>
      import('./assignment-groups/workflow-assignment-groups.component').then(
        m => m.WorkflowAssignmentGroupsComponent
      ),
  },
  {
    path: 'departments',    loadComponent: () =>
      import('./departments/workflow-departments.component').then(
        m => m.WorkflowDepartmentsComponent
      ),
  },
  // ── Runtime screens ──────────────────────────────────────────────────────
  {
    path: 'tasks',    loadComponent: () =>
      import('./tasks/workflow-tasks.component').then(
        m => m.WorkflowTasksComponent
      ),
  },
  {
    path: 'my-tasks',    loadComponent: () =>
      import('./tasks/workflow-tasks-redirect.component').then(
        m => m.WorkflowTasksRedirectComponent
      ),
  },
  {
    path: 'group-inbox',    loadComponent: () =>
      import('./tasks/workflow-tasks-redirect.component').then(
        m => m.WorkflowTasksRedirectComponent
      ),
  },
  {
    path: 'tasks/:id',    loadComponent: () =>
      import('./task-detail/workflow-task-detail.component').then(
        m => m.WorkflowTaskDetailComponent
      ),
  },
  {
    path: 'requests',    loadComponent: () =>
      import('./requests/workflow-requests.component').then(
        m => m.WorkflowRequestsComponent
      ),
  },
  {
    path: 'requests/:instanceId',    loadComponent: () =>
      import('./request-progress/workflow-request-progress.component').then(
        m => m.WorkflowRequestProgressComponent
      ),
  },
  {
    path: 'workload',    loadComponent: () =>
      import('./workload/workflow-workload.component').then(
        m => m.WorkflowWorkloadComponent
      ),
  },
  {
    path: 'help',
    loadComponent: () =>
      import('./help/workflow-help-center.component').then(
        m => m.WorkflowHelpCenterComponent
      ),
  },
  {
    path: 'notifications',    loadComponent: () =>
      import('./notifications/workflow-notifications.component').then(
        m => m.WorkflowNotificationsComponent
      ),
  },
];
