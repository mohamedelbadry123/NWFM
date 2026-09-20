import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/guards/auth.guard';
import { PERMISSIONS } from '../../core/auth/permissions';
import { provideDynamicForms } from '../../shared/components/dynamic-form/provide-dynamic-forms';

/**
 * Form engine screens. Designing, filling and reviewing are separate permissions, so the routes are
 * guarded individually rather than as one block.
 *
 * Formly and the custom field components are provided here rather than app-wide: they are a third of
 * a megabyte, and nothing outside these screens renders a form yet. A workflow task page that needs
 * one later adds the same call to its own route.
 */
export const FORM_ENGINE_ROUTES: Routes = [
  {
    path: '',
    providers: [provideDynamicForms()],
    children: [
      {
        path: '',
        canActivate: [permissionGuard(PERMISSIONS.viewForms)],
        data: { titleKey: 'forms.title', subtitleKey: 'forms.subtitle' },
        loadComponent: () => import('./list/form-list.component').then((m) => m.FormListComponent),
      },
      {
        path: 'published',
        canActivate: [permissionGuard(PERMISSIONS.submitForms, PERMISSIONS.viewForms)],
        data: { titleKey: 'forms.published.title', subtitleKey: 'forms.published.subtitle' },
        loadComponent: () => import('./published/published-forms.component').then((m) => m.PublishedFormsComponent),
      },
      {
        path: 'field-catalog',
        canActivate: [permissionGuard(PERMISSIONS.viewForms)],
        data: { titleKey: 'fieldCatalog.title', subtitleKey: 'fieldCatalog.subtitle' },
        loadComponent: () => import('./field-catalog/field-catalog.component').then((m) => m.FieldCatalogComponent),
      },
      {
        // The sandbox: the builder with no form behind it, for trying the palette out.
        path: 'sandbox',
        canActivate: [permissionGuard(PERMISSIONS.manageForms)],
        data: { titleKey: 'formBuilder.sandboxTitle', subtitleKey: 'formBuilder.sandboxSubtitle' },
        loadComponent: () => import('./builder/form-builder.component').then((m) => m.FormBuilderComponent),
      },
      {
        path: ':id/designer',
        canActivate: [permissionGuard(PERMISSIONS.manageForms)],
        data: { titleKey: 'formBuilder.title', subtitleKey: 'formBuilder.subtitle' },
        loadComponent: () => import('./builder/form-builder.component').then((m) => m.FormBuilderComponent),
      },
      {
        path: ':id/fill',
        canActivate: [permissionGuard(PERMISSIONS.submitForms)],
        data: { titleKey: 'forms.fill.title', subtitleKey: 'forms.fill.subtitle' },
        loadComponent: () => import('./fill/form-fill.component').then((m) => m.FormFillComponent),
      },
      {
        path: ':id/submissions',
        canActivate: [permissionGuard(PERMISSIONS.viewSubmissions)],
        data: { titleKey: 'forms.submissions.title', subtitleKey: 'forms.submissions.subtitle' },
        loadComponent: () => import('./submissions/form-submissions.component').then((m) => m.FormSubmissionsComponent),
      },
    ],
  },
];
