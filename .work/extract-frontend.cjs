const fs=require('fs'),path=require('path'); const src='D:/Work/Privora-New-Tech-Stach/frontend',dest=path.resolve('frontend');
const write=(p,s)=>{fs.mkdirSync(path.dirname(p),{recursive:true});fs.writeFileSync(p,s)};
function files(d){return fs.readdirSync(d,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(path.join(d,e.name)):[path.join(d,e.name)]);}
function copy(r){const p=path.join(src,r);if(fs.statSync(p).isDirectory()){for(const f of files(p))copy(path.relative(src,f));return;} if(r.endsWith('.spec.ts'))return;write(path.join(dest,r),fs.readFileSync(p));}
for(const f of ['package.json','package-lock.json','angular.json','tsconfig.json','tsconfig.app.json','tsconfig.spec.json','tailwind.config.js','postcss.config.js','src/main.ts','src/styles.css','src/styles','src/app/features/workflow','src/app/shared/components','src/app/core/i18n','src/app/core/notifications']) if(fs.existsSync(path.join(src,f)))copy(f);
for(const e of fs.readdirSync(src+'/src/app/features/admin'))if(e.startsWith('workflow-'))copy('src/app/features/admin/'+e);
for(const f of files(dest+'/src/app')){
 let s=fs.readFileSync(f,'utf8');
 if(f.endsWith('.ts'))s=s.replace(/^import.*(HasPermissionDirective|permissionGuard).*;\r?\n/gm,'').replace(/^\s*HasPermissionDirective,?\r?\n/gm,'').replace(/^\s*canActivate: \[permissionGuard\],\r?\n/gm,'').replace(/^\s*data: \{ resource:.*\r?\n/gm,'');
 if(f.endsWith('.html'))s=s.replace(/\s+\*appHasPermission="[^"]*"/g,'');
 s=s.replaceAll('Privora','NWFM');write(f,s);
}
let changed=true;const missing=new Set();
while(changed){changed=false;for(const f of files(dest+'/src/app').filter(f=>f.endsWith('.ts'))){const s=fs.readFileSync(f,'utf8');for(const m of s.matchAll(/(?:from\s*|import\s*\()['"]([^'"]+)['"]/g)){
 const p=m[1];if(!p.startsWith('@core/')&&!p.startsWith('@shared/')&&!p.startsWith('.'))continue;
 if(/auth\/|has-permission|team.service|team.models|org-consent|org-beneficiar|org-terms|org-dsar|consent-request.models|beneficiary.models|term-and-condition.models|dsar.models|superadmin-organizations|super-admin-organizations|organization.models/.test(p)){missing.add(p);continue;}
 const base=p.startsWith('@core/')?path.join(dest,'src/app/core',p.slice(6)):p.startsWith('@shared/')?path.join(dest,'src/app/shared',p.slice(8)):path.resolve(path.dirname(f),p);
 const candidates=[base+'.ts',path.join(base,'index.ts')];if(candidates.some(f=>fs.existsSync(f)))continue;
 for(const c of candidates){const rel=path.relative(dest,c);if(fs.existsSync(path.join(src,rel))){copy(rel);changed=true;break;}}
 }}}
const locale=JSON.parse(fs.readFileSync(src+'/public/assets/i18n/en.json','utf8'));
console.log('Locale keys: '+Object.keys(locale).join(', '));console.log('Dependencies to replace: '+[...missing].join(', '));
for(const lang of ['en','ar']){
 const all=JSON.parse(fs.readFileSync(src+'/public/assets/i18n/'+lang+'.json','utf8')); const keep={};
 for(const [k,v] of Object.entries(all))if(/workflow|common|shared|validation|pagination|table|button|error|GENERAL|COMMON|SHARED/.test(k))keep[k]=v;
 write(dest+'/public/assets/i18n/'+lang+'.json',JSON.stringify(keep,null,2));
}
