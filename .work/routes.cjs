const fs=require('fs');const base='frontend/src/app/app.routes.ts';
const routes=[
 ['hub','admin/workflow-hub/admin-workflow-hub-page','AdminWorkflowHubPageComponent'],
 ['bindings','admin/workflow-bindings/workflow-bindings-list','WorkflowBindingsListComponent'],
 ['bindings/new','admin/workflow-bindings/workflow-binding-wizard','WorkflowBindingWizardComponent'],
 ['bindings/:bindingId','admin/workflow-bindings/workflow-binding-detail','WorkflowBindingDetailComponent'],
 ['definitions','workflow/definitions/workflow-definitions','WorkflowDefinitionsComponent'],
 ['definitions/:definitionId/versions','workflow/versions/workflow-versions','WorkflowVersionsComponent'],
 ['definitions/:definitionId/versions/:versionId/designer','workflow/designer/workflow-designer','WorkflowDesignerComponent'],
 ['instances','admin/workflow-instances/workflow-instances','WorkflowInstancesComponent'],
 ['instances/:id','admin/workflow-instance-detail/workflow-instance-detail','WorkflowInstanceDetailComponent'],
 ['calendars','admin/workflow-calendars/workflow-calendars','WorkflowCalendarsComponent'],
 ['sla-policies','admin/workflow-sla-policies/workflow-sla-policies','WorkflowSlaPoliciesComponent'],
 ['incidents','admin/workflow-incidents/workflow-incidents','WorkflowIncidentsComponent'],
 ['incidents/:id','admin/workflow-incident-detail/workflow-incident-detail','WorkflowIncidentDetailComponent'],
 ['dead-letters','admin/workflow-dead-letters/workflow-dead-letters','WorkflowDeadLettersComponent'],
 ['workload','workflow/workload/workflow-workload','WorkflowWorkloadComponent'],
 ['actions-catalog','admin/workflow-actions-catalog/workflow-actions-catalog','WorkflowActionsCatalogComponent'],
 ['systems','admin/workflow-catalog/workflow-catalog','WorkflowCatalogComponent'],
 ['modules','admin/workflow-catalog/workflow-catalog','WorkflowCatalogComponent'],
 ['screens','admin/workflow-catalog/workflow-catalog','WorkflowCatalogComponent'],
 ['help','workflow/help/workflow-help-center','WorkflowHelpCenterComponent']
];
fs.writeFileSync(base,`import { Routes } from '@angular/router';
export const routes: Routes = [
 {path:'',pathMatch:'full',redirectTo:'org/workflow'},
 {path:'admin/workflow',pathMatch:'full',redirectTo:'admin/workflow/hub'},
 {path:'workflow/start',loadComponent:()=>import('./features/workflow/start/workflow-start.component').then(m=>m.WorkflowStartComponent)},
 ${routes.map(([url,file,type])=>`{path:'admin/workflow/${url}',${['systems','modules','screens'].includes(url)?`data:{catalogView:'${url}'},`:''}loadComponent:()=>import('./features/${file}.component').then(m=>m.${type})}`).join(',\n')},
 {path:'org/workflow',loadChildren:()=>import('./features/workflow/routes').then(m=>m.WORKFLOW_ROUTES)},
 {path:'**',redirectTo:'org/workflow'}
];`);
