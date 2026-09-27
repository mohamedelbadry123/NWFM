import assert from 'node:assert/strict';
export async function loginForWorkflowTest(base, userName=process.env.NWFM_TEST_USER||'administrator@localhost',password=process.env.NWFM_TEST_PASSWORD||'Administrator1!') {
 const response=await fetch(base+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({userName,password})});
 assert.equal(response.status,200,'Test login failed');return (await response.json()).value.accessToken;
}
export async function ensureTestParticipant(api,token,group) {
 const claims=JSON.parse(Buffer.from(token.split('.')[1],'base64url').toString());const userId=claims.sub||claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
 const all=await api('workflow/participants?pageSize=200');let person=all.items.find(p=>p.userId===userId);
 if(!person)person=await api('workflow/participants','POST',{userId,displayName:'Authenticated regression tester',email:'regression@example.test'},201);
 const detail=await api(`workflow/assignment-groups/${group.id}`);
 if(!detail.members.some(m=>m.participantId===person.id))await api(`workflow/assignment-groups/${group.id}/members`,'POST',{participantId:person.id,canClaim:true},201);
 return person;
}
