// Disposable SQL database only. Run `prepare`, restart the API process, then `verify`.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const base=process.env.NWFM_API_URL || 'http://localhost:5081';
const stateFile=new URL('../.work/workflow-recovery-state.json',import.meta.url);
async function api(path,method='GET',body,status=200,headers={}) {
  const response=await fetch(`${base}/api/${path}`,{method,headers:{'Content-Type':'application/json',...headers},body:body===undefined?undefined:JSON.stringify(body)});
  const text=await response.text();assert.equal(response.status,status,`${method} ${path}: ${text}`);return text?JSON.parse(text):null;
}
async function until(read,predicate) {
  const end=Date.now()+70000;let result;
  while(Date.now()<end){result=await read();if(predicate(result))return result;await new Promise(r=>setTimeout(r,300));}
  throw new Error('Recovery timed out: '+JSON.stringify(result));
}
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;');
const node=(key,type,config={})=>`<Activity nodeKey="${key}" type="${type}" name="${key}" configurationJson="${esc(JSON.stringify(config))}"/>`;
const edge=(a,b)=>`<Transition key="${a}-${b}" from="${a}" to="${b}"/>`;
const prefix='RECOVERY_'+randomUUID().slice(0,8);
async function workflow(name,nodes,edges) {
  const context=await api('app-context');const organizationId=context.tenant.id;
  const definition=await api('workflow/definitions','POST',{organizationId,definitionKey:prefix+'_'+name,name:prefix+' '+name},201);
  const route=`workflow/definitions/${definition.id}/versions`;const version=await api(route,'POST',{},201);
  const xmlContent=`<Workflow xmlns="https://privora.io/workflow/v1"><Activities>${nodes.join('')}</Activities><Transitions>${edges.join('')}</Transitions></Workflow>`;
  await api(`${route}/${version.id}/xml`,'PUT',{xmlContent});
  const validation=await api(`${route}/${version.id}/validate`,'POST',{});assert.ok(validation.isValid,JSON.stringify(validation));
  await api(`${route}/${version.id}/publish`,'POST',{});
  const binding=await api(`workflow/definitions/${definition.id}/bindings`,'POST',{organizationId,moduleKey:'Standalone',entityType:prefix+'_'+name,triggerEvent:'RequestSubmitted',mode:'Active',versionPolicy:'Latest',screenKey:'workflow.start'},201);
  await api(`workflow/definitions/${definition.id}/bindings/${binding.id}/activate`,'POST',{},204);return {definition,binding};
}
async function start(flow,business=prefix) {return api('workflow/runtime/instances','POST',{workflowBindingId:flow.binding.id,businessEntityId:business,idempotencyKey:business,correlationId:business},201);}
if(process.argv[2]==='prepare') {
  const secret=randomUUID()+randomUUID();
  const connection=await api('workflow/integrations/connections','POST',{name:prefix,kind:'Webhook',address:'',authentication:'ApiKey',credentials:{apiKey:secret}});
  const flow=await workflow('PARALLEL',[
    node('start','Start'),node('fork','ParallelGateway',{joinNodeKey:'join'}),
    node('a','WaitEvent',{connectionId:connection.id,eventKey:'a',correlationVariable:'CorrelationId',timeoutSeconds:15}),
    node('b','WaitEvent',{connectionId:connection.id,eventKey:'b',correlationVariable:'CorrelationId',timeoutSeconds:15}),
    node('timer','Timer',{timerType:'Duration',duration:'00:00:01'}),node('join','JoinGateway'),node('end','End')
  ],[edge('start','fork'),edge('fork','a'),edge('fork','b'),edge('fork','timer'),edge('a','join'),edge('b','join'),edge('timer','join'),edge('join','end')]);
  const instance=await start(flow);
  await api(`workflow/runtime/instances/${instance.id}/suspend`,'POST',{},204);
  const elapsed=Date.now();
  // Many concurrent deliveries for each identity must create exactly two receipts.
  await Promise.all(Array.from({length:24},(_,i)=>api(`workflow/integrations/webhooks/${connection.id}`,'POST',
    {eventId:prefix+'-'+i%2,eventKey:i%2?'a':'b',correlationId:prefix,payload:{}},202,{'X-Workflow-Key':secret})));
  const receipts=(await api('workflow/integrations/events')).filter(r=>r.eventId.startsWith(prefix));assert.equal(receipts.length,2);
  assert.ok(receipts.every(r=>r.status==='Received'));
  await mkdir(new URL('../.work/',import.meta.url),{recursive:true});
  await writeFile(stateFile,JSON.stringify({instanceId:instance.id,prefix,preparedAt:new Date().toISOString(),callbackBatchMs:Date.now()-elapsed}));
  console.log('PREPARED: suspended parallel callbacks and timer persisted; 24 concurrent deliveries produced two receipts. Restart the API, then run verify.');
} else if(process.argv[2]==='verify') {
  const state=JSON.parse(await readFile(stateFile,'utf8'));
  assert.equal((await api(`workflow/runtime/instances/${state.instanceId}`)).status,'Suspended');
  await api(`workflow/runtime/instances/${state.instanceId}/resume`,'POST',{},204);
  const result=await until(()=>api(`workflow/runtime/instances/${state.instanceId}`),i=>['Completed','Failed'].includes(i.status));assert.equal(result.status,'Completed',JSON.stringify(result));
  const timeline=await api(`workflow/runtime/instances/${state.instanceId}/timeline`);
  assert.equal(timeline.filter(e=>e.eventType==='JoinCompleted').length,1);
  assert.equal(timeline.filter(e=>e.eventType==='InstanceCompleted').length,1);
  const receipts=(await api('workflow/integrations/events')).filter(r=>r.eventId.startsWith(state.prefix));assert.ok(receipts.every(r=>r.status==='Processed'));
  const timers=await api(`workflow/runtime/instances/${state.instanceId}/timers`);assert.equal(timers.length,1);assert.equal(timers[0].status,'Completed');
  const child=await workflow('CHILD',[node('start','Start'),node('timer','Timer',{timerType:'Duration',duration:'01:00:00'}),node('end','End')],[edge('start','timer'),edge('timer','end')]);
  const parent=await workflow('PARENT',[node('start','Start'),node('child','CallActivity',{definitionKey:child.definition.definitionKey,waitForCompletion:true}),node('end','End')],[edge('start','child'),edge('child','end')]);
  const parentInstance=await start(parent,prefix+'-parent');
  const instances=await api('workflow/runtime/instances?pageSize=200');
  const childInstance=instances.items.find(i=>i.businessEntityId===parentInstance.businessEntityId&&i.id!==parentInstance.id);assert.ok(childInstance);
  await api(`workflow/runtime/instances/${parentInstance.id}/cancel`,'POST',{},204);
  await until(()=>api(`workflow/runtime/instances/${childInstance.id}`),i=>i.status==='Cancelled');
  assert.ok((await api(`workflow/runtime/instances/${childInstance.id}/timers`)).every(t=>t.status==='Cancelled'));
  console.log(`PASS: real process restart, suspended on-time events beat expired deadline, durable timer, parallel join once, callback deduplication, waiting-child cancellation. Callback batch: ${state.callbackBatchMs}ms (local evidence, not a capacity benchmark).`);
} else throw new Error('Usage: node scripts/workflow-recovery-test.mjs prepare|verify');
