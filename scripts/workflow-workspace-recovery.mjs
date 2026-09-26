import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {loginForWorkflowTest} from './workflow-test-auth.mjs';
const base=process.env.NWFM_API_URL||'http://127.0.0.1:5082',token=await loginForWorkflowTest(base);
const file=new URL('../.work/workspace-recovery.json',import.meta.url),actor='30000000-0000-0000-0000-000000000001';
async function api(path,method='GET',body){const r=await fetch(base+'/api/workflow/workspace/'+path,{method,headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});assert.ok(r.ok,await r.clone().text());return r.status===204?null:r.json();}
const tasks=d=>d.tree.flatMap(x=>x.activities).flatMap(a=>a.tasks).map(t=>t.task).filter(t=>t.status==='Pending'||t.status==='Claimed');
if(process.argv[2]==='prepare'){
 const main=(await api('catalog')).find(x=>x.definitionKey==='DEMO-MAIN-STANDARD');const started=await api(`definitions/${main.id}/instances`,'POST',{requestId:randomUUID(),reference:'Restart acceptance '+randomUUID(),isDemo:true});
 let d=await api('instances/'+started.instanceId);await api(`tasks/${tasks(d)[0].id}/actions`,'POST',{requestId:randomUUID(),action:'APPROVE',demoActorId:actor});
 for(let n=0;n<50;n++){d=await api('instances/'+started.instanceId);if(tasks(d).some(t=>t.workflowInstanceId===started.instanceId))break;await new Promise(r=>setTimeout(r,200));}
 assert.equal(tasks(d).length,1);assert.equal(tasks(d)[0].workflowInstanceId,started.instanceId);
 await mkdir(new URL('../.work/',import.meta.url),{recursive:true});await writeFile(file,JSON.stringify({id:d.id,task:tasks(d)[0].id,children:d.tree.map(x=>x.id),geography:d.geographyJson}));
 console.log('Prepared nested main approval. Restart API, then run verify.');
}else{
 const state=JSON.parse(await readFile(file,'utf8'));const d=await api('instances/'+state.id);assert.equal(tasks(d).length,1);assert.equal(tasks(d)[0].id,state.task);assert.deepEqual(d.tree.map(x=>x.id),state.children);assert.equal(d.geographyJson,state.geography);
 const action={requestId:randomUUID(),action:'APPROVE',demoActorId:actor};await api(`tasks/${state.task}/actions`,'POST',action);await api(`tasks/${state.task}/actions`,'POST',action);
 const after=await api('instances/'+state.id);assert.equal(after.tree.filter(x=>!state.children.includes(x.id)).length,1);console.log('PASS: persisted child linkage and main approval survive restart without duplicate child, task or transition.');
}
