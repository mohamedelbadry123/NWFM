const fs=require('fs'),path=require('path');const source='D:/Work/Privora-New-Tech-Stach/backend/tests/Privora.UnitTests';const dest='backend/tests/NWFM.Tests';
fs.mkdirSync(dest+'/Workflow',{recursive:true});let count=0;
for(const name of fs.readdirSync(source+'/Modules/Workflow')){
 const s=fs.readFileSync(source+'/Modules/Workflow/'+name,'utf8');
 if(/using (?:global::)?(?:Consent|Vendor|DPIA|Complaints|PrivacyConsentRequest|Notifications)\.|Infrastructure.Seeding|Persistence.Seeding|WorkflowSeedIds|Privora.Shared.Events|Privora.Shared.DemoSeeding/.test(s))continue;
 fs.writeFileSync(dest+'/Workflow/'+name,s.replaceAll('Privora.UnitTests','NWFM.Tests').replaceAll('Privora.Shared','NWFM.Shared'));count++;
}
let proj=fs.readFileSync(source+'/Privora.UnitTests.csproj','utf8').replaceAll('Privora.Shared','NWFM.Shared');
proj=proj.replace(/^.*ProjectReference.*Modules\\(?!Workflow\\).*\r?\n/gm,'');
fs.writeFileSync(dest+'/NWFM.Tests.csproj',proj);console.log('Extracted '+count+' workflow test files.');
