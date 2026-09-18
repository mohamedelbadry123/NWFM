import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const base = process.env.NWFM_API_URL || 'http://localhost:5080';
async function request(path, { method='GET', participant, body, headers={} }={}) {
  const response=await fetch(base+path,{method,headers:{...(body?{'Content-Type':'application/json'}:{}),...(participant?{'X-Workflow-Participant-Id':participant}:{}),...headers},body:body?JSON.stringify(body):undefined});
  const text=await response.text(); let data;try{data=JSON.parse(text)}catch{data=text}
  return {status:response.status,data};
}
const context=await request('/api/app-context');assert.equal(context.status,200);assert.equal(context.data.tenant.name,'NWFM');assert.ok(context.data.participants.length>=2);
for(const path of ['/api/workflow/definitions','/api/workflow/participants','/api/workflow/assignment-groups','/api/workflow/departments','/api/workflow/bindings','/api/workflow/calendars','/api/workflow/sla-policies','/api/workflow/incidents','/api/workflow/module-catalog','/api/workflow/actions-catalog','/api/workflow/runtime/admin/instances','/api/workflow/workload','/api/workflow/notifications','/api/workflow/requests']) {
  const response=await request(path);assert.equal(response.status,200,path+': '+JSON.stringify(response.data));
}
assert.equal((await request('/api/workflow/definitions',{headers:{'x-organization-id':randomUUID()}})).status,400);
assert.equal((await request('/api/workflow/definitions?organizationId='+randomUUID())).status,400);
assert.equal((await request('/api/workflow/work-items/my',{participant:randomUUID()})).status,400);
assert.equal((await request('/api/workflow/definitions',{method:'POST',body:{organizationId:randomUUID(),definitionKey:'FOREIGN',name:'Foreign tenant'}})).status,400);
assert.equal((await request('/api/workflow/participants/'+context.data.defaultParticipantId,{method:'DELETE'})).status,409,'The default actor must remain available');
const participant=await request('/api/workflow/participants',{method:'POST',body:{displayName:'Smoke test participant',email:`smoke-${randomUUID()}@example.test`}});
assert.equal(participant.status,201,JSON.stringify(participant.data));
assert.equal((await request('/api/workflow/work-items/my',{participant:participant.data.id})).status,200);
assert.equal((await request('/api/workflow/participants/'+participant.data.id,{method:'DELETE'})).status,204);
assert.equal((await request('/api/workflow/work-items/my',{participant:participant.data.id})).status,400,'An inactive participant cannot act');
assert.equal((await request('/api/app-context')).status,200,'Participant deactivation must not block startup');
const bindings=(await request('/api/workflow/bindings')).data.items;
const binding=bindings.find(b=>b.moduleKey==='Standalone'&&b.isActive&&b.mode==='Active');assert.ok(binding);
const key=randomUUID();const startBody={workflowBindingId:binding.id,businessEntityId:'SMOKE-'+key,idempotencyKey:key};
const started=await request('/api/workflow/runtime/instances',{method:'POST',body:startBody});assert.equal(started.status,201,JSON.stringify(started.data));
const duplicate=await request('/api/workflow/runtime/instances',{method:'POST',body:startBody});assert.ok([400,409].includes(duplicate.status));
const people=context.data.participants.slice(0,2);
const available=await request('/api/workflow/work-items/available',{participant:people[0].id});
const item=available.data.find(i=>i.workflowInstanceId===started.data.id);assert.ok(item);
const claims=await Promise.all(people.map(p=>request(`/api/workflow/work-items/${item.id}/claim`,{method:'POST',participant:p.id})));
assert.equal(claims.filter(r=>r.status===200).length,1,'Exactly one concurrent claim must win');
assert.ok(claims.every(r=>[200,400,409].includes(r.status)));
const winner=claims.findIndex(r=>r.status===200),loser=1-winner;
const wrong=await request(`/api/workflow/work-items/${item.id}/complete`,{method:'POST',participant:people[loser].id,body:{actionTaken:'Approve'}});
assert.equal(wrong.status,400,'Another actor cannot complete a claimed task');
const completed=await request(`/api/workflow/work-items/${item.id}/complete`,{method:'POST',participant:people[winner].id,body:{actionTaken:'Approve',comment:'Automated NWFM verification'}});
assert.equal(completed.status,200,JSON.stringify(completed.data));
const instance=await request('/api/workflow/runtime/instances/'+started.data.id);assert.equal(instance.data.status,'Completed');
assert.equal(instance.data.pinnedWorkflowVersionId,started.data.pinnedWorkflowVersionId);
const requestView=await request('/api/workflow/requests/by-instance/'+started.data.id);assert.equal(requestView.status,200);assert.equal(requestView.data.status,'Completed');
const spec=(await request('/swagger/v1/swagger.json')).data;
assert.ok(Object.keys(spec.paths).every(p=>p.startsWith('/api/workflow/')||p==='/api/app-context'||p==='/health'));
console.log('PASS: API catalog, no-auth startup, tenant validation, participant lifecycle, default actor protection, standalone execution, duplicate start, concurrent claim, ownership, completion, request projection, and reduced API surface.');
console.log('Sample completed instance: '+started.data.id);
