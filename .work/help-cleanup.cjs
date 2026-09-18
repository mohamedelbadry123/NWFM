const fs=require('fs');
for(const lang of ['en','ar']) {
  const file=`frontend/public/assets/i18n/${lang}.json`;
  let data=JSON.parse(fs.readFileSync(file,'utf8'));
  function clean(o) { for(const k of Object.keys(o)) {
    if(typeof o[k]==='object'&&o[k]) clean(o[k]);
    else if(typeof o[k]==='string') o[k]=o[k]
      .replaceAll('Super Admin','workflow designers').replaceAll('SaaS','workflow')
      .replaceAll('Consent','Workflow').replaceAll('consent','workflow').replaceAll('DSAR','workflow').replaceAll('PDPL','review history')
      .replaceAll('مسؤول النظام العام','مصمم سير العمل').replaceAll('مسؤول النظام','مصمم سير العمل').replaceAll('المشرف العام','مصمم سير العمل')
      .replaceAll('مراجعي الخصوصية','المراجعين').replaceAll('مراجعة الخصوصية','مراجعة الطلب').replaceAll('DPO','سير العمل');
  }}
  clean(data);
  const w=data.workflow, g=w.manual.guides;
  delete g['consent-walkthrough'];
  const english=lang==='en';
  w.help.subtitle=english?'Guides for designing workflows, configuring teams, and handling tasks. All features are available without login.':'أدلة لتصميم سير العمل وإعداد الفرق ومعالجة المهام. جميع الميزات متاحة دون تسجيل دخول.';
  w.help.audience_admin=w.help.badge_admin=english?'Design & setup':'التصميم والإعداد';
  w.help.audience_org=english?'Tasks & teams':'المهام والفرق';
  w.help.nav_admin=english?'Design & setup guide':'دليل التصميم والإعداد';
  w.help.nav_org=english?'Tasks & teams guide':'دليل المهام والفرق';
  g.roles.summary=english?'Feature areas in NWFM. These are guide categories, not access roles.':'مجالات الميزات في NWFM. هذه فئات للأدلة وليست أدوار وصول.';
  g.roles.s1_title=english?'Design and configure':'التصميم والإعداد';
  g.roles.s2_title=english?'Set up participants and process tasks':'إعداد المشاركين ومعالجة المهام';
  g.roles.s3_title=english?'Select the acting participant':'اختيار المشارك المنفذ';
  g.roles.s3_body=english?'Use the participant selector in the header to attribute task actions. NWFM currently has no login or access roles.':'استخدم محدد المشاركين في أعلى الصفحة لإسناد إجراءات المهام. لا يوجد تسجيل دخول أو أدوار وصول في هذا الإصدار.';
  g['register-participants'].s2_title=english?'Enter participant details':'إدخال بيانات المشارك';
  g['register-participants'].s2_body=english?'Enter a name, email, optional Arabic name and employee number, then register. No user account is required.':'أدخل الاسم والبريد الإلكتروني والاسم العربي والرقم الوظيفي الاختياريين ثم سجّل المشارك. لا يلزم حساب مستخدم.';
  g['register-participants'].s2_tip=english?'The header list updates automatically. The configured default participant must remain active.':'تتحدث قائمة المشاركين تلقائياً. يجب أن يبقى المشارك الافتراضي نشطاً.';
  g.workload.s1_body=english?'Open Workload to inspect assignment groups in the active tenant.':'افتح عبء العمل لفحص مجموعات الإسناد في المستأجر الحالي.';
  g['monitor-ops'].s3_body=english?'Use Workload to spot overdue and claimed tasks for the active tenant.':'استخدم عبء العمل لمراجعة المهام المتأخرة والمستلمة في المستأجر الحالي.';
  g['overview'].s3_body=english?'Use Start workflow to create a request. Tasks appear in the selected participant’s inbox. Completing outcomes advances the flow.':'استخدم بدء سير عمل لإنشاء طلب. تظهر المهام في صندوق المشارك المحدد ويؤدي إكمال النتائج إلى تقدم سير العمل.';
  g['bindings-saas'].s2_body=english?'Choose Standalone, entity WorkflowRequest, and trigger RequestSubmitted. The start screen uses active bindings.':'اختر Standalone والكيان WorkflowRequest والمحفز RequestSubmitted. تستخدم شاشة البدء الارتباطات النشطة.';
  g['modes-shadow-active'].s2_body=english?'The Start workflow screen lists Active bindings. Use simulation to inspect routing before starting a live request; Shadow mode remains available for future event integrations.':'تعرض شاشة بدء سير عمل الارتباطات بوضع Active. استخدم المحاكاة لفحص التوجيه قبل بدء طلب؛ يبقى وضع Shadow متاحاً لتكامل الأحداث مستقبلاً.';
  g['modes-shadow-active'].s3_body=english?'Switch the binding to Active to make it available on Start workflow. Standalone results are recorded in workflow history.':'حوّل الارتباط إلى Active ليظهر في شاشة بدء سير عمل. تسجل النتائج المستقلة في سجل سير العمل.';
  g['path-admin-go-live'].s8_body=g['modes-shadow-active'].s3_body;
  const walkthrough=english?{
    title:'Run the sample approval',summary:'Start, claim, and complete the seeded workflow in NWFM.',
    s1_title:'Check the current context',s1_body:'The header shows tenant NWFM and the selected participant. Reviewer 1 and Reviewer 2 belong to the Reviewers group.',s1_tip:'Selecting a participant changes action attribution without a login.',
    s2_title:'Inspect Simple approval',s2_body:'Open Designer & definitions, then the Simple approval version. The published flow is Start → Review request → Completed.',s2_tip:'Clone the version to make changes.',s2_link:'Open Definitions',
    s3_title:'Review assignment',s3_body:'The review task is assigned to the Reviewers group.',s3_tip:'Add participants to groups before assigning them work.',
    s4_title:'Start a request',s4_body:'Open Start workflow, select the standalone binding, enter a request reference, and start.',s4_tip:'Use a meaningful reference to find the request later.',
    s5_title:'Inspect the binding',s5_body:'The binding uses Standalone / WorkflowRequest / RequestSubmitted and screen key workflow.start.',s5_tip:'Active mode makes the binding available on the start screen.',s5_link:'Open Bindings',
    s6_title:'Claim the task',s6_body:'Open Tasks and claim the available review task as Reviewer 1 or Reviewer 2.',s6_tip:'Only the participant who owns the task can complete it.',
    s7_title:'Complete the review',s7_body:'Open Action, enter Approve, add a comment if useful, and complete.',s7_tip:'Custom workflows can define named outcomes and required form fields.',
    s8_title:'Inspect the result',s8_body:'Open Requests or Execution monitor. The sample request is Completed and its history records the action.',s8_tip:'Existing instances retain their published workflow version.',
    s9_title:'Design another process',s9_body:'Create definitions, configure assignments, validate, publish, and bind them to the standalone start screen.',s9_tip:'No external business module is required.'
  }:{
    title:'تشغيل مثال الموافقة',summary:'ابدأ سير العمل التجريبي واستلم المهمة وأكملها في NWFM.',
    s1_title:'تحقق من السياق الحالي',s1_body:'يعرض الرأس المستأجر NWFM والمشارك المحدد. ينتمي المراجعان 1 و2 إلى مجموعة المراجعين.',s1_tip:'يغيّر اختيار المشارك إسناد الإجراءات دون تسجيل دخول.',
    s2_title:'افحص الموافقة البسيطة',s2_body:'افتح التصميم والتعريفات ثم إصدار الموافقة البسيطة. المسار المنشور هو بداية ثم مراجعة الطلب ثم اكتمال.',s2_tip:'استنسخ الإصدار لإجراء تغييرات.',s2_link:'فتح التعريفات',
    s3_title:'راجع الإسناد',s3_body:'تُسند مهمة المراجعة إلى مجموعة المراجعين.',s3_tip:'أضف المشاركين إلى المجموعات قبل إسناد العمل.',
    s4_title:'ابدأ طلباً',s4_body:'افتح بدء سير عمل واختر الارتباط المستقل وأدخل مرجع الطلب ثم ابدأ.',s4_tip:'استخدم مرجعاً واضحاً لتجد الطلب لاحقاً.',
    s5_title:'افحص الارتباط',s5_body:'يستخدم الارتباط Standalone وWorkflowRequest وRequestSubmitted ومفتاح الشاشة workflow.start.',s5_tip:'يتيح الوضع Active الارتباط في شاشة البدء.',s5_link:'فتح الارتباطات',
    s6_title:'استلم المهمة',s6_body:'افتح المهام واستلم مهمة المراجعة المتاحة باسم المراجع 1 أو 2.',s6_tip:'يمكن للمشارك المالك للمهمة إكمالها.',
    s7_title:'أكمل المراجعة',s7_body:'افتح الإجراء وأدخل Approve وأضف تعليقاً عند الحاجة ثم أكمل.',s7_tip:'يمكن لسير العمل المخصص تعريف نتائج وحقول مطلوبة.',
    s8_title:'افحص النتيجة',s8_body:'افتح الطلبات أو مراقبة التنفيذ. تظهر حالة الطلب مكتملة ويسجل التاريخ الإجراء.',s8_tip:'تحتفظ المثيلات الحالية بإصدارها المنشور.',
    s9_title:'صمم عملية أخرى',s9_body:'أنشئ تعريفات وأعد الإسناد وتحقق وانشر واربط بشاشة البدء المستقلة.',s9_tip:'لا تحتاج إلى وحدة أعمال خارجية.'
  };
  g['standalone-walkthrough']=walkthrough;
  function prune(o) { for(const k of Object.keys(o)) {if(['details_consent','details_dsar','load_consent_failed','load_dsar_failed'].includes(k)) delete o[k];else if(typeof o[k]==='object'&&o[k]) prune(o[k]);}}
  prune(w);
  const raw=JSON.stringify(data,null,2).replaceAll('consent-management','workflow.start').replaceAll('مؤسسة → وحدة الموافقة','مؤسسة → سير عمل مستقل');
  fs.writeFileSync(file,raw+'\n');
}
const path='frontend/src/app/features/workflow/help/workflow-manual.content.ts';
fs.writeFileSync(path,fs.readFileSync(path,'utf8').replaceAll('consent-walkthrough','standalone-walkthrough'));
