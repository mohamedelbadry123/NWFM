const fs=require('fs');
for(const lang of ['en','ar']){
 const path='frontend/public/assets/i18n/'+lang+'.json';const data=JSON.parse(fs.readFileSync(path));
 if(data.error)for(const k of Object.keys(data.error))if(/Consent|Privacy|DSAR|DPIA|Identity|Auth/i.test(k))delete data.error[k];
 const replacements=lang==='en'?[
 ['Privora','NWFM'],['consentRequestId','requestId'],['consent-approval','simple-approval'],['Consent Approval','Simple Approval'],['consent-review-form','request-review-form'],['consent-subprocess','request-subprocess'],['consent.approved.external','request.approved.external'],['consent-management','workflow.start'],['Consent Management','Workflow Requests'],['Consent, DSAR, …','Standalone Workflow'],['Consent, DSAR','Standalone Workflow'],['Privacy Review','Request Review'],['PrivacyReview','RequestReview'],['PRIVACY_REVIEWERS','REVIEWERS'],['PRIVACY_REVIEW','REVIEWERS'],['DPO','Reviewer'],['SuperAdmin','Workflow operator'],['OrgAdmin','Workflow operator'],['Authorized users','Participants'],['organisation users','participants'],['organization user','participant'],['existing user','participant'],['existing active user','participant']
 ]:[['Privora','NWFM'],['consentRequestId','requestId'],['consent-approval','simple-approval'],['PRIVACY_REVIEWERS','REVIEWERS'],['PRIVACY_REVIEW','REVIEWERS'],['Consent, DSAR, …','سير عمل مستقل'],['Consent, DSAR','سير عمل مستقل'],['SuperAdmin','مشغل سير العمل'],['OrgAdmin','مشغل سير العمل']];
 function transform(o){for(const k of Object.keys(o)){if(typeof o[k]==='string'){for(const [a,b] of replacements)o[k]=o[k].replaceAll(a,b);}else if(o[k]&&typeof o[k]==='object')transform(o[k]);}}
 transform(data);
 if(data.workflow.definitions)for(const k of Object.keys(data.workflow.definitions))if(k.startsWith('guid_panel'))delete data.workflow.definitions[k];
 fs.writeFileSync(path,JSON.stringify(data,null,2));
}
