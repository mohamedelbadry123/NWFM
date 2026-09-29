import { Routes } from '@angular/router';
import { provideDynamicForms } from '../../../shared/components/dynamic-form/provide-dynamic-forms';

/**
 * The workflow designer. Its Form tab previews Form Engine forms with the shared renderer, so Formly and the field
 * components are provided here — lazily, the same way the form engine and task routes provide them.
 */
export const DESIGNER_ROUTES: Routes = [
  {
    path: '',
    providers: [provideDynamicForms()],
    loadComponent: () => import('./workflow-designer.component').then(m => m.WorkflowDesignerComponent),
  },
];
