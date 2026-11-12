import { Routes } from '@angular/router';
import { provideDynamicForms } from '../../../shared/components/dynamic-form/provide-dynamic-forms';
import type { WorkflowDesignerComponent } from './workflow-designer.component';

/**
 * The workflow designer. Its Form tab previews Form Engine forms with the shared renderer, so Formly and the field
 * components are provided here — lazily, the same way the form engine and task routes provide them.
 *
 * Leaving the route by any means (a link, the browser's Back button, a menu entry) first settles the unsaved form
 * editors the designer holds: each offers Save draft, Discard or Keep editing, and Keep stays on the canvas.
 */
export const DESIGNER_ROUTES: Routes = [
  {
    path: '',
    providers: [provideDynamicForms()],
    loadComponent: () =>
      import('./workflow-designer.component').then((m) => m.WorkflowDesignerComponent),
    canDeactivate: [(component: WorkflowDesignerComponent) => component.canLeaveRoute()],
  },
];
