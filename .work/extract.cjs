const fs = require('fs'), path = require('path');
const source = 'D:/Work/Privora-New-Tech-Stach', target = 'D:/Work/NWFM';
function files(dir) { return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e => ['bin','obj','node_modules','.git','Migrations','Seeding'].includes(e.name) ? [] : e.isDirectory() ? files(path.join(dir,e.name)) : [path.join(dir,e.name)]); }
function write(p,s) { fs.mkdirSync(path.dirname(p),{recursive:true}); fs.writeFileSync(p,s); }
function copy(rel,out=rel) { for(const f of files(path.join(source,rel))) { const dest=path.join(target,out,path.relative(path.join(source,rel),f)); let s=fs.readFileSync(f); if(/\.(cs|csproj|ts|html|css|json)$/.test(f)) s=s.toString().replaceAll('Privora.Shared','NWFM.Shared').replaceAll('Privora.UnitTests','NWFM.Tests'); write(dest,s); } }
copy('backend/src/Modules/Workflow');
for(const p of ['Domain','Results','Exceptions','Persistence','MultiTenancy','Integration/Workflow']) copy('backend/src/Privora.Shared/'+p,'backend/src/NWFM.Shared/'+p);
for(const p of ['Privora.Shared.csproj','Abstractions/ICurrentTenant.cs','Behaviors/ValidationBehavior.cs']) write(path.join(target,'backend/src/NWFM.Shared',p.replace('Privora.Shared','NWFM.Shared')),fs.readFileSync(path.join(source,'backend/src/Privora.Shared',p),'utf8').replaceAll('Privora.Shared','NWFM.Shared'));
for(const f of files(target+'/backend')) {
 if(!/\.cs$/.test(f)) continue;
 let s=fs.readFileSync(f,'utf8');
 s=s.replace(/^using Workflow.Infrastructure.Seeding;\r?\n/gm,'').replace(/^\s*services.AddScoped<WorkflowDesignerSeeder>\(\);\r?\n/gm,'');
 if(f.includes('Workflow.Api') && f.endsWith('Controller.cs')) {
  s=s.replace(/^using (Microsoft.AspNetCore.Authorization|System.Security.Claims);\r?\n/gm,'').replace(/^\s*\[Authorize[^\n]*\]\r?\n/gm,'\n');
  s=s.replace(': ControllerBase',': WorkflowControllerBase');
  s=s.replace(/Guid.TryParse\(User.FindFirstValue\("organizationId"\), out var id\) \? id : Guid.Empty/g,'Context.OrganizationId');
  s=s.replace(/Guid.TryParse\(User.FindFirstValue\("organizationId"\), out orgId\)/g,'TryGetOrganizationId(out orgId)');
  s=s.replaceAll('WorkflowPrincipal.GetUserId(User)','Context.ActorId').replaceAll('WorkflowPrincipal.TryGetUserId(User, out userId)','TryGetActorId(out userId)');
  s=s.replace(/User.IsInRole\("[^"]+"\)/g,'false');
  s=s.replaceAll('instanceId, Guid.Empty, GetUserId()', 'instanceId, GetOrgId(), GetUserId()');
  s=s.replaceAll('TryGetJwtOrganizationId','TryGetContextOrganizationId').replaceAll('IsSuperAdmin','HasCrossTenantAccess');
 }
 if(f.includes('/Repositories/') || f.includes('\\Repositories\\')) s=s.replace(/\.IgnoreQueryFilters\(\)/g,'');
 write(f,s);
}
console.log('Workflow and required shared backend sources extracted.');
