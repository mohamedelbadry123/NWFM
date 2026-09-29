import { Component, ElementRef, EventEmitter, HostListener, Injector, Input, OnInit, Output, afterNextRender, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { A11yModule } from '@angular/cdk/a11y';
import { LocaleService } from '@core/i18n/locale.service';
import { DynamicFormRendererComponent } from '@shared/components/dynamic-form/dynamic-form-renderer.component';
import { ActivityFormPreview, WorkflowActivityFormsService } from './workflow-activity-forms.service';
import type { ActivityFormPreviewRequest } from './workflow-activity-forms.component';

/**
 * A read-only look at one of an activity's related forms, rendered by the same renderer that fills it. Modal over the
 * designer, so the workflow draft, the selected activity and the canvas stay exactly as they were. Nothing is submitted
 * or saved: no form id reaches the renderer (media stays in the browser) and its submit event goes nowhere.
 */
@Component({selector:'app-workflow-form-preview',standalone:true,imports:[A11yModule,DynamicFormRendererComponent],template:`
<div class="fixed inset-0 z-[210] flex items-center justify-center p-4">
  <div class="absolute inset-0 bg-black/40" aria-hidden="true" (click)="closed.emit()"></div>
  <section role="dialog" aria-modal="true" aria-labelledby="form-preview-title" aria-describedby="form-preview-note" cdkTrapFocus
    class="wf-modal relative flex max-h-[90vh] w-full max-w-3xl flex-col text-ink-900 dark:text-white" [attr.aria-busy]="loading()">
    <header class="flex items-start justify-between gap-3 border-b border-ink-200 px-5 py-4 dark:border-surface-600">
      <div class="min-w-0">
        <h2 id="form-preview-title" tabindex="-1" class="wf-title text-lg font-semibold outline-none break-words">{{ t('Preview','معاينة') }}: {{ name() }}</h2>
        <p class="wf-muted mt-1 text-sm"><span dir="ltr" class="font-mono">{{ request.form.code }}</span> · {{ statusText() }}</p>
      </div>
      <button type="button" class="rounded-lg px-2 py-1 text-ink-500 hover:text-ink-900 dark:text-ink-300 dark:hover:text-white" (click)="closed.emit()" [attr.aria-label]="t('Close preview','إغلاق المعاينة')">✕</button>
    </header>
    <div class="flex-1 space-y-4 overflow-y-auto px-5 py-4">
      <p id="form-preview-note" class="rounded-lg px-3 py-2 text-sm" style="background:var(--acc-brand-bg);color:var(--acc-brand-fg)">{{ t('Preview only. You can try the fields, but nothing is submitted or saved, and your workflow is not changed.','معاينة فقط. يمكنك تجربة الحقول، لكن لا يتم إرسال أو حفظ أي شيء، ولا يتغير مسار العمل.') }}</p>
      @if(request.form.versionNos.length > 1){
        <label class="block text-sm">{{ t('Published version','الإصدار المنشور') }}
          <select id="form-preview-version" class="wf-input" [value]="versionNo()" (change)="select(+$any($event.target).value)">
            @for(v of request.form.versionNos;track v){<option [value]="v">v{{ v }}@if(v === request.form.currentVersionNo){ · {{ t('current','الحالي') }}}</option>}
          </select></label>
      }
      @if(loading()){
        <p role="status" class="text-sm">{{ t('Loading the form…','جارٍ تحميل النموذج…') }}</p>
      } @else if(error()){
        <div role="alert" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)"><p>{{ error() }}</p>
          @if(canRetry()){<button type="button" class="underline" (click)="load()">{{ t('Try again','حاول مرة أخرى') }}</button>}</div>
      } @else if(definition(); as d){
        <app-dynamic-form-renderer [definition]="d" [emptyMessage]="t('This version has no fields.','لا توجد حقول في هذا الإصدار.')" />
      }
    </div>
    <footer class="flex justify-end border-t border-ink-200 px-5 py-3 dark:border-surface-600">
      <button type="button" class="wf-btn-secondary" (click)="closed.emit()">{{ t('Close','إغلاق') }}</button>
    </footer>
  </section>
</div>`})
export class WorkflowFormPreviewComponent implements OnInit {
  @Input({required:true}) request!: ActivityFormPreviewRequest;
  @Output() closed=new EventEmitter<void>();
  private api=inject(WorkflowActivityFormsService); private locale=inject(LocaleService); private host=inject(ElementRef<HTMLElement>); private injector=inject(Injector);
  private call=0;
  readonly versionNo=signal(0); readonly loading=signal(false); readonly error=signal(''); readonly canRetry=signal(true);
  readonly preview=signal<ActivityFormPreview|null>(null); readonly definition=signal<Record<string,unknown>|null>(null);
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  name(){const f=this.request.form;return (this.locale.locale()==='ar'?f.nameAr:f.nameEn)||f.nameEn||f.code;}
  statusText(){
    const v=`v${this.versionNo()}`;
    return this.request.form.isUsable?this.t(`Published version ${v}`,`الإصدار المنشور ${v}`):this.t(`Version ${v} · not available for new work (${this.request.form.status.toLowerCase()})`,`الإصدار ${v} · غير متاح لعمل جديد`);
  }
  ngOnInit(){this.versionNo.set(this.request.versionNo);this.load();afterNextRender(()=>(this.host.nativeElement.querySelector('#form-preview-title') as HTMLElement|null)?.focus(),{injector:this.injector});}
  select(versionNo:number){if(versionNo===this.versionNo())return;this.versionNo.set(versionNo);this.load();}
  load(){
    const call=++this.call,r=this.request;this.loading.set(true);this.error.set('');this.definition.set(null);
    this.api.preview(r.form.id,this.versionNo(),r.departmentCode,r.fieldActivityCode).subscribe({
      next:p=>{if(call!==this.call)return;this.loading.set(false);this.preview.set(p);
        try{this.definition.set(JSON.parse(p.schemaJson) as Record<string,unknown>);}catch{this.canRetry.set(false);this.error.set(this.t('This version’s form definition could not be read.','تعذرت قراءة تعريف النموذج لهذا الإصدار.'));}},
      error:(e:unknown)=>{if(call!==this.call)return;this.loading.set(false);const status=e instanceof HttpErrorResponse?e.status:0;this.canRetry.set(status!==403&&status!==404);
        this.error.set(status===403?this.t('You do not have permission to preview this form.','ليست لديك صلاحية معاينة هذا النموذج.')
          :status===404?this.t('This form or version is no longer filed under the activity’s Department and Field Activity Type.','لم يعد هذا النموذج أو الإصدار مصنفاً تحت قسم النشاط ونوع النشاط الميداني.')
          :this.t('The form could not be loaded.','تعذر تحميل النموذج.'));}});
  }
  /** Document level so Escape still closes the preview wherever focus is inside it. */
  @HostListener('document:keydown.escape',['$event'])
  onEscape(event:Event){event.preventDefault();this.closed.emit();}
}
