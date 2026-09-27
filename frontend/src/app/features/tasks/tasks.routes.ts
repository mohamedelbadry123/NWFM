import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/guards/auth.guard';
import { PERMISSIONS } from '../../core/auth/permissions';
import { provideDynamicForms } from '../../shared/components/dynamic-form/provide-dynamic-forms';

/**
 * Field task screens. The worklist fills and shows forms, so Formly and the field components are
 * provided here, the same way the form engine's own routes provide them.
 */
export const TASKS_ROUTES: Routes = [
  {
    path: '',
    providers: [provideDynamicForms()],
    children: [
      {
        path: '',
        canActivate: [permissionGuard(PERMISSIONS.viewTasks)],
        data: { titleKey: 'tasks.title', subtitleKey: 'tasks.subtitle' },
        loadComponent: () => import('./list/task-list.component').then((m) => m.TaskListComponent),
      },
      // Task types and C2M action mappings are tabs of Lookups now; old links land on them.
      { path: 'types', redirectTo: '/lookups?tab=task-types' },
      { path: 'c2m-actions', redirectTo: '/lookups?tab=c2m-actions' },
    ],
  },
];
