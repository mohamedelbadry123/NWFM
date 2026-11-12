import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  inject,
  signal,
} from '@angular/core';
import { forkJoin } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupItem, LookupsService } from '@core/lookups/lookups.service';
import { WorkflowOrganizationScope, WorkflowScopeRow } from './workflow-workspace.service';
import { WorkflowScopeLookups } from './workflow-scope-selection';
import {
  EMPTY_SCOPE_DRAFT,
  SCOPE_DRAFT_LEVELS,
  ScopeDraftLevel,
  WorkflowScopeDraft,
  changeDraft,
  coveringRow,
  draftFromRow,
  draftOptions,
  draftProblem,
  effectiveScopeRows,
  expandScopeRows,
  isDraftStarted,
  rowFromDraft,
  scopeRowKey,
} from './workflow-scope-rows';

export interface WorkflowScopeChange {
  scopes: WorkflowOrganizationScope[];
  rows: WorkflowScopeRow[];
}

let nextEditorId = 0;

/**
 * Organization scope as a list of entries. Each entry picks one Cluster (required), then optionally one CBU
 * under it and one Branch and one Operation Area under that CBU, and is added to the list. The saved scope is
 * still the union of everything the entries cover; the entries are only how it was put together.
 */
@Component({
  selector: 'app-workflow-scope-editor',
  standalone: true,
  template: `
    <fieldset class="space-y-3 min-w-0">
      <legend class="font-medium mb-2">{{ t('Organization scope', 'النطاق التنظيمي') }}</legend>
      @if (!readonly) {
        <div
          role="group"
          class="space-y-3"
          [attr.aria-label]="
            editingId()
              ? t('Edit scope entry', 'تعديل إدخال النطاق')
              : t('Add scope entry', 'إضافة إدخال نطاق')
          "
          (keydown.escape)="escape($event)"
        >
          @if (editingId()) {
            <p class="text-sm font-medium">{{ t('Editing an entry', 'تعديل إدخال') }}</p>
          }
          <div class="grid gap-3 grid-cols-1 sm:grid-cols-2 xl:grid-cols-4">
            @for (level of levels; track level.key) {
              <div class="flex flex-col gap-1 min-w-0">
                <label [for]="id + level.key"
                  >{{ t(level.en, level.ar) }}
                  @if (level.key === 'clusterCode') {
                    <span aria-hidden="true"> *</span>
                  }
                </label>
                <select
                  class="wf-input w-full"
                  [id]="id + level.key"
                  [attr.data-level]="level.key"
                  [attr.aria-required]="level.key === 'clusterCode'"
                  [attr.aria-invalid]="
                    level.key === 'clusterCode' && attempted() && !draft().clusterCode
                  "
                  [disabled]="loading() || !!error() || unavailable(level.key)"
                  (change)="pick(level.key, $any($event.target).value)"
                >
                  <option value="" [selected]="!draft()[level.key]">
                    {{ placeholder(level.key) }}
                  </option>
                  @for (item of options(level.key); track item.code) {
                    <option [value]="item.code" [selected]="item.code === draft()[level.key]">
                      {{ optionLabel(item) }}
                    </option>
                  }
                </select>
              </div>
            }
          </div>
          <div class="flex flex-wrap items-center gap-2">
            <button
              type="button"
              class="wf-btn-primary"
              [disabled]="loading() || !!error()"
              (click)="commit()"
            >
              {{ editingId() ? t('Save changes', 'حفظ التغييرات') : t('Add', 'إضافة') }}
            </button>
            @if (editingId() || started()) {
              <button type="button" class="wf-btn-secondary" (click)="cancel()">
                {{ t('Cancel', 'إلغاء') }}
              </button>
            }
          </div>
          @if (problem()) {
            <p role="alert" class="text-red-600">{{ problem() }}</p>
          } @else if (pending()) {
            <p role="status" class="text-sm text-amber-700">
              {{
                t(
                  'Add this entry or cancel it before continuing.',
                  'أضف هذا الإدخال أو ألغه قبل المتابعة.'
                )
              }}
            </p>
          }
        </div>
      }
      <p class="text-xs opacity-70">
        {{
          t(
            'Choose one Cluster per entry. CBU, Branch and Operation Area are optional; Branch and Operation Area both belong directly to the CBU. Add several entries to cover more than one place.',
            'اختر قطاعاً واحداً لكل إدخال. وحدة الأعمال والفرع ومنطقة العمليات اختيارية؛ ويتبع الفرع ومنطقة العمليات وحدة الأعمال مباشرة. أضف عدة إدخالات لتغطية أكثر من موقع.'
          )
        }}
      </p>
      @if (error()) {
        <p role="alert" class="text-red-600">{{ error() }}</p>
        <button type="button" class="wf-btn-secondary" (click)="load()">
          {{ t('Retry', 'إعادة المحاولة') }}
        </button>
      }
      @if (missing()) {
        <p role="alert" class="text-red-600">
          {{
            t(
              'Some saved units are no longer available. Review the scope before publishing.',
              'بعض الوحدات المحفوظة لم تعد متاحة. راجع النطاق قبل النشر.'
            )
          }}
        </p>
      }
      @if (rows().length) {
        <ul
          class="flex flex-wrap gap-2 text-sm list-none p-0 m-0"
          [id]="id + 'list'"
          [attr.aria-label]="t('Scope entries', 'إدخالات النطاق')"
        >
          @for (row of visible(); track row.id) {
            <li
              class="rounded border px-3 py-1 flex flex-wrap items-center gap-x-3 gap-y-1 max-w-full min-w-0"
              [class.border-primary]="row.id === editingId()"
              [attr.data-scope-entry]="row.id"
            >
              <span class="min-w-0" style="overflow-wrap: anywhere">{{ label(row) }}</span>
              @if (covered(row); as outer) {
                <span class="text-xs opacity-70"
                  >{{ t('Already covered by', 'مشمول ضمن') }} {{ label(outer) }}</span
                >
              }
              @if (!readonly) {
                <span class="flex gap-2">
                  <button
                    type="button"
                    class="underline"
                    [disabled]="!!editingId() || pending()"
                    [attr.aria-label]="t('Edit', 'تعديل') + ': ' + label(row)"
                    (click)="edit(row)"
                  >
                    {{ t('Edit', 'تعديل') }}
                  </button>
                  <button
                    type="button"
                    class="underline"
                    [disabled]="!!editingId() || pending()"
                    [attr.aria-label]="t('Remove', 'إزالة') + ': ' + label(row)"
                    (click)="remove(row)"
                  >
                    {{ t('Remove', 'إزالة') }}
                  </button>
                </span>
              }
            </li>
          }
        </ul>
        @if (rows().length > compact) {
          <button
            type="button"
            class="underline text-sm"
            [attr.aria-expanded]="expanded()"
            [attr.aria-controls]="id + 'list'"
            (click)="expanded.set(!expanded())"
          >
            {{
              expanded()
                ? t('Show fewer', 'عرض أقل')
                : t(
                    '+' + (rows().length - compact) + ' more',
                    'و' + (rows().length - compact) + ' أخرى'
                  )
            }}
          </button>
        }
      } @else if (readonly) {
        <p class="text-sm">{{ t('Not configured', 'غير محدد') }}</p>
      }
      <p class="sr-only" role="status" aria-live="polite">{{ announcement() }}</p>
    </fieldset>
  `,
})
export class WorkflowScopeEditorComponent implements OnInit, OnChanges {
  /** The canonical scopes: always the authority. */
  @Input() scopes: WorkflowOrganizationScope[] = [];
  /** The saved entries, when the version has them. Used only while they still expand to exactly {@link scopes}. */
  @Input() scopeRows?: WorkflowScopeRow[];
  @Input() readonly = false;
  @Output() scopeChange = new EventEmitter<WorkflowScopeChange>();
  /** True while an entry has been started or changed but not yet added or saved. */
  @Output() pendingChange = new EventEmitter<boolean>();
  private readonly api = inject(LookupsService);
  private readonly locale = inject(LocaleService);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  readonly id = 'workflow-scope-' + nextEditorId++ + '-';
  readonly compact = 2;
  readonly levels = SCOPE_DRAFT_LEVELS;
  readonly loading = signal(true);
  readonly error = signal('');
  readonly rows = signal<WorkflowScopeRow[]>([]);
  readonly draft = signal<WorkflowScopeDraft>({ ...EMPTY_SCOPE_DRAFT });
  readonly editingId = signal<string | null>(null);
  readonly expanded = signal(false);
  readonly attempted = signal(false);
  readonly announcement = signal('');
  lookups: WorkflowScopeLookups = { clusters: [], cbus: [], branches: [], areas: [] };
  private lastPending = false;

  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  ngOnInit() {
    this.load();
  }
  ngOnChanges() {
    this.rows.set(effectiveScopeRows(this.scopes ?? [], this.scopeRows));
    const editing = this.editingId();
    if (editing && !this.rows().some((r) => r.id === editing)) this.reset();
    if (this.readonly) this.reset();
  }
  load() {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      clusters: this.api.listAll('Cluster', { isActive: true }),
      cbus: this.api.listAll('Cbu', { isActive: true }),
      branches: this.api.listAll('Branch', { isActive: true }),
      areas: this.api.listAll('OperationArea', { isActive: true }),
    }).subscribe({
      next: (rows) => {
        this.lookups = rows;
        this.loading.set(false);
      },
      error: () => {
        this.error.set(
          this.t('Could not load organization choices.', 'تعذر تحميل الخيارات التنظيمية.'),
        );
        this.loading.set(false);
      },
    });
  }

  visible() {
    return this.expanded() ? this.rows() : this.rows().slice(0, this.compact);
  }
  started() {
    return isDraftStarted(this.draft());
  }
  /** Anything typed into the entry form that has not been added, or an edit that differs from the saved entry. */
  pending() {
    const editing = this.editingId();
    if (!editing) return this.started();
    const row = this.rows().find((r) => r.id === editing);
    return !row || scopeRowKey(this.draft()) !== scopeRowKey(row);
  }
  problem() {
    if (!this.attempted()) return '';
    const found = draftProblem(this.rows(), this.draft(), this.editingId() ?? undefined);
    return found === 'cluster'
      ? this.t('Choose a Cluster for this entry.', 'اختر قطاعاً لهذا الإدخال.')
      : found === 'duplicate'
        ? this.t('This entry is already in the list.', 'هذا الإدخال موجود بالفعل في القائمة.')
        : '';
  }
  unavailable(level: ScopeDraftLevel) {
    const parent = SCOPE_DRAFT_LEVELS.find((l) => l.key === level)?.parent;
    return !!parent && !this.draft()[parent];
  }
  placeholder(level: ScopeDraftLevel) {
    return level === 'clusterCode'
      ? this.t('Select', 'اختر')
      : this.t('Any (optional)', 'أي (اختياري)');
  }
  options(level: ScopeDraftLevel): { code: string; label: string }[] {
    const items = draftOptions(this.lookups, this.draft(), level).map((item) => ({
      code: item.code,
      label: this.name(item),
    }));
    const current = this.draft()[level];
    // A saved unit that has since been retired stays selectable so editing never silently swaps it for another.
    return current && !items.some((i) => i.code === current) && !this.loading()
      ? [{ code: current, label: current + ' · ' + this.t('unavailable', 'غير متاح') }, ...items]
      : items;
  }
  optionLabel(item: { label: string }) {
    return item.label;
  }
  pick(level: ScopeDraftLevel, value: string) {
    this.draft.set(changeDraft(this.draft(), level, value));
    this.announcement.set('');
    this.syncPending();
  }
  commit() {
    if (this.readonly || this.loading()) return;
    this.attempted.set(true);
    const editing = this.editingId();
    if (draftProblem(this.rows(), this.draft(), editing ?? undefined)) return;
    const next = rowFromDraft(this.draft(), editing ?? crypto.randomUUID());
    const rows = editing
      ? this.rows().map((r) => (r.id === editing ? next : r))
      : [...this.rows(), next];
    this.announcement.set(
      editing
        ? this.t('Entry updated.', 'تم تحديث الإدخال.')
        : this.t('Entry added.', 'تمت إضافة الإدخال.'),
    );
    this.reset();
    this.publish(rows);
    this.focus('clusterCode');
  }
  edit(row: WorkflowScopeRow) {
    if (this.readonly || this.pending() || this.editingId()) return;
    this.editingId.set(row.id);
    this.draft.set(draftFromRow(row));
    this.attempted.set(false);
    this.announcement.set('');
    this.syncPending();
    this.focus('clusterCode');
  }
  remove(row: WorkflowScopeRow) {
    if (this.readonly || this.pending() || this.editingId()) return;
    this.announcement.set(this.t('Entry removed: ', 'تمت إزالة الإدخال: ') + this.label(row));
    this.publish(this.rows().filter((r) => r.id !== row.id));
    this.focus('clusterCode');
  }
  cancel() {
    this.reset();
    this.announcement.set(this.t('Entry cancelled.', 'تم إلغاء الإدخال.'));
    this.focus('clusterCode');
  }
  escape(event: Event) {
    if (!this.pending() && !this.editingId()) return;
    event.stopPropagation();
    this.cancel();
  }

  /** What an entry reads as: each chosen unit by name, Branch and Operation Area labelled as such. */
  label(row: WorkflowScopeRow) {
    const parts = [this.unit('clusters', row.clusterCode)];
    if (row.cbuCode) parts.push(this.unit('cbus', row.cbuCode));
    if (row.branchCode)
      parts.push(this.t('Branch', 'الفرع') + ': ' + this.unit('branches', row.branchCode));
    if (row.operationAreaCode)
      parts.push(
        this.t('Operation area', 'منطقة العمليات') +
          ': ' +
          this.unit('areas', row.operationAreaCode),
      );
    return parts.join(' / ');
  }
  covered(row: WorkflowScopeRow) {
    return coveringRow(this.rows(), row);
  }
  missing() {
    if (this.loading() || this.error()) return false;
    const has = (list: keyof WorkflowScopeLookups, code?: string) =>
      !code || this.lookups[list].some((i) => i.code === code);
    return this.rows().some(
      (r) =>
        !has('clusters', r.clusterCode) ||
        !has('cbus', r.cbuCode) ||
        !has('branches', r.branchCode) ||
        !has('areas', r.operationAreaCode),
    );
  }

  private publish(rows: WorkflowScopeRow[]) {
    this.rows.set(rows);
    this.scopeChange.emit({ scopes: expandScopeRows(rows), rows });
  }
  private reset() {
    this.draft.set({ ...EMPTY_SCOPE_DRAFT });
    this.editingId.set(null);
    this.attempted.set(false);
    this.syncPending();
  }
  private syncPending() {
    const pending = this.pending();
    if (pending !== this.lastPending) {
      this.lastPending = pending;
      this.pendingChange.emit(pending);
    }
  }
  private unit(list: keyof WorkflowScopeLookups, code: string) {
    const item = this.lookups[list].find((i) => i.code === code);
    return item ? this.name(item) : code;
  }
  private name(item: LookupItem) {
    return this.t(item.nameEn, item.nameAr || item.nameEn);
  }
  private focus(level: ScopeDraftLevel) {
    setTimeout(() =>
      this.host.nativeElement.querySelector<HTMLElement>(`[data-level="${level}"]`)?.focus(),
    );
  }
}
