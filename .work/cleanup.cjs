const fs=require('fs'),path=require('path');const edit=(p,f)=>fs.writeFileSync(p,f(fs.readFileSync(p,'utf8')));
for(const p of ['definitions/workflow-definitions.component','designer/workflow-designer.component']){
 edit('frontend/src/app/features/workflow/'+p+'.ts',s=>s.replace(/^  protected readonly SEED_(?:DEFINITION_ID|VERSION_ID).*\r?\n/gm,'').replace(/  protected readonly SEED_GUIDS = \[[\s\S]*?\n  \];\r?\n/,'').replace(/^  protected readonly showGuidPanel.*\r?\n/gm,''));
 edit('frontend/src/app/features/workflow/'+p+'.html',s=>s.replace(/<!-- ── GUID \/ Technical IDs Panel ── -->[\s\S]*?(?=<!-- Create Definition Modal -->)/,'').replace(/\s*\[class\.ring-(?:1|inset|primary)\]="d.id === SEED_DEFINITION_ID"/g,'').replace(/@if \(d.id === SEED_DEFINITION_ID\) \{[\s\S]*?\n\s*\}/g,'').replace(/\[routerLink\]="\['\/admin\/workflow\/definitions', SEED_DEFINITION_ID, 'versions', SEED_VERSION_ID, 'designer'\]"/g,'routerLink="/admin/workflow/definitions"').replace(/<button[^>]*\(click\)="showGuidPanel.set\(true\)"[\s\S]*?<\/button>/g,''));
}
for(const lang of ['en','ar']){
 const p='frontend/public/assets/i18n/'+lang+'.json';const all=JSON.parse(fs.readFileSync(p,'utf8'));const c=all.workflow.runtime.case;
 c.field_entity_type=lang==='en'?'Request type':'نوع الطلب';c.field_entity_id=lang==='en'?'Request reference':'مرجع الطلب';
 all.workflow.participants.name_ar=lang==='en'?'Name in Arabic':'الاسم بالعربية';
 all.workflow.participants.col_name=lang==='en'?'Name':'الاسم';all.workflow.participants.col_email=lang==='en'?'Email':'البريد الإلكتروني';
 all.workflow.participants.modal_subtitle=lang==='en'?'Add a person to the workflow directory.':'إضافة مشارك إلى دليل سير العمل.';
 fs.writeFileSync(p,JSON.stringify(all,null,2));
}
