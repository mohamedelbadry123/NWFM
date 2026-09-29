import { Component, ElementRef, Injector, afterNextRender, EventEmitter, HostListener, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { A11yModule } from '@angular/cdk/a11y';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { ToastService } from '@core/notifications/toast.service';
import { SlaCalendarOption, SlaContext, SlaRule, WorkflowSlaRulesService } from './workflow-sla-rules.service';
import { ReferenceItem, WorkflowWorkspaceService } from './workflow-workspace.service';
import { SLA_UNITS, SLA_UNIT_LABELS, SlaDraft, SlaField, sameDraft, slaErrorMessage, toDraft, toRule, validateSlaDraft } from './workflow-sla-rule.validation';
import { WorkflowSlaCalendarManagerComponent } from './workflow-sla-calendar-manager.component';

/** What the editor is opened for. The designer locks Department + FA Type to the selected activity; the SLA page does not. */
export interface SlaEditorRequest {
  locked: boolean;
  departmentCode?: string;
  fieldActivityCode?: string;
  /** SLA page: the rule picked from the list. The designer always edits the rule the context resolves to. */
  rule?: SlaRule | null;
  activityName?: string;
  /** Activities in the open (possibly unsaved) workflow that use the same combination, the selected one included. */
  currentWorkflowMatches?: number;
  /** The draft being designed; its saved activities are left out of the server usage count so they are not counted twice. */
  versionId?: string;
}

const FIELD_ORDER: SlaField[] = ['departmentCode', 'fieldActivityCode', 'name', 'duration', 'durationUnit', 'calendarId', 'reminders', 'overdue', 'isActive'];

/**
 * The one SLA rule editor. A side drawer so the workflow stays visible behind it; calendar administration swaps the
 * drawer body instead of stacking another dialog, which keeps the unsaved rule in memory and a single focus trap.
 */
@Component({selector:'app-workflow-sla-editor',standalone:true,imports:[FormsModule,A11yModule,WorkflowSlaCalendarManagerComponent],template:`
<div class="fixed inset-0 z-[210] flex justify-end">
  <div class="absolute inset-0 bg-black/30" aria-hidden="true" (click)="requestClose()"></div>
  <aside role="dialog" aria-modal="true" aria-labelledby="sla-editor-title" aria-describedby="sla-editor-context" cdkTrapFocus
    class="wf-modal relative flex h-full max-w-xl flex-col rounded-none border-y-0 border-e-0 text-ink-900 dark:text-white" [attr.aria-busy]="loading() || saving()">
    <header class="flex items-start justify-between gap-3 border-b border-ink-200 px-5 py-4 dark:border-surface-600">
      <div class="min-w-0">
        <h2 id="sla-editor-title" tabindex="-1" class="wf-title text-lg font-semibold outline-none">{{ title() }}</h2>
        <p id="sla-editor-context" class="wf-muted mt-1 text-sm">
          @if(request.locked && request.activityName){ {{ t('For activity','للنشاط') }} “{{ request.activityName }}” · }
          {{ contextLabel() }}
        </p>
      </div>
      <button type="button" class="rounded-lg px-2 py-1 text-ink-500 hover:text-ink-900 dark:text-ink-300 dark:hover:text-white" (click)="requestClose()" [attr.aria-label]="t('Close SLA editor','إغلاق محرر الخدمة')">✕</button>
    </header>

    <div class="flex-1 space-y-4 overflow-y-auto px-5 py-4">
      @if(loading()){<p role="status" class="wf-muted text-sm">{{ t('Loading SLA details…','جارٍ تحميل تفاصيل الخدمة…') }}</p>}
      @else if(loadError()){
        <div role="alert" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)"><p>{{ loadError() }}</p>
        <button type="button" class="wf-btn-secondary" (click)="load()">{{ t('Try again','حاول مرة أخرى') }}</button></div>
      }
      @else if(view()==='calendar'){
        <button type="button" id="sla-back-to-rule" class="text-sm underline" (click)="showRule()">{{ locale.isRtl() ? '→' : '←' }} {{ t('Back to SLA rule','العودة إلى قاعدة الخدمة') }}</button>
        <p class="wf-muted text-sm">{{ t('Your SLA rule changes are kept while you manage calendars.','يتم الاحتفاظ بتعديلات قاعدة الخدمة أثناء إدارة التقاويم.') }}</p>
        <app-workflow-sla-calendar-manager [selectedId]="draft().calendarId" [selectable]="true" (changed)="reloadCalendars()" (use)="useCalendar($event)" />
      }
      @else {
        @if(context()?.rule && !context()!.calendarActive){
          <p role="status" class="rounded-lg px-3 py-2 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)">{{ t('This rule’s calendar is inactive, so activities using it have no usable SLA and cannot be published. Select an active calendar and save.','تقويم هذه القاعدة غير نشط، لذا لا توجد مدة خدمة صالحة للأنشطة التي تستخدمها ولا يمكن نشرها. اختر تقويماً نشطاً واحفظ.') }}</p>
        }
        @if(inactiveCandidate(); as old){
          <div class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-rule-bg);color:var(--acc-rule-fg)">
            <p>{{ t('An inactive rule already exists for this combination:','توجد قاعدة غير نشطة لهذه المجموعة:') }} <strong>{{ old.name }}</strong> ({{ old.duration }} {{ unitLabel(old.durationUnit) }}).</p>
            <button type="button" class="underline" (click)="reuse(old)">{{ t('Review and reactivate it instead','مراجعتها وإعادة تفعيلها بدلاً من ذلك') }}</button>
          </div>
        }
        <form id="sla-rule-form" class="space-y-4" (ngSubmit)="save()" novalidate>
          <fieldset class="space-y-3">
            <legend class="wf-title text-sm font-semibold">{{ t('Applies to','ينطبق على') }}</legend>
            @if(request.locked){
              <dl class="grid grid-cols-2 gap-3 rounded-lg border border-ink-200 px-3 py-2 text-sm dark:border-surface-600">
                <div><dt class="wf-muted text-xs">{{ t('Department','القسم') }}</dt><dd class="font-medium">{{ departmentLabel(draft().departmentCode) }}</dd></div>
                <div><dt class="wf-muted text-xs">{{ t('Field Activity Type','نوع النشاط الميداني') }}</dt><dd class="font-medium">{{ fieldLabel(draft().fieldActivityCode) }}</dd></div>
                <p class="wf-muted col-span-2 text-xs">{{ t('Taken from the activity’s General settings and locked here, so the rule cannot be saved for a different combination.','مأخوذ من الإعدادات العامة للنشاط ومقفل هنا حتى لا تُحفظ القاعدة لمجموعة مختلفة.') }}</p>
              </dl>
            } @else {
              <div class="grid gap-3 sm:grid-cols-2">
                <label class="text-sm" for="sla-departmentCode">{{ t('Department','القسم') }}
                  <select id="sla-departmentCode" name="department" class="wf-input mt-1" [ngModel]="draft().departmentCode" (ngModelChange)="department($event)" (blur)="touch('departmentCode')" [attr.aria-invalid]="!!err('departmentCode')" [attr.aria-describedby]="err('departmentCode') ? 'sla-departmentCode-error' : null">
                    <option value="">—</option>@for(d of departments();track d.code){<option [value]="d.code">{{ label(d) }}</option>}</select></label>
                <label class="text-sm" for="sla-fieldActivityCode">{{ t('Field Activity Type','نوع النشاط الميداني') }}
                  <select id="sla-fieldActivityCode" name="field" class="wf-input mt-1" [disabled]="!draft().departmentCode" [ngModel]="draft().fieldActivityCode" (ngModelChange)="field($event)" (blur)="touch('fieldActivityCode')" [attr.aria-invalid]="!!err('fieldActivityCode')" [attr.aria-describedby]="err('fieldActivityCode') ? 'sla-fieldActivityCode-error' : null">
                    <option value="">—</option>@for(d of fieldTypes();track d.code){<option [value]="d.code">{{ label(d) }}</option>}</select></label>
                @if(err('departmentCode'); as e){<p id="sla-departmentCode-error" class="text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}
                @if(err('fieldActivityCode'); as e){<p id="sla-fieldActivityCode-error" class="text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}
              </div>
            }
          </fieldset>

          <label class="block text-sm" for="sla-name">{{ t('Rule name','اسم القاعدة') }}
            <input id="sla-name" name="name" class="wf-input mt-1" maxlength="200" autocomplete="off" [ngModel]="draft().name" (ngModelChange)="patch({name:$event})" (blur)="touch('name')" [attr.aria-invalid]="!!err('name')" [attr.aria-describedby]="err('name') ? 'sla-name-error' : null"></label>
          @if(err('name'); as e){<p id="sla-name-error" class="-mt-3 text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}

          <div class="grid gap-3 sm:grid-cols-2">
            <label class="text-sm" for="sla-duration">{{ t('Duration','المدة') }}
              <input id="sla-duration" name="duration" type="number" min="1" max="87600" step="1" inputmode="numeric" class="wf-input mt-1" [ngModel]="draft().duration" (ngModelChange)="patch({duration:$event})" (blur)="touch('duration')" [attr.aria-invalid]="!!err('duration')" [attr.aria-describedby]="err('duration') ? 'sla-duration-error' : null"></label>
            <label class="text-sm" for="sla-durationUnit">{{ t('Unit','الوحدة') }}
              <select id="sla-durationUnit" name="unit" class="wf-input mt-1" [ngModel]="draft().durationUnit" (ngModelChange)="patch({durationUnit:$event})" [attr.aria-invalid]="!!err('durationUnit')">
                @for(unit of units;track unit){<option [value]="unit">{{ unitLabel(unit) }}</option>}</select></label>
          </div>
          @if(err('duration'); as e){<p id="sla-duration-error" class="-mt-3 text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}
          @if(err('durationUnit'); as e){<p class="-mt-3 text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}

          <div class="space-y-1">
            <label class="block text-sm" for="sla-calendarId">{{ t('Calendar','التقويم') }}
              <select id="sla-calendarId" name="calendar" class="wf-input mt-1" [ngModel]="draft().calendarId" (ngModelChange)="patch({calendarId:$event})" (blur)="touch('calendarId')" [attr.aria-invalid]="!!err('calendarId')" [attr.aria-describedby]="err('calendarId') ? 'sla-calendarId-error' : 'sla-calendar-hint'">
                <option value="">{{ calendars().length ? t('Select a calendar','اختر تقويماً') : t('No active calendars','لا توجد تقاويم نشطة') }}</option>
                @if(unavailableCalendar(); as u){<option [value]="u.id" disabled>{{ u.name }} — {{ t('inactive','غير نشط') }}</option>}
                @for(c of calendars();track c.id){<option [value]="c.id">{{ c.name }} · {{ c.timeZone }}</option>}</select></label>
            @if(err('calendarId'); as e){<p id="sla-calendarId-error" class="text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}
            <p id="sla-calendar-hint" class="wf-muted text-xs">{{ t('Deadlines are counted in the calendar’s time zone; business units skip non-working time and holidays.','تُحتسب المواعيد بالمنطقة الزمنية للتقويم؛ وحدات العمل تتجاوز أوقات عدم العمل والعطلات.') }}
              @if(selectedCalendar(); as c){ {{ t('Time zone:','المنطقة الزمنية:') }} <span dir="ltr">{{ c.timeZone }}</span>.}</p>
            @if(canCalendars()){<button type="button" id="sla-manage-calendars" class="text-sm underline" (click)="showCalendars()">{{ t('Manage calendars, working periods and holidays','إدارة التقاويم وفترات العمل والعطلات') }}</button>}
            @else {<p class="wf-muted text-xs">{{ t('Creating or changing calendars requires the Manage Calendars permission.','إنشاء التقاويم أو تعديلها يتطلب صلاحية إدارة التقاويم.') }}</p>}
          </div>

          <label class="block text-sm" for="sla-reminders">{{ t('Reminders before the deadline (minutes)','التذكيرات قبل الاستحقاق (بالدقائق)') }}
            <input id="sla-reminders" name="reminders" class="wf-input mt-1" dir="ltr" placeholder="60, 15" [ngModel]="draft().reminders" (ngModelChange)="patch({reminders:$event})" (blur)="touch('reminders')" [attr.aria-invalid]="!!err('reminders')" [attr.aria-describedby]="err('reminders') ? 'sla-reminders-error' : 'sla-reminders-hint'"></label>
          <p [id]="err('reminders') ? 'sla-reminders-error' : 'sla-reminders-hint'" class="-mt-3 text-xs" [class.wf-muted]="!err('reminders')" [style.color]="err('reminders') ? 'var(--acc-danger-fg)' : null">{{ err('reminders') || t('Comma-separated; up to 10. Leave empty for no reminders.','مفصولة بفواصل؛ حتى 10. اتركها فارغة لعدم التذكير.') }}</p>

          <label class="block text-sm" for="sla-overdue">{{ t('Overdue alerts after the deadline (minutes)','تنبيهات التأخير بعد الاستحقاق (بالدقائق)') }}
            <input id="sla-overdue" name="overdue" class="wf-input mt-1" dir="ltr" placeholder="0, 60" [ngModel]="draft().overdue" (ngModelChange)="patch({overdue:$event})" (blur)="touch('overdue')" [attr.aria-invalid]="!!err('overdue')" [attr.aria-describedby]="err('overdue') ? 'sla-overdue-error' : 'sla-overdue-hint'"></label>
          <p [id]="err('overdue') ? 'sla-overdue-error' : 'sla-overdue-hint'" class="-mt-3 text-xs" [class.wf-muted]="!err('overdue')" [style.color]="err('overdue') ? 'var(--acc-danger-fg)' : null">{{ err('overdue') || t('Comma-separated; up to 10. 0 alerts at the deadline itself.','مفصولة بفواصل؛ حتى 10. القيمة 0 تنبه عند الاستحقاق نفسه.') }}</p>

          <label class="flex items-center gap-2 text-sm" for="sla-isActive"><input id="sla-isActive" type="checkbox" name="active" [ngModel]="draft().isActive" (ngModelChange)="patch({isActive:$event})" [attr.aria-describedby]="err('isActive') ? 'sla-isActive-error' : null"> {{ t('Active','نشط') }}</label>
          @if(err('isActive'); as e){<p id="sla-isActive-error" class="-mt-3 text-xs" style="color:var(--acc-danger-fg)">{{ e }}</p>}

          @if(draft().departmentCode && draft().fieldActivityCode){
          <section class="space-y-1 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)" aria-labelledby="sla-impact-title">
            <h3 id="sla-impact-title" class="font-semibold">{{ isNew() ? t('This creates a shared rule','سيتم إنشاء قاعدة مشتركة') : t('This is a shared rule','هذه قاعدة مشتركة') }}</h3>
            @for(line of impact();track $index){<p>{{ line }}</p>}
          </section>}
        </form>
        @if(saveError()){<p id="sla-save-error" role="alert" tabindex="-1" class="rounded-lg px-3 py-2 text-sm outline-none" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)">{{ saveError() }}</p>}
        @if(submitted() && errorCount()){<p role="alert" class="text-sm" style="color:var(--acc-danger-fg)">{{ t('Fix the highlighted fields before saving.','صحّح الحقول المحددة قبل الحفظ.') }}</p>}
      }
    </div>

    <footer class="space-y-2 border-t border-ink-200 px-5 py-3 dark:border-surface-600">
      @if(confirmDiscard()){
        <div role="alertdialog" aria-labelledby="sla-discard-text" class="flex flex-wrap items-center justify-between gap-2">
          <p id="sla-discard-text" class="text-sm font-medium">{{ t('Discard your unsaved SLA changes?','تجاهل تعديلات الخدمة غير المحفوظة؟') }}</p>
          <div class="flex gap-2"><button type="button" id="sla-keep-editing" class="wf-btn-secondary" (click)="keepEditing()">{{ t('Keep editing','متابعة التعديل') }}</button>
          <button type="button" class="wf-btn-primary" style="background:var(--acc-danger-fg)" (click)="discard()">{{ t('Discard changes','تجاهل التغييرات') }}</button></div>
        </div>
      } @else if(view()==='calendar'){
        <button type="button" class="wf-btn-secondary" (click)="showRule()">{{ t('Back to SLA rule','العودة إلى قاعدة الخدمة') }}</button>
      } @else {
        <div class="flex flex-wrap items-center gap-2">
          <button type="submit" form="sla-rule-form" class="wf-btn-primary" [disabled]="loading() || !!loadError() || saving()">{{ saving() ? t('Saving…','جارٍ الحفظ…') : t('Save SLA rule','حفظ قاعدة الخدمة') }}</button>
          <button type="button" class="wf-btn-secondary" [disabled]="saving()" (click)="requestClose()">{{ t('Cancel','إلغاء') }}</button>
        </div>
        <p class="wf-muted text-xs">{{ request.locked ? t('Saves the SLA rule only. Your workflow is not saved or published; save it separately.','يحفظ قاعدة الخدمة فقط. لا يتم حفظ مسار العمل أو نشره؛ احفظه بشكل منفصل.') : t('Saves the SLA rule only. Published workflows keep their captured SLA.','يحفظ قاعدة الخدمة فقط. تحتفظ مسارات العمل المنشورة بمدة الخدمة المحفوظة فيها.') }}</p>
      }
    </footer>
  </aside>
</div>`})
export class WorkflowSlaEditorComponent implements OnInit {
  @Input({required:true}) request!: SlaEditorRequest;
  /** The saved rule, or null when the editor was cancelled or dismissed. */
  @Output() closed = new EventEmitter<SlaRule | null>();
  private api = inject(WorkflowSlaRulesService); private references = inject(WorkflowWorkspaceService); private auth = inject(AuthStore); private toast = inject(ToastService); private host = inject(ElementRef<HTMLElement>); private injector = inject(Injector);
  protected locale = inject(LocaleService);
  readonly units = SLA_UNITS;
  readonly view = signal<'rule' | 'calendar'>('rule');
  readonly loading = signal(true); readonly loadError = signal(''); readonly saving = signal(false); readonly saveError = signal(''); readonly submitted = signal(false); readonly confirmDiscard = signal(false);
  readonly context = signal<SlaContext | null>(null); readonly calendars = signal<SlaCalendarOption[]>([]);
  readonly departments = signal<ReferenceItem[]>([]); readonly fieldTypes = signal<ReferenceItem[]>([]);
  readonly draft = signal<SlaDraft>(toDraft(null)); readonly initial = signal<SlaDraft>(toDraft(null)); readonly touched = signal<ReadonlySet<SlaField>>(new Set());
  readonly errors = computed(() => validateSlaDraft(this.draft(), this.calendars(), this.context()?.rule?.id));
  readonly errorCount = computed(() => Object.keys(this.errors()).length);
  readonly dirty = computed(() => !sameDraft(this.draft(), this.initial()));
  readonly isNew = computed(() => !this.draft().id);
  readonly selectedCalendar = computed(() => this.calendars().find(c => c.id === this.draft().calendarId) ?? null);
  /** The rule's current calendar when it is no longer offered, so the select does not silently look empty. */
  readonly unavailableCalendar = computed(() => { const d = this.draft(); const rule = this.context()?.rule; return d.calendarId && !this.selectedCalendar() && rule?.calendarId === d.calendarId ? { id: d.calendarId, name: rule.calendarName ?? d.calendarId } : null; });
  readonly inactiveCandidate = computed(() => this.request.locked && this.isNew() && !this.context()?.rule ? this.context()?.inactiveRules[0] ?? null : null);
  readonly canCalendars = computed(() => this.auth.roles().includes('Administrator') || this.auth.hasAnyPermission('ManageCalendars'));
  private contextRequest = 0; private fieldRequest = 0;

  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  label(d: ReferenceItem) { return this.locale.locale() === 'ar' ? d.nameAr || d.nameEn : d.nameEn || d.nameAr; }
  departmentLabel(code: string) { const d = this.departments().find(x => x.code === code); return d ? this.label(d) : code; }
  fieldLabel(code: string) { const d = this.fieldTypes().find(x => x.code === code); return d ? this.label(d) : code; }
  unitLabel(unit: string) { const l = SLA_UNIT_LABELS[unit]; return l ? this.t(l[0], l[1]) : unit; }
  title() { return this.view() === 'calendar' ? this.t('Manage calendars', 'إدارة التقاويم') : this.isNew() ? this.t('Create SLA rule', 'إنشاء قاعدة خدمة') : this.t('Edit SLA rule', 'تعديل قاعدة الخدمة'); }
  contextLabel() { const d = this.draft(); return d.departmentCode && d.fieldActivityCode ? this.departmentLabel(d.departmentCode) + ' · ' + this.fieldLabel(d.fieldActivityCode) : this.t('Choose the Department and Field Activity Type the rule applies to.', 'اختر القسم ونوع النشاط الميداني الذي تنطبق عليه القاعدة.'); }
  err(field: SlaField): string | null { const e = this.errors()[field]; return e && (this.submitted() || this.touched().has(field)) ? this.t(e[0], e[1]) : null; }
  touch(field: SlaField) { if (!this.touched().has(field)) this.touched.update(s => new Set([...s, field])); }
  patch(change: Partial<SlaDraft>) { this.draft.update(d => ({ ...d, ...change })); this.saveError.set(''); }

  impact(): string[] {
    const usage = this.context()?.usage; const d = this.draft(); const initial = this.initial(); const lines: string[] = [];
    lines.push(this.t(`Every activity with Department “${this.departmentLabel(d.departmentCode)}” and Field Activity Type “${this.fieldLabel(d.fieldActivityCode)}” uses this rule — not only this activity.`,
      `كل نشاط قسمه “${this.departmentLabel(d.departmentCode)}” ونوعه “${this.fieldLabel(d.fieldActivityCode)}” يستخدم هذه القاعدة، وليس هذا النشاط فقط.`));
    if (this.request.locked) { const n = this.request.currentWorkflowMatches ?? 1; lines.push(this.t(`In this workflow: ${n} ${n === 1 ? 'activity' : 'activities'}, including this one.`, `في مسار العمل هذا: ${n} نشاط، بما فيها هذا النشاط.`)); }
    if (usage) {
      const others = this.request.locked ? this.t('Other draft workflows', 'مسارات عمل مسودة أخرى') : this.t('Draft workflows', 'مسارات عمل مسودة');
      lines.push(usage.draftActivities
        ? `${others}: ${this.t(`${usage.draftActivities} activities in ${usage.draftWorkflows} workflows`, `${usage.draftActivities} نشاط في ${usage.draftWorkflows} مسار عمل`)} (${usage.draftWorkflowNames.join(this.t(', ', '، '))}${usage.draftWorkflows > usage.draftWorkflowNames.length ? '…' : ''}).`
        : `${others}: ${this.t('none', 'لا يوجد')}.`);
      lines.push(this.t(`${usage.publishedVersions} published ${usage.publishedVersions === 1 ? 'version keeps' : 'versions keep'} the SLA captured at publication, and running requests are not changed. Drafts use the saved rule when they are published.`,
        `${usage.publishedVersions} نسخة منشورة تحتفظ بمدة الخدمة المحفوظة عند النشر، ولا تتغير الطلبات الجارية. تستخدم المسودات القاعدة المحفوظة عند نشرها.`));
    }
    if (!this.isNew() && initial.isActive && !d.isActive)
      lines.push(this.t('Deactivating leaves these activities without an SLA; their workflows cannot be published until an active rule exists.', 'إلغاء التفعيل يترك هذه الأنشطة بلا مدة خدمة؛ ولا يمكن نشر مساراتها حتى توجد قاعدة نشطة.'));
    if (!this.isNew() && (initial.departmentCode !== d.departmentCode || initial.fieldActivityCode !== d.fieldActivityCode))
      lines.push(this.t('Moving the rule to another combination removes it from activities that use the previous one.', 'نقل القاعدة إلى مجموعة أخرى يزيلها من الأنشطة التي تستخدم المجموعة السابقة.'));
    return lines;
  }

  ngOnInit() { this.focus('#sla-editor-title'); this.load(); }

  load() {
    const r = this.request; const department = r.locked ? r.departmentCode ?? '' : r.rule?.departmentCode ?? '';
    const field = r.locked ? r.fieldActivityCode ?? '' : r.rule?.fieldActivityCode ?? '';
    this.loading.set(true); this.loadError.set('');
    forkJoin({
      calendars: this.api.calendars(),
      departments: this.references.references('departments'),
      fields: department ? this.references.references('field-activity-types', department) : of([] as ReferenceItem[]),
      context: department && field ? this.api.context(department, field, r.locked ? r.versionId : undefined) : of(null),
    }).subscribe({
      next: ({ calendars, departments, fields, context }) => {
        this.calendars.set(calendars); this.departments.set(departments); this.fieldTypes.set(fields); this.context.set(context);
        const rule = r.locked ? context?.rule ?? null : r.rule ?? null;
        const draft = toDraft(rule, { departmentCode: department, fieldActivityCode: field });
        if (!rule && department && field) draft.name = `${this.departmentLabel(department)} · ${this.fieldLabel(field)}`;
        this.draft.set(draft); this.initial.set(draft); this.loading.set(false);
        this.focus('#sla-name');
      },
      error: e => { this.loading.set(false); this.loadError.set(slaErrorMessage(e, this.t.bind(this))); },
    });
  }

  department(code: string) {
    this.patch({ departmentCode: code, fieldActivityCode: '' }); this.fieldTypes.set([]); this.context.set(null);
    const request = ++this.fieldRequest; if (!code) return;
    this.references.references('field-activity-types', code).subscribe({ next: items => { if (request === this.fieldRequest) this.fieldTypes.set(items); },
      error: e => { if (request === this.fieldRequest) this.saveError.set(slaErrorMessage(e, this.t.bind(this))); } });
  }

  /** SLA page only: the chosen combination decides duplicate checks and the shared-impact summary. */
  field(code: string) {
    this.patch({ fieldActivityCode: code }); this.context.set(null);
    const department = this.draft().departmentCode; const request = ++this.contextRequest; if (!department || !code) return;
    this.api.context(department, code).pipe(catchError(() => of(null))).subscribe(context => {
      if (request === this.contextRequest && this.draft().departmentCode === department && this.draft().fieldActivityCode === code) this.context.set(context); });
  }

  reuse(rule: SlaRule) { const draft = toDraft(rule); this.initial.set(draft); this.draft.set({ ...draft, isActive: true }); this.focus('#sla-name'); }

  reloadCalendars() { this.api.calendars().subscribe({ next: c => this.calendars.set(c), error: e => this.saveError.set(slaErrorMessage(e, this.t.bind(this))) }); }
  showCalendars() { this.view.set('calendar'); this.focus('#sla-calendar-heading'); }
  showRule() { this.view.set('rule'); this.focus(this.canCalendars() ? '#sla-manage-calendars' : '#sla-calendarId'); }
  useCalendar(id: string) { this.patch({ calendarId: id }); this.touch('calendarId'); this.view.set('rule'); this.focus('#sla-calendarId'); }

  save() {
    if (this.saving() || this.loading()) return;
    this.submitted.set(true); this.saveError.set('');
    if (this.errorCount()) { const first = FIELD_ORDER.find(f => this.errors()[f]); if (first) this.focus('#sla-' + first); return; }
    const initial = this.initial(); const previous = initial.id ? { departmentCode: initial.departmentCode, fieldActivityCode: initial.fieldActivityCode } : undefined;
    this.saving.set(true);
    this.api.save(toRule(this.draft()), previous).subscribe({
      next: saved => {
        this.saving.set(false); this.initial.set(this.draft());
        this.toast.success(this.request.locked ? this.t('SLA rule saved. The workflow itself was not saved or published.', 'تم حفظ قاعدة الخدمة. لم يتم حفظ مسار العمل أو نشره.') : this.t('SLA rule saved.', 'تم حفظ قاعدة الخدمة.'));
        this.closed.emit(saved);
      },
      error: e => { this.saving.set(false); this.saveError.set(slaErrorMessage(e, this.t.bind(this))); this.focus('#sla-save-error'); },
    });
  }

  /** Document level so Escape still works when focus was dropped (e.g. a button that disappeared after an action). */
  @HostListener('document:keydown.escape', ['$event'])
  escape(event: Event) {
    event.stopPropagation(); event.preventDefault();
    if (this.confirmDiscard()) this.keepEditing(); else if (this.view() === 'calendar') this.showRule(); else this.requestClose();
  }
  /** Saving cannot be abandoned half way: the response would arrive after the user has moved on. */
  requestClose() { if (this.saving()) return; if (this.dirty() && !this.loadError()) { this.confirmDiscard.set(true); this.focus('#sla-keep-editing'); } else this.closed.emit(null); }
  keepEditing() { this.confirmDiscard.set(false); this.focus(this.view() === 'calendar' ? '#sla-calendar-heading' : '#sla-name'); }
  discard() { this.confirmDiscard.set(false); this.closed.emit(null); }

  @HostListener('window:beforeunload', ['$event'])
  protected beforeUnload(event: BeforeUnloadEvent) { if (this.dirty()) event.preventDefault(); }

  /** After the next render: coalesced change detection renders on an animation frame, after a zero-delay timer would run. */
  private focus(selector: string) { afterNextRender(() => (this.host.nativeElement.querySelector(selector) as HTMLElement | null)?.focus(), { injector: this.injector }); }
}
