// Run only against an explicitly seeded disposable acceptance database.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const base=process.env.NWFM_API_URL||'http://127.0.0.1:5180';
const provider=process.env.NWFM_DEMO_URL||'http://127.0.0.1:5191';
let token='',checks=0;
async function api(path,method='GET',body,status=200){
  const response=await fetch(base+'/api/'+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body===undefined?undefined:JSON.stringify(body)});
  const text=await response.text();assert.equal(response.status,status,`${method} ${path}: ${text}`);checks++;return text?JSON.parse(text):null;
}
async function until(fn,predicate,label){for(let i=0;i<120;i++){const value=await fn();if(predicate(value))return value;await new Promise(r=>setTimeout(r,400));}throw Error(label);}
const W='workflow/workspace';
await api(W+'/sla','GET',undefined,401);
token=(await api('v1/auth/login','POST',{userName:'administrator@localhost',password:'Administrator1!'})).value.accessToken;
const fields={};for(const code of ['10','11','50']){fields[code]=await api(W+'/lookups/field-activity-types?parentCode='+code);assert.ok(fields[code].length>=2);}
const rules=await api(W+'/sla');assert.ok(rules.length>=7);
const isolation=await api(W+'/sla/resolve?departmentCode=10&fieldActivityCode=ISOLATION');assert.equal(isolation.fieldActivityCode,'ISOLATION');
await api(W+'/sla','POST',{...isolation,id:undefined,name:'Duplicate'},400);
await api(W+'/sla','POST',{...isolation,id:undefined,departmentCode:'50',fieldActivityCode:'ISOLATION'},400);
const group=(await api(W+'/groups')).find(g=>g.name.includes('Review'));
const connections=await api('workflow/integrations/connections');const http=connections.find(c=>c.kind==='Http'),smtp=connections.find(c=>c.kind==='Smtp');
const actor='30000000-0000-0000-0000-000000000001';
const esc=s=>s.replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;').replaceAll('>','&gt;');
const config=c=>`configurationJson="${esc(JSON.stringify(c))}"`;
const task=(key,type,c)=>`<Activity nodeKey="${key}" type="${type}" name="${key}" assignmentGroupId="${group.id}" ${config(c)}><Outcomes><Outcome key="APPROVE" name="Accept" isDefault="true"/><Outcome key="REJECT" name="Reject" requiresComment="true"/></Outcomes></Activity>`;
const event=(key,c,type='ServiceTask')=>`<Activity nodeKey="${key}" name="${key}" type="${type}" ${type==='ServiceTask'?'actionKey="http.request"':''} ${config(c)}/>`;
const xml=(settings,nodes,edges)=>`<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${esc(JSON.stringify({...settings,designerVersion:2}))}"><Activities><Activity nodeKey="start" type="Start" name="Start"/>${nodes}<Activity nodeKey="end" type="End" name="End"/></Activities><Transitions>${edges.map(([from,to])=>`<Transition key="${from}-${to}" from="${from}" to="${to}"/>`).join('')}</Transitions></Workflow>`;
const business={departmentCode:'10',fieldActivityCode:'ISOLATION',rejectTargetNodeKey:'review'};
const child=await api(W+'/definitions','POST',{name:'Visual acceptance child '+randomUUID(),settings:{kind:'Child',designerVersion:2}});
const childPath=`workflow/definitions/${child.definitionId}/versions/${child.versionId}`;
await api(childPath+'/xml','PUT',{xmlContent:xml({kind:'Child'},task('review','UserTask',business),[['start','review'],['review','end']])});
assert.equal((await api(childPath+'/validate','POST',{})).isValid,true);
await api(childPath+'/publish','POST',{});
const childInfo=(await api(W+'/children')).find(c=>c.id===child.definitionId);
const catalog=await api(W+'/catalog');const standard=catalog.find(c=>c.definitionKey==='DEMO-MAIN-STANDARD-VISUAL');assert.ok(standard);
const settings=JSON.parse(standard.workspaceJson);
const main=await api(W+'/definitions','POST',{name:'Visual acceptance main '+randomUUID(),settings});
const mainPath=`workflow/definitions/${main.definitionId}/versions/${main.versionId}`;
const mainConfig={...business,definitionKey:childInfo.definitionKey,versionId:child.versionId};
const request={connectionId:http.id,method:'POST',path:'/rest',body:'{}',maxAttempts:1,required:true};
const nodes=task('review','MainActivity',mainConfig)+event('comment-api',{...request,path:'/fail',triggerBinding:{sourceNodeKey:'review',trigger:'OnComment'}})
  +event('flow-api',request)+event('background-sms',{...request,protocol:'Sms',smsTo:'+966500000000',smsMessage:'Local test',path:'/sms',body:'{"to":"{{smsTo}}","message":"{{smsMessage}}"}',required:false})
  +event('background-email',{connectionId:smtp.id,channels:'Email',to:'test@example.test',subject:'Visual acceptance',body:'Local test',failurePolicy:'Retry',required:false},'NotificationTask');
const edges=[['start','review'],['review','flow-api'],['flow-api','background-sms'],['background-sms','background-email'],['background-email','end']];
await api(mainPath+'/xml','PUT',{xmlContent:xml(settings,nodes,[...edges,['review','comment-api']])});
assert.ok((await api(mainPath+'/validate','POST',{})).errors.some(e=>e.code==='EVENT_SEQUENCE'));
await api(mainPath+'/xml','PUT',{xmlContent:xml(settings,nodes,edges)});
const validation=await api(mainPath+'/validate','POST',{});assert.equal(validation.isValid,true,JSON.stringify(validation.errors));
await api(mainPath+'/publish','POST',{});
const pinned=JSON.parse((await api(mainPath)).activities.find(a=>a.nodeKey==='review').configurationJson).publishedSla;
await api(W+'/sla/'+isolation.id,'PUT',{...isolation,duration:isolation.duration+1});
assert.equal(JSON.parse((await api(mainPath)).activities.find(a=>a.nodeKey==='review').configurationJson).publishedSla.duration,pinned.duration);
await api(W+'/sla/'+isolation.id,'PUT',isolation);
const start={requestId:randomUUID(),reference:'Visual test '+randomUUID(),isDemo:true};
const root=(await api(W+`/definitions/${main.definitionId}/instances`,'POST',start)).instanceId;
assert.equal((await api(W+`/definitions/${main.definitionId}/instances`,'POST',start)).instanceId,root);
const detail=()=>api(W+'/instances/'+root+'?demoActorId='+actor);
const tasks=d=>d.tree.flatMap(r=>r.activities).flatMap(a=>a.tasks).filter(e=>['Pending','Claimed'].includes(e.task.status));
async function action(id,action,comment='Acceptance test',requestId=randomUUID(),status=204){return api(W+`/demo/tasks/${id}/actions`,'POST',{requestId,action,comment,demoActorId:actor},status);}
let d=await detail();assert.equal(d.tree[0].activities.find(a=>a.type==='MainActivity').phase,'WaitingForChild');assert.equal(tasks(d).length,1);
let entry=tasks(d)[0];await action(entry.task.id,'REJECT','',randomUUID(),400);await action(entry.task.id,'APPROVE');
d=await until(detail,d=>tasks(d).some(e=>e.task.workflowInstanceId===root),'Parent approval');
entry=tasks(d)[0];await action(entry.task.id,'REJECT');
d=await until(detail,d=>tasks(d).some(e=>e.task.workflowInstanceId!==root),'Rework child');await action(tasks(d)[0].task.id,'APPROVE');
d=await until(detail,d=>tasks(d).some(e=>e.task.workflowInstanceId===root),'Parent after rework');entry=tasks(d)[0];
const commentId=randomUUID();await action(entry.task.id,'comment','Required comment delivery',commentId);await action(entry.task.id,'comment','Required comment delivery',commentId);
d=await until(detail,d=>d.operations.some(o=>o.eventNodeKey==='comment-api'&&o.status==='Failed'),'Required comment failure');
assert.equal(d.operations.filter(o=>o.eventNodeKey==='comment-api').length,1);assert.equal(tasks(d)[0].completionBlocked,true);
await action(entry.task.id,'APPROVE','Cannot yet advance',randomUUID(),400);
await fetch(provider+'/recover',{method:'POST'});const job=d.operations.find(o=>o.eventNodeKey==='comment-api');await api('workflow/integrations/operations/'+job.id+'/replay','POST',{},204);
await until(detail,d=>d.operations.find(o=>o.id===job.id)?.status==='Completed','Delivery retry');
const acceptId=randomUUID();await action(entry.task.id,'APPROVE','Done',acceptId);await action(entry.task.id,'APPROVE','Done',acceptId);
d=await until(detail,d=>d.tree[0].status==='Completed'&&d.operations.filter(o=>['InFlow'].includes(o.trigger)).every(o=>o.status==='Completed'),'Flow and background completion');
assert.equal(d.tree[0].activities.filter(a=>a.type==='MainActivity').length,2);assert.ok(d.operations.some(o=>o.eventNodeKey==='background-sms'));
const rest=await api('workflow/integrations/http/test','POST',{configuration:request,variables:{}});assert.ok(rest.success&&rest.elapsedMilliseconds>=0);
const soap=await api('workflow/integrations/http/test','POST',{configuration:{...request,protocol:'Soap',soapVersion:'1.1',soapAction:'urn:Test',path:'/soap-fault',body:'<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body/></s:Envelope>'},variables:{}});assert.equal(soap.success,false);
console.log(JSON.stringify({checks,root,childVersion:child.versionId,mainDefinition:main.definitionId,mainVersion:main.versionId,results:'lookups, SLA matching and snapshots, validation, nested rework, required comments, retry, duplicate actions, REST/SOAP, background SMS/email passed'},null,2));
