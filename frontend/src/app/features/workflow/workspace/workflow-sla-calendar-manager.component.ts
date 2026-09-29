import { Component, ElementRef, Injector, afterNextRender, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowCalendarsService } from '../../admin/workflow-calendars/workflow-calendars.service';
import type { BusinessCalendarDto } from '@shared/models/models/Workflow/Application/DTOs/business-calendar-dto';
import { slaErrorMessage } from './workflow-sla-rule.validation';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;
const DAY_AR: Record<string, string> = { Sunday: 'الأحد', Monday: 'الاثنين', Tuesday: 'الثلاثاء', Wednesday: 'الأربعاء', Thursday: 'الخميس', Friday: 'الجمعة', Saturday: 'السبت' };
const TIME_ZONES: string[] = (() => { try { return (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? []; } catch { return []; } })();

/**
 * Calendar administration shared by the SLA page and the designer's SLA editor. Every change is saved on its own
 * request (the calendar API has no draft state), so the component says so rather than implying a later Save applies it.
 */
@Component({selector:'app-workflow-sla-calendar-manager',standalone:true,imports:[FormsModule],template:`
<section class="space-y-4" aria-labelledby="sla-calendar-heading">
  <div>
    <h3 id="sla-calendar-heading" tabindex="-1" class="wf-title text-base font-semibold outline-none">{{ t('Calendars, working periods and holidays','التقاويم وفترات العمل والعطلات') }}</h3>
    <p class="mt-2 rounded-lg px-3 py-2 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">{{ t('Calendar changes are saved immediately and apply to every SLA rule that uses the calendar. Cancelling the SLA editor does not undo them.','تُحفظ تغييرات التقويم فوراً وتنطبق على كل قواعد مدة الخدمة التي تستخدمه. إلغاء محرر الخدمة لا يتراجع عنها.') }}</p>
  </div>
  @if(error()){<p role="alert" class="rounded-lg px-3 py-2 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)">{{ error() }}</p>}
  <p role="status" class="sr-only">{{ status() }}</p>
  @if(loading()){<p class="wf-muted text-sm">{{ t('Loading calendars…','جارٍ تحميل التقاويم…') }}</p>}
  @else {
  <div class="flex flex-wrap items-end gap-2">
    <label class="min-w-0 flex-1 text-sm" for="sla-cal-select">{{ t('Calendar','التقويم') }}
      <select id="sla-cal-select" class="wf-input mt-1" [ngModel]="currentId()" (ngModelChange)="open($event)" [disabled]="creating()">
        <option value="">{{ t('Select a calendar','اختر تقويماً') }}</option>
        @for(c of calendars();track c.id){<option [value]="c.id">{{ c.name }} · {{ c.timeZone }}{{ c.isActive ? '' : ' — ' + t('inactive','غير نشط') }}</option>}
      </select></label>
    @if(!creating()){<button type="button" class="wf-btn-secondary" (click)="startCreate()">{{ t('New calendar','تقويم جديد') }}</button>}
  </div>
  @if(!calendars().length && !creating()){<p class="wf-muted text-sm">{{ t('No calendars exist yet. Create one to use with SLA rules.','لا توجد تقاويم بعد. أنشئ تقويماً لاستخدامه مع قواعد الخدمة.') }}</p>}

  @if(creating()){
  <form class="wf-card space-y-3 p-4" (ngSubmit)="create()" aria-labelledby="sla-cal-create-title">
    <h4 id="sla-cal-create-title" class="wf-title text-sm font-semibold">{{ t('New calendar','تقويم جديد') }}</h4>
    <div class="grid gap-3 sm:grid-cols-2">
      <label class="text-sm">{{ t('Code','الرمز') }}<input id="sla-cal-code" class="wf-input mt-1" name="code" [(ngModel)]="createForm.code" required maxlength="100" autocomplete="off"></label>
      <label class="text-sm">{{ t('Time zone','المنطقة الزمنية') }}<input class="wf-input mt-1" name="tz" [(ngModel)]="createForm.timeZone" required maxlength="100" list="sla-tz-list" dir="ltr"></label>
      <label class="text-sm">{{ t('Name (English)','الاسم (إنجليزي)') }}<input class="wf-input mt-1" name="name" [(ngModel)]="createForm.name" required maxlength="200"></label>
      <label class="text-sm">{{ t('Name (Arabic)','الاسم (عربي)') }}<input class="wf-input mt-1" name="nameAr" [(ngModel)]="createForm.nameAr" maxlength="200" dir="rtl"></label>
    </div>
    <div class="flex flex-wrap gap-2">
      <button class="wf-btn-primary" [disabled]="busy() || !createForm.code.trim() || !createForm.name.trim() || !createForm.timeZone.trim()">{{ busy() ? t('Creating…','جارٍ الإنشاء…') : t('Create calendar','إنشاء التقويم') }}</button>
      <button type="button" class="wf-btn-secondary" [disabled]="busy()" (click)="cancelCreate()">{{ t('Cancel','إلغاء') }}</button>
    </div>
  </form>}

  @if(!creating() && current(); as c){
  <form class="wf-card space-y-3 p-4" (ngSubmit)="saveDetails()" aria-labelledby="sla-cal-details-title">
    <h4 id="sla-cal-details-title" class="wf-title text-sm font-semibold">{{ t('Calendar details','تفاصيل التقويم') }} <span class="app-badge app-badge--code">{{ c.code }}</span></h4>
    <div class="grid gap-3 sm:grid-cols-2">
      <label class="text-sm">{{ t('Name (English)','الاسم (إنجليزي)') }}<input class="wf-input mt-1" name="dname" [(ngModel)]="details.name" required maxlength="200"></label>
      <label class="text-sm">{{ t('Name (Arabic)','الاسم (عربي)') }}<input class="wf-input mt-1" name="dnameAr" [(ngModel)]="details.nameAr" maxlength="200" dir="rtl"></label>
      <label class="text-sm">{{ t('Time zone','المنطقة الزمنية') }}<input class="wf-input mt-1" name="dtz" [(ngModel)]="details.timeZone" required maxlength="100" list="sla-tz-list" dir="ltr"></label>
      <label class="flex items-center gap-2 self-end text-sm"><input type="checkbox" name="dactive" [(ngModel)]="details.isActive"> {{ t('Active','نشط') }}</label>
    </div>
    @if(c.isActive && !details.isActive){<p class="text-sm" style="color:var(--acc-warn-fg)">{{ t('SLA rules that use an inactive calendar cannot be published until they use an active one.','لا يمكن نشر قواعد الخدمة التي تستخدم تقويماً غير نشط حتى تستخدم تقويماً نشطاً.') }}</p>}
    <button class="wf-btn-secondary" [disabled]="busy() || !details.name.trim() || !details.timeZone.trim()">{{ t('Save calendar details','حفظ تفاصيل التقويم') }}</button>
  </form>

  <section class="wf-card space-y-3 p-4" aria-labelledby="sla-cal-periods-title">
    <h4 id="sla-cal-periods-title" tabindex="-1" class="wf-title text-sm font-semibold outline-none">{{ t('Working periods','فترات العمل') }}</h4>
    @if(!c.periods?.length){<p class="text-sm" style="color:var(--acc-warn-fg)">{{ t('No working periods. Business hours and business days need at least one.','لا توجد فترات عمل. ساعات وأيام العمل تتطلب فترة واحدة على الأقل.') }}</p>}
    <ul class="space-y-1">@for(p of c.periods ?? [];track p.id){<li class="flex items-center justify-between gap-2 text-sm"><span>{{ day(p.dayOfWeek) }} <span dir="ltr">{{ time(p.startTime) }} – {{ time(p.endTime) }}</span></span>
      <button type="button" class="text-xs underline" [disabled]="busy()" (click)="remove('periods', p.id!)" [attr.aria-label]="(pendingRemoval()===p.id ? t('Confirm removal of','تأكيد إزالة') : t('Remove','إزالة')) + ' ' + day(p.dayOfWeek) + ' ' + time(p.startTime) + '–' + time(p.endTime)">{{ pendingRemoval()===p.id ? t('Confirm removal','تأكيد الإزالة') : t('Remove','إزالة') }}</button></li>}</ul>
    <form class="flex flex-wrap items-end gap-2" (ngSubmit)="addPeriod()">
      <label class="text-sm">{{ t('Day','اليوم') }}<select class="wf-input mt-1" name="pday" [(ngModel)]="period.dayOfWeek">@for(d of days;track d){<option [value]="d">{{ day(d) }}</option>}</select></label>
      <label class="text-sm">{{ t('Start','البداية') }}<input class="wf-input mt-1" type="time" name="pstart" [(ngModel)]="period.startTime" required></label>
      <label class="text-sm">{{ t('End','النهاية') }}<input class="wf-input mt-1" type="time" name="pend" [(ngModel)]="period.endTime" required></label>
      <button class="wf-btn-secondary" [disabled]="busy() || !period.startTime || !period.endTime">{{ t('Add working period','إضافة فترة عمل') }}</button>
    </form>
    @if(period.startTime && period.endTime && period.endTime <= period.startTime){<p class="text-sm" style="color:var(--acc-danger-fg)">{{ t('The end time must be after the start time.','يجب أن يكون وقت النهاية بعد وقت البداية.') }}</p>}
  </section>

  <section class="wf-card space-y-3 p-4" aria-labelledby="sla-cal-holidays-title">
    <h4 id="sla-cal-holidays-title" tabindex="-1" class="wf-title text-sm font-semibold outline-none">{{ t('Holidays','العطلات') }}</h4>
    @if(!c.holidays?.length){<p class="wf-muted text-sm">{{ t('No holidays.','لا توجد عطلات.') }}</p>}
    <ul class="space-y-1">@for(h of c.holidays ?? [];track h.id){<li class="flex items-center justify-between gap-2 text-sm"><span>{{ locale.locale()==='ar' ? (h.nameAr || h.name) : h.name }} · <span dir="ltr">{{ h.holidayDate }}</span>{{ h.isRecurring ? ' · ' + t('every year','سنوية') : '' }}</span>
      <button type="button" class="text-xs underline" [disabled]="busy()" (click)="remove('holidays', h.id!)" [attr.aria-label]="(pendingRemoval()===h.id ? t('Confirm removal of','تأكيد إزالة') : t('Remove','إزالة')) + ' ' + h.name">{{ pendingRemoval()===h.id ? t('Confirm removal','تأكيد الإزالة') : t('Remove','إزالة') }}</button></li>}</ul>
    <form class="flex flex-wrap items-end gap-2" (ngSubmit)="addHoliday()">
      <label class="text-sm">{{ t('Holiday name','اسم العطلة') }}<input id="sla-cal-holiday-name" class="wf-input mt-1" name="hname" [(ngModel)]="holiday.name" required maxlength="200"></label>
      <label class="text-sm">{{ t('Date','التاريخ') }}<input class="wf-input mt-1" type="date" name="hdate" [(ngModel)]="holiday.holidayDate" required></label>
      <label class="flex items-center gap-2 pb-2 text-sm"><input type="checkbox" name="hrec" [(ngModel)]="holiday.isRecurring"> {{ t('Every year','سنوية') }}</label>
      <button class="wf-btn-secondary" [disabled]="busy() || !holiday.name.trim() || !holiday.holidayDate">{{ t('Add holiday','إضافة عطلة') }}</button>
    </form>
  </section>
  @if(selectable && c.isActive){<button type="button" class="wf-btn-primary" (click)="use.emit(c.id!)">{{ selectedId===c.id ? t('Keep this calendar for the SLA','الإبقاء على هذا التقويم للخدمة') : t('Use this calendar for the SLA','استخدام هذا التقويم للخدمة') }}</button>}
  }
  }
  <datalist id="sla-tz-list">@for(z of timeZones;track z){<option [value]="z"></option>}</datalist>
</section>`})
export class WorkflowSlaCalendarManagerComponent implements OnInit {
  /** The calendar the SLA form currently uses; opened first. */
  @Input() selectedId = '';
  /** Offer "Use this calendar" (the designer editor); the standalone page only administers. */
  @Input() selectable = false;
  /** Any calendar was created or changed; SLA calendar options must be reloaded. */
  @Output() changed = new EventEmitter<void>();
  @Output() use = new EventEmitter<string>();
  private api = inject(WorkflowCalendarsService); private host = inject(ElementRef<HTMLElement>); private injector = inject(Injector);
  protected locale = inject(LocaleService);
  readonly days = DAYS; readonly timeZones = ['UTC', ...TIME_ZONES.filter(z => z !== 'UTC')];
  readonly calendars = signal<BusinessCalendarDto[]>([]); readonly current = signal<BusinessCalendarDto | null>(null); readonly currentId = signal('');
  readonly loading = signal(true); readonly busy = signal(false); readonly error = signal(''); readonly status = signal(''); readonly creating = signal(false); readonly pendingRemoval = signal('');
  createForm = { code: '', name: '', nameAr: '', timeZone: 'UTC' };
  details = { name: '', nameAr: '', timeZone: '', isActive: true };
  period = { dayOfWeek: 'Sunday', startTime: '08:00', endTime: '16:00' };
  holiday = { name: '', holidayDate: '', isRecurring: false };
  private request = 0;
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  day(d: unknown) { const name = String(d); return this.locale.locale() === 'ar' ? DAY_AR[name] ?? name : name; }
  time(v: unknown) { return String(v ?? '').slice(0, 5); }
  ngOnInit() { this.reload(this.selectedId); }
  reload(openId = this.currentId()) {
    this.loading.set(true);
    this.api.list().subscribe({ next: items => { this.calendars.set(items); this.loading.set(false); if (openId && items.some(c => c.id === openId)) this.open(openId); },
      error: e => { this.loading.set(false); this.error.set(slaErrorMessage(e, this.t.bind(this))); } });
  }
  open(id: string) {
    const request = ++this.request; this.currentId.set(id); this.pendingRemoval.set(''); this.error.set('');
    if (!id) { this.current.set(null); return; }
    this.api.get(id).subscribe({ next: c => { if (request !== this.request) return; this.current.set(c); this.details = { name: c.name ?? '', nameAr: c.nameAr ?? '', timeZone: c.timeZone ?? '', isActive: !!c.isActive }; },
      error: e => { if (request === this.request) this.error.set(slaErrorMessage(e, this.t.bind(this))); } });
  }
  startCreate() { this.createForm = { code: '', name: '', nameAr: '', timeZone: 'UTC' }; this.creating.set(true); this.focus('#sla-cal-code'); }
  cancelCreate() { this.creating.set(false); this.focus('#sla-cal-select'); }
  create() {
    if (this.busy()) return; const f = this.createForm;
    this.run(this.api.create({ code: f.code.trim(), name: f.name.trim(), nameAr: f.nameAr.trim() || null, timeZone: f.timeZone.trim() }), this.t('Calendar created.', 'تم إنشاء التقويم.'), created => {
      this.creating.set(false); this.reload((created as BusinessCalendarDto).id ?? ''); this.focus('#sla-cal-select'); });
  }
  saveDetails() {
    const id = this.currentId(); if (!id || this.busy()) return; const d = this.details;
    this.run(this.api.update(id, { name: d.name.trim(), nameAr: d.nameAr.trim() || null, timeZone: d.timeZone.trim(), isActive: d.isActive }), this.t('Calendar details saved.', 'تم حفظ تفاصيل التقويم.'), () => this.reload(id));
  }
  addPeriod() {
    const id = this.currentId(); if (!id || this.busy()) return;
    if (this.period.endTime <= this.period.startTime) { this.error.set(this.t('The end time must be after the start time.', 'يجب أن يكون وقت النهاية بعد وقت البداية.')); return; }
    this.run(this.api.addPeriod(id, this.period), this.t('Working period added.', 'تمت إضافة فترة العمل.'), () => this.open(id));
  }
  addHoliday() {
    const id = this.currentId(); if (!id || this.busy()) return;
    this.run(this.api.addHoliday(id, { ...this.holiday, name: this.holiday.name.trim() }), this.t('Holiday added.', 'تمت إضافة العطلة.'), () => { this.holiday = { name: '', holidayDate: '', isRecurring: false }; this.open(id); this.focus('#sla-cal-holiday-name'); });
  }
  /** Two-step: the first activation asks for confirmation in place, the second removes. */
  remove(kind: 'periods' | 'holidays', itemId: string) {
    const id = this.currentId(); if (!id || this.busy()) return;
    if (this.pendingRemoval() !== itemId) { this.pendingRemoval.set(itemId); return; }
    this.run(this.api.removeItem(id, kind, itemId), this.t('Removed.', 'تمت الإزالة.'), () => { this.pendingRemoval.set(''); this.open(id); this.focus(kind === 'periods' ? '#sla-cal-periods-title' : '#sla-cal-holidays-title'); });
  }
  private run(request: Observable<unknown>, done: string, then: (result: unknown) => void) {
    this.busy.set(true); this.error.set(''); this.status.set('');
    request.subscribe({ next: result => { this.busy.set(false); this.status.set(done); this.changed.emit(); then(result); },
      error: e => { this.busy.set(false); this.error.set(slaErrorMessage(e, this.t.bind(this))); } });
  }
  /** After the next render: coalesced change detection renders on an animation frame, after a zero-delay timer would run. */
  private focus(selector: string) { afterNextRender(() => (this.host.nativeElement.querySelector(selector) as HTMLElement | null)?.focus(), { injector: this.injector }); }
}
