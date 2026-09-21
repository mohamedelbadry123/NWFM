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
      {
        path: 'types',
        canActivate: [permissionGuard(PERMISSIONS.manageTaskTypes)],
        data: { titleKey: 'taskTypes.title', subtitleKey: 'taskTypes.subtitle' },
        loadComponent: () => import('./types/task-type-list.component').then((m) => m.TaskTypeListComponent),
      },
      {
        path: 'c2m-actions',
        canActivate: [permissionGuard(PERMISSIONS.manageTaskTypes)],
        data: { titleKey: 'c2mMappings.title', subtitleKey: 'c2mMappings.subtitle' },
        loadComponent: () =>
          import('./c2m/c2m-action-mapping-list.component').then((m) => m.C2mActionMappingListComponent),
      },
    ],
  },
];
