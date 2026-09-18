const fs=require('fs'),path=require('path');const edit=(p,f)=>fs.writeFileSync(p,f(fs.readFileSync(p,'utf8')));
const p='backend/src/Modules/Workflow/Workflow.Api/Controllers/WorkflowDefinitionsController.cs';
edit(p,s=>s.replace(/    private bool HasCrossTenantAccess => false;\r?\n/,'').replace('        if (HasCrossTenantAccess) return true;\r\n','').replace('        if (HasCrossTenantAccess) return true;\n','').replace(/        organizationId = Guid.Empty;[\s\S]*?\n        return true;\r?\n    \}/,`        organizationId = Context.OrganizationId;
        error = null;
        if (requested.HasValue && requested != organizationId) {
            error = BadRequest(new { Code = "Tenant.Mismatch", Message = "The requested tenant is not active." });
            return false;
        }
        return organizationId != Guid.Empty;
    }`));
edit('backend/src/Modules/Workflow/Workflow.Api/Controllers/WorkflowRuntimeController.cs',s=>s.replace('new GetWorkflowLiveGraphQuery(instanceId, Guid.Empty, SuperAdmin: true)','new GetWorkflowLiveGraphQuery(instanceId, GetOrgId(), SuperAdmin: false)'));
// Keep the selected-tenant UI; remove obsolete role-controlled selectors entirely.
function removeIf(s,condition){let start;while((start=s.indexOf('@if ('+condition+') {'))>=0){let open=s.indexOf('{',start),depth=1,i=open+1;for(;i<s.length&&depth;i++){if(s[i]==='{')depth++;if(s[i]==='}')depth--;}s=s.slice(0,start)+s.slice(i);}return s;}
edit('frontend/src/app/features/workflow/definitions/workflow-definitions.component.html',s=>removeIf(s,'isSuperAdmin()').replaceAll('!needsOrg() && ','').replace(/@if \(needsOrg\(\)\) \{[\s\S]*?\} @else \{/,'{'));
edit('frontend/src/app/features/workflow/definitions/workflow-definitions.component.ts',s=>{
 const begin=s.indexOf('    if (this.isSuperAdmin()) {');if(begin>=0){const end=s.indexOf('    this.load();',begin);s=s.slice(0,begin)+s.slice(end);}
 return s.replaceAll('this.isSuperAdmin() ? (this.selectedOrgId() || undefined) : undefined','undefined').replaceAll('this.isSuperAdmin() ? (v.organizationId || undefined) : undefined','undefined').replace(/^  protected readonly isSuperAdmin.*\r?\n/gm,'').replace('computed(() => this.isSuperAdmin() && !this.selectedOrgId())','computed(() => false)');
});
for(const lang of ['en','ar']){
const p='frontend/public/assets/i18n/'+lang+'.json';const j=JSON.parse(fs.readFileSync(p));
j.workflow.runtime.task_detail.reassign_hint=lang==='en'?'Move this task to another assignment group. The current claim is released.':'نقل المهمة إلى مجموعة إسناد أخرى وإلغاء الاستلام الحالي.';
fs.writeFileSync(p,JSON.stringify(j,null,2));}
