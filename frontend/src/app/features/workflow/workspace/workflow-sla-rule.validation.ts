import { HttpErrorResponse } from '@angular/common/http';
import { SlaCalendarOption, SlaRule } from './workflow-sla-rules.service';

/** Mirrors WorkspaceSla.SaveAsync; the server stays authoritative and re-checks everything. */
export const SLA_UNITS = ['Minutes', 'Hours', 'Days', 'BusinessHours', 'BusinessDays'] as const;
export const SLA_LIMITS = { nameMax: 200, durationMax: 87600, thresholdsMax: 10, minutesMax: 525600 } as const;
const BUSINESS_UNITS = new Set(['BusinessHours', 'BusinessDays']);

export type Bilingual = readonly [en: string, ar: string];
export type SlaField = 'name' | 'departmentCode' | 'fieldActivityCode' | 'duration' | 'durationUnit' | 'calendarId' | 'reminders' | 'overdue' | 'isActive';
export type SlaErrors = Partial<Record<SlaField, Bilingual>>;

/** The editable form. Thresholds stay as the text the user typed so a failed save never rewrites their input. */
export interface SlaDraft { id: string; name: string; departmentCode: string; fieldActivityCode: string; duration: number | null; durationUnit: string; calendarId: string; reminders: string; overdue: string; isActive: boolean; }

export const SLA_UNIT_LABELS: Record<string, Bilingual> = {
  Minutes: ['Minutes', 'دقائق'], Hours: ['Hours', 'ساعات'], Days: ['Calendar days', 'أيام تقويمية'],
  BusinessHours: ['Business hours', 'ساعات عمل'], BusinessDays: ['Business days', 'أيام عمل'],
};

/** Published snapshots serialize the enum by value (SlaDurationUnit order); the rule API uses names. */
const SLA_UNIT_BY_VALUE = ['Minutes', 'Hours', 'BusinessHours', 'Days', 'BusinessDays'];
export function slaUnitName(unit: string | number): string { return typeof unit === 'number' ? SLA_UNIT_BY_VALUE[unit] ?? String(unit) : unit; }

export function toDraft(rule: SlaRule | null, context?: { departmentCode: string; fieldActivityCode: string }): SlaDraft {
  return rule
    ? { id: rule.id, name: rule.name, departmentCode: rule.departmentCode, fieldActivityCode: rule.fieldActivityCode, duration: rule.duration, durationUnit: rule.durationUnit,
        calendarId: rule.calendarId, reminders: rule.reminderMinutes.join(', '), overdue: rule.overdueMinutes.join(', '), isActive: rule.isActive }
    : { id: '', name: '', departmentCode: context?.departmentCode ?? '', fieldActivityCode: context?.fieldActivityCode ?? '', duration: 8, durationUnit: 'Hours',
        calendarId: '', reminders: '60', overdue: '0', isActive: true };
}

/** `null` when any entry is not a whole number. */
export function parseThresholds(text: string): number[] | null {
  const parts = (text ?? '').split(/[,،]/).map(x => x.trim()).filter(Boolean);
  if (parts.some(p => !/^-?\d+$/.test(p))) return null;
  return parts.map(Number);
}

export function validateSlaDraft(draft: SlaDraft, calendars: SlaCalendarOption[], activeRuleId?: string | null): SlaErrors {
  const errors: SlaErrors = {};
  const name = draft.name?.trim() ?? '';
  if (!name) errors.name = ['Enter a rule name.', 'أدخل اسم القاعدة.'];
  else if (name.length > SLA_LIMITS.nameMax) errors.name = ['Use at most 200 characters.', 'استخدم 200 حرف كحد أقصى.'];
  if (!draft.departmentCode) errors.departmentCode = ['Select a department.', 'اختر القسم.'];
  if (!draft.fieldActivityCode) errors.fieldActivityCode = ['Select a Field Activity Type.', 'اختر نوع النشاط الميداني.'];
  if (draft.duration == null || !Number.isInteger(draft.duration) || draft.duration < 1 || draft.duration > SLA_LIMITS.durationMax)
    errors.duration = ['Enter a whole number from 1 to 87,600.', 'أدخل عدداً صحيحاً من 1 إلى 87,600.'];
  if (!(SLA_UNITS as readonly string[]).includes(draft.durationUnit)) errors.durationUnit = ['Select a supported unit.', 'اختر وحدة مدعومة.'];
  const calendar = calendars.find(c => c.id === draft.calendarId);
  if (!draft.calendarId) errors.calendarId = ['Select a calendar.', 'اختر التقويم.'];
  else if (!calendar) errors.calendarId = ['This calendar is inactive or unavailable. Select an active calendar.', 'هذا التقويم غير نشط أو غير متاح. اختر تقويماً نشطاً.'];
  else if (BUSINESS_UNITS.has(draft.durationUnit) && !calendar.hasWorkingPeriods)
    errors.calendarId = ['Business time needs working periods. Add them to this calendar or choose another.', 'وقت العمل يتطلب فترات عمل. أضفها لهذا التقويم أو اختر تقويماً آخر.'];
  const reminders = parseThresholds(draft.reminders);
  if (!reminders || reminders.length > SLA_LIMITS.thresholdsMax || reminders.some(x => x <= 0 || x > SLA_LIMITS.minutesMax))
    errors.reminders = ['Use up to 10 whole minutes, each from 1 to 525,600.', 'استخدم حتى 10 قيم بالدقائق، كل منها من 1 إلى 525,600.'];
  const overdue = parseThresholds(draft.overdue);
  if (!overdue || overdue.length > SLA_LIMITS.thresholdsMax || overdue.some(x => x < 0 || x > SLA_LIMITS.minutesMax))
    errors.overdue = ['Use up to 10 whole minutes, each from 0 to 525,600.', 'استخدم حتى 10 قيم بالدقائق، كل منها من 0 إلى 525,600.'];
  if (draft.isActive && activeRuleId && activeRuleId !== draft.id)
    errors.isActive = ['Another active rule already covers this Department and FA Type. Edit that rule, or save this one as inactive.', 'توجد قاعدة نشطة أخرى لهذا القسم ونوع النشاط. عدّل تلك القاعدة أو احفظ هذه كغير نشطة.'];
  return errors;
}

export function toRule(draft: SlaDraft): SlaRule {
  return { id: draft.id, name: draft.name.trim(), departmentCode: draft.departmentCode, fieldActivityCode: draft.fieldActivityCode, duration: draft.duration ?? 0,
    durationUnit: draft.durationUnit, calendarId: draft.calendarId, reminderMinutes: parseThresholds(draft.reminders) ?? [], overdueMinutes: parseThresholds(draft.overdue) ?? [], isActive: draft.isActive };
}

export function sameDraft(a: SlaDraft, b: SlaDraft): boolean { return JSON.stringify(a) === JSON.stringify(b); }

export function formatMinutes(values: number[], t: (en: string, ar: string) => string): string {
  return values.length ? values.join(', ') + ' ' + t('min', 'دقيقة') : '—';
}

/** Server text wins (it names the conflict); status-specific fallbacks cover permission and connectivity failures. */
export function slaErrorMessage(error: unknown, t: (en: string, ar: string) => string): string {
  const e = error instanceof HttpErrorResponse ? error : null;
  if (e?.status === 403) return t('You do not have permission to make this change.', 'ليست لديك صلاحية لإجراء هذا التغيير.');
  if (e?.status === 0) return t('Could not reach the server. Check your connection and try again.', 'تعذر الوصول إلى الخادم. تحقق من الاتصال وحاول مرة أخرى.');
  const body = (e ? e.error : error) as { message?: string; Message?: string; detail?: string; title?: string; errors?: Record<string, string[]> } | null;
  const fieldErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : '';
  return body?.message ?? body?.Message ?? body?.detail ?? (fieldErrors || body?.title) ?? t('The change could not be saved. Try again.', 'تعذر حفظ التغيير. حاول مرة أخرى.');
}
