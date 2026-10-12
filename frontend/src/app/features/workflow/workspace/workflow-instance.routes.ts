import { Routes } from '@angular/router';
import { provideDynamicForms } from '@shared/components/dynamic-form/provide-dynamic-forms';

export const WORKFLOW_INSTANCE_ROUTES: Routes = [
  {
    path: '',
    providers: [provideDynamicForms()],
    loadComponent: () =>
      import('./workflow-instance-workspace.component').then(
        (m) => m.WorkflowInstanceWorkspaceComponent,
      ),
  },
];
