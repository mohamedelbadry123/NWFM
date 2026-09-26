// Disposable database, Auth seed and WorkflowDemo enabled.
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {loginForWorkflowTest,ensureTestParticipant} from './workflow-test-auth.mjs';
const base=process.env.NWFM_API_URL||'http://localhost:5080';
const token=await loginForWorkflowTest(base);
async function request(path,method='GET',body,status=200,headers={}){
 const response=await fetch(base+'/api/'+path,{method,headers:{'Content-Type':'application/json',Authorization:'Bearer '+token,...headers},body:body===undefined?undefined:JSON.stringify(body)});
 const text=await response.text();assert.equal(response.status,status,method+' '+path+': '+text);return text?JSON.parse(text):null;
}
for(const path of ['workflow/definitions','workflow/participants','workflow/assignment-groups','workflow/departments','workflow/bindings','workflow/calendars','workflow/sla-policies','workflow/incidents','workflow/module-catalog','workflow/actions-catalog','workflow/runtime/admin/instances','workflow/workload','workflow/notifications','workflow/requests'])await request(path);
await request('workflow/definitions','GET',undefined,400,{'X-Organization-Id':randomUUID()});
await request('workflow/definitions?organizationId='+randomUUID(),'GET',undefined,400);
await request('workflow/definitions','POST',{organizationId:randomUUID(),definitionKey:'FOREIGN',name:'Foreign'},400);
const group=(await request('workflow/assignment-groups')).items.find(g=>g.code==='REVIEWERS');assert.ok(group);
await ensureTestParticipant(request,token,group);
const cluster=(await request('workflow/workspace/lookups/clusters'))[0].code;
const name='smoke-'+randomUUID().slice(0,8)+'@example.test',password='Smoke-Test1!';
const user=(await request('v1/users','POST',{userName:name,email:name,password,roles:['Participant'],scopes:[{level:'Cluster',code:cluster}]},201)).value;
const participant=await request('workflow/participants','POST',{userId:user.id,displayName:'Smoke participant',email:name},201);
await request('workflow/assignment-groups/'+group.id+'/members','POST',{participantId:participant.id},201);
const otherToken=await loginForWorkflowTest(base,name,password);
const simple=(await request('workflow/definitions?pageSize=200')).items.find(d=>d.definitionKey==='SIMPLE_APPROVAL');
const binding=(await request('workflow/bindings?pageSize=200')).items.find(b=>b.workflowDefinitionId===simple.id&&b.isActive);
const key=randomUUID(),body={workflowBindingId:binding.id,businessEntityId:'SMOKE-'+key,idempotencyKey:key};
const started=await request('workflow/runtime/instances','POST',body,201);
await request('workflow/runtime/instances','POST',body,400);
const task=(await request('workflow/work-items/available')).find(t=>t.workflowInstanceId===started.id);assert.ok(task);
async function action(token,action,body){const r=await fetch(base+'/api/workflow/work-items/'+task.id+'/'+action,{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify(body||{})});return r.status;}
const tokens=[token,otherToken];const results=await Promise.all(tokens.map(t=>action(t,'claim')));assert.equal(results.filter(s=>s===200).length,1);
const winner=results.indexOf(200);assert.equal(await action(tokens[1-winner],'complete',{actionTaken:'Approve'}),400);
assert.equal(await action(tokens[winner],'complete',{actionTaken:'Approve',comment:'Authenticated smoke test'}),200);
assert.equal((await request('workflow/runtime/instances/'+started.id)).status,'Completed');
assert.equal((await request('workflow/requests/by-instance/'+started.id)).status,'Completed');
await request('workflow/participants/'+participant.id,'DELETE',undefined,204);
console.log('PASS: authenticated API catalog, tenant isolation, real-user assignments, concurrent claims, ownership and standalone execution.');
