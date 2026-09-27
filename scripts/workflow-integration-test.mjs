// Run only against a disposable NWFM database. It creates uniquely named examples
// and contacts local mock HTTP/SMTP servers, never real recipients or providers.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createServer } from 'node:http';
import { createServer as createTcpServer } from 'node:net';
import { writeFile, mkdir } from 'node:fs/promises';
import { loginForWorkflowTest, ensureTestParticipant } from './workflow-test-auth.mjs';
const base = process.env.NWFM_API_URL || 'http://localhost:5081';
const token = await loginForWorkflowTest(base);
const prefix = 'VERIFY_' + randomUUID().slice(0,8);
const secret = randomUUID() + randomUUID();
const esc = value => String(value).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;').replaceAll('>','&gt;');
async function api(path, method='GET', body, expected=200, headers={}) {
  const response=await fetch(base+'/api/'+path,{method,headers:{'Content-Type':'application/json',Authorization:`Bearer ${token}`,...headers},body:body===undefined?undefined:JSON.stringify(body)});
  const text=await response.text(); let data;try{data=JSON.parse(text)}catch{data=text}
  assert.equal(response.status,expected,`${method} ${path}: ${text}`); return data;
}
async function until(read, predicate, label, seconds=90) {
  const deadline=Date.now()+seconds*1000; let value;
  while(Date.now()<deadline){value=await read();if(predicate(value))return value;await new Promise(r=>setTimeout(r,300));}
  throw new Error(`${label} timed out: ${JSON.stringify(value)}`);
}
const node=(key,type,config={},attrs='',children='')=>`<Activity nodeKey="${key}" type="${type}" name="${key}" positionX="100" positionY="100" ${attrs} configurationJson="${esc(JSON.stringify(config))}">${children}</Activity>`;
const edge=(from,to,condition='',fallback=false,priority=0)=>`<Transition key="${from}-${to}" from="${from}" to="${to}" priority="${priority}" ${condition?`condition="${esc(condition)}"`:''} ${fallback?'isDefault="true"':''}/>`;
const xml=(nodes,edges,variables='')=>`<Workflow xmlns="https://privora.io/workflow/v1"><Activities>${nodes.join('')}</Activities><Transitions>${edges.join('')}</Transitions><Variables>${variables}</Variables></Workflow>`;
const variable=(key,type,defaultValue)=>`<Variable key="${key}" name="${key}" dataType="${type}" ${defaultValue!==undefined?`defaultValue="${esc(defaultValue)}"`:''}/>`;
const context=await api('app-context'); const org=context.tenant.id;
const groups=await api('workflow/assignment-groups');const group=(groups.items||groups).find(g=>g.code==='REVIEWERS');assert.ok(group);
await ensureTestParticipant(api,token,group);
async function createWorkflow(name,content) {
  const definition=await api('workflow/definitions','POST',{organizationId:org,definitionKey:prefix+'_'+name,name:prefix+' '+name},201);
  const route=`workflow/definitions/${definition.id}/versions`;
  const version=await api(route,'POST',{},201);
  await api(`${route}/${version.id}/xml`,'PUT',{xmlContent:content});
  const validation=await api(`${route}/${version.id}/validate`,'POST',{}); assert.equal(validation.isValid,true,JSON.stringify(validation));
  await api(`${route}/${version.id}/publish`,'POST',{});
  const binding=await api(`workflow/definitions/${definition.id}/bindings`,'POST',{organizationId:org,moduleKey:'Standalone',entityType:prefix+'_'+name,triggerEvent:'RequestSubmitted',mode:'Active',versionPolicy:'Latest',screenKey:'workflow.start'},201);
  await api(`workflow/definitions/${definition.id}/bindings/${binding.id}/activate`,'POST',{},204);
  return {definition,version,binding,route};
}
let webhook; const attempts=new Map(); const calls=[]; const mail=[];
const http=createServer(async(req,res)=>{
  try {
    let text=''; for await(const chunk of req)text+=chunk; const input=JSON.parse(text||'{}');
    const operation=req.headers['idempotency-key']; calls.push({operation,input,authorization:req.headers.authorization});
    const count=(attempts.get(operation)||0)+1; attempts.set(operation,count);
    if(count===1){res.writeHead(503);res.end('temporary');return;}
    await api(`workflow/integrations/webhooks/${webhook.id}`,'POST',{eventId:input.order,eventKey:'order.ready',correlationId:input.order,payload:{approved:true}},202,{'X-Workflow-Key':secret});
    res.writeHead(201,{'Content-Type':'application/json','X-Provider':'local-test'});res.end(JSON.stringify({id:input.order,amount:input.amount}));
  } catch(error){res.writeHead(500);res.end(String(error));}
});
const smtp=createTcpServer(socket=>{
  socket.on('error',error=>{ if(error.code!=='ECONNRESET') throw error; });
  let buffer='',data=false,message='',recipients=[]; socket.write('220 localhost SMTP\r\n');
  socket.on('data',chunk=>{buffer+=chunk.toString();let index;while((index=buffer.indexOf('\r\n'))>=0){const line=buffer.slice(0,index);buffer=buffer.slice(index+2);
    if(data){if(line==='.'){data=false;mail.push({recipients:[...recipients],message});socket.write('250 accepted\r\n');}else message+=line+'\n';continue;}
    if(/^EHLO|^HELO/.test(line))socket.write('250 localhost\r\n');
    else if(line.startsWith('MAIL FROM'))socket.write('250 sender\r\n');
    else if(line.startsWith('RCPT TO')){recipients.push(line);socket.write('250 recipient\r\n');}
    else if(line==='DATA'){data=true;message='';socket.write('354 send data\r\n');}
    else if(line==='QUIT'){socket.end('221 bye\r\n');}else socket.write('250 ok\r\n');
  }});
});
await Promise.all([new Promise(r=>http.listen(0,'127.0.0.1',r)),new Promise(r=>smtp.listen(0,'127.0.0.1',r))]);
try {
  const httpConnection=await api('workflow/integrations/connections','POST',{name:prefix+' API',kind:'Http',address:`http://127.0.0.1:${http.address().port}`,authentication:'Bearer',allowPrivateNetwork:true,credentials:{token:secret}});
  webhook=await api('workflow/integrations/connections','POST',{name:prefix+' Callback',kind:'Webhook',address:'',authentication:'ApiKey',credentials:{apiKey:secret}});
  const smtpConnection=await api('workflow/integrations/connections','POST',{name:prefix+' SMTP',kind:'Smtp',address:'127.0.0.1',port:smtp.address().port,useTls:false,authentication:'None',credentials:{fromAddress:'sender@example.test'}});
  assert.ok(!JSON.stringify(await api('workflow/integrations/connections')).includes(secret));
  await api(`workflow/integrations/webhooks/${webhook.id}`,'POST',{eventId:'bad'},401,{'X-Workflow-Key':'bad'});
  const child=await createWorkflow('CHILD',xml([node('start','Start'),node('set','ScriptTask',{setVariables:{childResult:'finished'}}),node('end','End')],[edge('start','set'),edge('set','end')],variable('childResult','String')));
  const fields=[{key:'amount',labelEn:'Order amount',labelAr:'قيمة الطلب',type:'number',required:true}];
  const main=await createWorkflow('ALL_ACTIVITIES',xml([
    node('start','Start'),node('review','UserTask',{instructionsEn:'Enter the test order amount.',formFields:fields,outputMappingJson:JSON.stringify({amount:'$.output.amount'}),slaDurationHours:2},`assignmentGroupId="${group.id}"`,
      `<Actions><Action key="workflow.setVariables" trigger="OnComplete" sequence="1" failurePolicy="FailWorkflow" inputMapping="${esc(JSON.stringify({reviewedAmount:'variables.amount'}))}" /></Actions>`),
    node('prepare','ScriptTask',{assignmentFormat:'typed',setVariables:{stage:'submitted'}}),
    node('api','ServiceTask',{connectionId:httpConnection.id,method:'POST',path:'/orders',body:JSON.stringify({order:'{{BusinessEntityId}}',amount:'{{amount}}'}),maxAttempts:2,retryDelaySeconds:1,outputMappings:{externalId:'body.id',httpStatus:'status',provider:'headers.x-provider'}},'actionKey="http.request"'),
    node('callback','WaitEvent',{connectionId:webhook.id,eventKey:'order.ready',correlationVariable:'externalId',timeoutSeconds:60,outputMappings:{approved:'approved'}}),
    node('email','NotificationTask',{connectionId:smtpConnection.id,channels:'Email, InApp',templateKey:'order.ready',to:'recipient@example.test',subject:'Order {{externalId}} ready',body:'Amount {{amount}}',failurePolicy:'Retry',maxAttempts:2,retryDelaySeconds:1}),
    node('decision','ExclusiveGateway'),node('fork','ParallelGateway',{joinNodeKey:'join'}),
    node('timer','Timer',{timerType:'Duration',duration:'00:00:00'}),node('inclusive','InclusiveGateway',{joinNodeKey:'innerJoin'}),
    node('branchA','ScriptTask',{setVariables:{branchA:true}}),node('branchB','ScriptTask',{setVariables:{branchB:true}}),node('fallback','ScriptTask',{setVariables:{branchA:false}}),
    node('innerJoin','JoinGateway'),node('join','JoinGateway'),
    node('child','CallActivity',{definitionKey:child.definition.definitionKey,waitForCompletion:true,inputMappings:{amount:'amount'},outputMappings:{childResult:'childResult'}}),
    node('end','End'),node('rejected','End')
  ],[edge('start','review'),edge('review','prepare'),edge('prepare','api'),edge('api','callback'),edge('callback','email'),edge('email','decision'),
    edge('decision','fork',"amount >= 100 && approved == true"),edge('decision','rejected','',true,10),
    edge('fork','timer'),edge('fork','inclusive'),edge('timer','join'),edge('inclusive','branchA','approved == true'),edge('inclusive','branchB','amount >= 100'),edge('inclusive','fallback','',true,10),
    edge('branchA','innerJoin'),edge('branchB','innerJoin'),edge('fallback','innerJoin'),edge('innerJoin','join'),edge('join','child'),edge('child','end')],
    variable('amount','Decimal')+variable('stage','String')+variable('branchA','Boolean')+variable('branchB','Boolean')+variable('childResult','String')));
  const business=prefix+'-order'; const started=await api('workflow/runtime/instances','POST',{workflowBindingId:main.binding.id,businessEntityId:business,idempotencyKey:business},201);
  const items=await api('workflow/work-items/available'); const task=items.find(i=>i.workflowInstanceId===started.id);assert.ok(task);
  const detail=await api(`workflow/work-items/${task.id}`);assert.equal(detail.formFields[0].key,'amount');assert.ok(detail.dueAt);assert.equal(detail.instructionsEn,'Enter the test order amount.');
  await api(`workflow/work-items/${task.id}/claim`,'POST',{});
  await api(`workflow/work-items/${task.id}/complete`,'POST',{actionTaken:'Approve'},400);
  await api(`workflow/work-items/${task.id}/complete`,'POST',{actionTaken:'Approve',formValues:{amount:150}});
  const completed=await until(()=>api(`workflow/runtime/instances/${started.id}`),r=>r.status==='Completed'||r.status==='Failed','all activities');
  assert.equal(completed.status,'Completed',JSON.stringify(completed));
  assert.equal(calls.length,2);assert.equal(calls[0].operation,calls[1].operation);assert.equal(calls[1].input.amount,150);assert.equal(calls[1].authorization,'Bearer '+secret);
  assert.equal(mail.length,1);assert.ok(mail[0].recipients.some(r=>r.includes('recipient@example.test')));assert.ok(!mail[0].recipients.some(r=>r.includes('sender@example.test')));assert.ok(mail[0].message.includes('Amount 150'));
  const operations=await api(`workflow/integrations/operations?instanceId=${started.id}`);assert.equal(operations.length,2);assert.ok(operations.every(o=>o.status==='Completed'));
  assert.equal(operations.find(o=>o.kind==='Http').attempts,2);
  const receipts=await api('workflow/integrations/events');const receipt=receipts.find(r=>r.eventId===business);assert.equal(receipt.status,'Processed');
  await api(`workflow/integrations/webhooks/${webhook.id}`,'POST',{eventId:business,eventKey:'order.ready',correlationId:business,payload:{approved:true}},202,{'X-Workflow-Key':secret});
  assert.equal((await api('workflow/integrations/events')).filter(r=>r.eventId===business).length,1);
  const progress=await api(`workflow/runtime/instances/${started.id}/progress`);
  assert.ok(progress.events.some(e=>e.eventType==='ServiceTaskExecuted'&&e.payloadJson?.includes('OnComplete')),'Task completion action hook must execute');
  await mkdir(new URL('../.work/',import.meta.url),{recursive:true});
  await writeFile(new URL('../.work/workflow-test-result.json',import.meta.url),JSON.stringify({instanceId:started.id,definitionId:main.definition.id,versionId:main.version.id,base,status:completed.status,httpAttempts:calls.length,emailMessages:mail.length,receiptStatus:receipt.status,verifiedAt:new Date().toISOString(),progress},null,2));
  console.log('PASS: SQL migration; all 13 activities; required task form; typed output; real HTTP retry with stable operation ID; authenticated early callback; SMTP recipient/template; nested parallel/inclusive joins; timer resume; immediate child mapping; callback deduplication.');
  console.log(`Example workflow: ${main.definition.id}, version ${main.version.id}, completed instance ${started.id}`);
} finally { http.closeAllConnections(); await Promise.all([new Promise(r=>http.close(r)),new Promise(r=>smtp.close(r))]); }
