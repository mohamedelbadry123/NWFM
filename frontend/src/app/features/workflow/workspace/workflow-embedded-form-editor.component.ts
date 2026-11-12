import {
  Component,
  ElementRef,
  Injector,
  Input,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { A11yModule } from '@angular/cdk/a11y';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { apiErrorMessage } from '@core/api/api-error-message';
import { FormsService } from '@core/form-engine/forms.service';
import { FORM_CATEGORIES, FormDetail } from '@core/form-engine/form-engine.models';
import { TaskTypesService } from '@core/tasks/task-types.service';
import { FormBuilderComponent } from '../../form-engine/builder/form-builder.component';
import { ActivityForm } from './workflow-activity-forms.service';
import { ActivityClassification, PinnedForm, isClassified } from './workflow-activity-form-pin';
import { ReferenceItem, WorkflowWorkspaceService } from './workflow-workspace.service';

/** What the Step's form picker asks the window to do, and how the window reports back. */
export interface FormEditorContext {
  mode: 'create' | 'edit' | 'duplicate';
  taskTypeId: string;
  stepName: string;
  /** The form to edit, or the one to copy. */
  form?: ActivityForm;
  /**
   * The Department and Field Activity Type the Step belongs to: its own, or the parent Task Type Step's.
   * A form made for the Step carries it; when it is missing the window asks for one.
   */
  classification?: ActivityClassification;
  /** The published form version to use in the Step this window was opened from. */
  use: (form: PinnedForm) => void;
  /** Closes the window; the host asks {@link requestClose} first. */
  close: () => void;
  /** Set by the editor: asks whether it may close. Resolves false to keep editing. */
  requestClose?: () => Promise<boolean>;
  /** Set by the editor: whether the design has changes that were not saved. */
  isDirty?: () => boolean;
  /** Set by the editor: whether it is publishing/attaching, which must finish before the window can be left. */
  isBusy?: () => boolean;
  /** Set by the editor: saves the design as a draft; resolves false when it could not be saved. */
  saveDraft?: () => Promise<boolean>;
}

const CATEGORY_NAMES: Record<string, [string, string]> = {
  GENERAL: ['General', 'عام'],
  INSPECTION: ['Inspection', 'فحص'],
  SURVEY: ['Survey', 'مسح'],
  REQUEST: ['Request', 'طلب'],
  CHECKLIST: ['Checklist', 'قائمة تحقق'],
  OTHER: ['Other', 'أخرى'],
};
const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.-]*$/;
const norm = (value?: string | null) => (value ?? '').trim().toLowerCase();
const sameClassification = (
  form: Pick<FormDetail, 'departmentCode' | 'fieldActivityCode'>,
  wanted: ActivityClassification,
) =>
  norm(form.departmentCode) === norm(wanted.departmentCode) &&
  norm(form.fieldActivityCode) === norm(wanted.fieldActivityCode);
/** The update request that keeps a form's names and category and sets its classification. */
const details = (
  form: Pick<FormDetail, 'nameEn' | 'nameAr' | 'category'>,
  wanted: ActivityClassification,
) => ({
  nameEn: form.nameEn,
  nameAr: form.nameAr,
  category: form.category,
  departmentCode: wanted.departmentCode ?? '',
  fieldActivityCode: wanted.fieldActivityCode ?? '',
});

/**
 * The form designer inside a window opened from a Step. New and copied forms start with a short details
 * form (their identity comes back from the server before any design work); every mode then edits the form
 * with the shared builder. Save draft keeps the work private; Publish and use freezes a new version, files
 * the form under the Step's Task Type and pins that version in this Step only.
 */
@Component({
  selector: 'app-workflow-embedded-form-editor',
  standalone: true,
  imports: [FormsModule, A11yModule, NgTemplateOutlet, FormBuilderComponent],
  template: `
    <ng-template #classificationPicker>
      <label class="grid gap-1">
        {{ t('Department and Field Activity Type', 'القسم ونوع النشاط الميداني') }} *
        <select
          class="wf-input"
          name="activityType"
          id="form-activity-type"
          [ngModel]="activityTypeId()"
          [ngModelOptions]="{ standalone: true }"
          (ngModelChange)="activityTypeId.set($event)"
          [disabled]="busy() || activityTypesLoading()"
          [attr.aria-invalid]="(submitted() || pickerAttempted()) && !activityTypeId()"
        >
          <option value="">{{ t('Select a type', 'اختر النوع') }}</option>
          @for (item of activityTypes(); track item.id) {
            <option [value]="item.id">{{ activityTypeLabel(item) }}</option>
          }
        </select>
        <span class="text-xs opacity-70">{{
          t(
            'This Step has no classification yet. The form carries it, and the workflow cannot be published without it.',
            'ليس لهذه الخطوة تصنيف بعد. يحمله النموذج، ولا يمكن نشر سير العمل بدونه.'
          )
        }}</span>
      </label>
      @if (activityTypesError()) {
        <p role="alert" class="text-red-600">{{ activityTypesError() }}</p>
      }
    </ng-template>
    <div class="relative h-full">
      <div
        class="flex h-full min-h-0 flex-col"
        [attr.inert]="closePrompt() ? '' : null"
        [attr.aria-hidden]="closePrompt() ? 'true' : null"
      >
        <div class="flex flex-wrap items-center justify-between gap-2 border-b px-4 py-2">
          <div class="min-w-0">
            <h2 class="text-base font-semibold" tabindex="-1" #heading>{{ title() }}</h2>
            <p class="text-xs opacity-70">{{ t('For Step', 'للخطوة') }}: {{ context.stepName }}</p>
          </div>
          @if (stage() === 'design') {
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="busy() || !dirty()"
                (click)="saveDraft()"
              >
                {{ t('Save draft', 'حفظ كمسودة') }}
              </button>
              <button
                type="button"
                class="wf-btn-primary"
                [disabled]="busy()"
                [attr.aria-busy]="busy()"
                (click)="publishAndUse()"
              >
                {{ t('Publish and use in this Step', 'نشر واستخدامه في هذه الخطوة') }}
              </button>
            </div>
          }
        </div>
        @if (context.mode === 'edit' && stage() === 'design') {
          <p class="border-b px-4 py-2 text-sm" role="note">
            {{
              t(
                'This form may be shared by other Steps. Publishing creates a new version; every other Step keeps the version it uses until you change it there.',
                'قد يستخدم هذا النموذج خطوات أخرى. النشر ينشئ إصداراً جديداً، وتحتفظ كل خطوة أخرى بالإصدار الذي تستخدمه حتى تغيّره فيها.'
              )
            }}
          </p>
        }
        @if (error()) {
          <p role="alert" class="border-b px-4 py-2 text-red-600">{{ error() }}</p>
        }
        <div class="min-h-0 flex-1 overflow-auto">
          @if (stage() === 'details') {
            <form
              class="mx-auto grid max-w-xl gap-4 p-4"
              novalidate
              (submit)="$event.preventDefault(); createForm()"
            >
              <p class="text-sm opacity-80">
                {{
                  context.mode === 'duplicate'
                    ? t(
                        'Make an independent copy of this form. Changes to the copy never affect the original or the Steps that use it.',
                        'أنشئ نسخة مستقلة من هذا النموذج. لا تؤثر تغييرات النسخة على الأصل ولا على الخطوات التي تستخدمه.'
                      )
                    : t(
                        'Name the new form. You design its fields next.',
                        'سمِّ النموذج الجديد. ستصمم حقوله في الخطوة التالية.'
                      )
                }}
              </p>
              <label class="grid gap-1">
                {{ t('Name (English)', 'الاسم (بالإنجليزية)') }} *
                <input
                  class="wf-input"
                  name="nameEn"
                  maxlength="250"
                  required
                  [ngModel]="nameEn()"
                  (ngModelChange)="changeNameEn($event)"
                  [attr.aria-invalid]="submitted() && !nameEn().trim()"
                />
              </label>
              <label class="grid gap-1">
                {{ t('Name (Arabic)', 'الاسم (بالعربية)') }} *
                <input
                  class="wf-input"
                  dir="rtl"
                  name="nameAr"
                  maxlength="250"
                  required
                  [ngModel]="nameAr()"
                  (ngModelChange)="nameAr.set($event)"
                  [attr.aria-invalid]="submitted() && !nameAr().trim()"
                />
              </label>
              <label class="grid gap-1">
                {{ t('Code', 'الرمز') }} *
                <input
                  class="wf-input"
                  dir="ltr"
                  name="code"
                  maxlength="50"
                  required
                  autocomplete="off"
                  [ngModel]="code()"
                  (ngModelChange)="changeCode($event)"
                  [attr.aria-invalid]="submitted() && !codeValid()"
                />
                <span class="text-xs opacity-70">{{
                  t(
                    'Letters, digits, dash, underscore and dot. It cannot be changed later.',
                    'أحرف وأرقام وشرطة وشرطة سفلية ونقطة. لا يمكن تغييره لاحقاً.'
                  )
                }}</span>
              </label>
              @if (context.mode === 'create') {
                <label class="grid gap-1">
                  {{ t('Category', 'التصنيف') }}
                  <select
                    class="wf-input"
                    name="category"
                    [ngModel]="category()"
                    (ngModelChange)="category.set($event)"
                  >
                    @for (item of categories; track item) {
                      <option [value]="item">{{ categoryName(item) }}</option>
                    }
                  </select>
                </label>
              }
              @if (needsPicker()) {
                <ng-container [ngTemplateOutlet]="classificationPicker" />
              } @else if (inheritedClassification()) {
                <p class="text-xs opacity-70" role="note">
                  {{
                    t(
                      'The form is classified like this Step (Department and Field Activity Type).',
                      'يُصنَّف النموذج مثل هذه الخطوة (القسم ونوع النشاط الميداني).'
                    )
                  }}
                </p>
              }
              @if (submitted() && !detailsValid()) {
                <p role="alert" class="text-red-600">
                  {{
                    needsPicker() && !activityTypeId()
                      ? t(
                          'Enter both names, a valid code and the Field Activity Type.',
                          'أدخل الاسمين ورمزاً صالحاً ونوع النشاط الميداني.'
                        )
                      : t('Enter both names and a valid code.', 'أدخل الاسمين ورمزاً صالحاً.')
                  }}
                </p>
              }
              <div class="flex flex-wrap gap-2">
                <button
                  type="submit"
                  class="wf-btn-primary"
                  [disabled]="busy()"
                  [attr.aria-busy]="busy()"
                >
                  {{
                    context.mode === 'duplicate'
                      ? t('Create copy and design', 'إنشاء النسخة والتصميم')
                      : t('Create form and design', 'إنشاء النموذج والتصميم')
                  }}
                </button>
                <button
                  type="button"
                  class="wf-btn-secondary"
                  [disabled]="busy()"
                  (click)="context.close()"
                >
                  {{ t('Cancel', 'إلغاء') }}
                </button>
              </div>
            </form>
          } @else if (formId()) {
            @if (needsPicker()) {
              <div class="grid max-w-xl gap-2 border-b p-4">
                <ng-container [ngTemplateOutlet]="classificationPicker" />
              </div>
            }
            <!-- Inert for the whole of a save or publish, so a change made meanwhile cannot be marked saved. -->
            <div [attr.inert]="busy() ? '' : null" [attr.aria-busy]="busy()">
              <app-form-builder [embeddedFormId]="formId()" (dirtyChange)="dirty.set($event)" />
            </div>
          }
        </div>
      </div>
      @if (closePrompt()) {
        <div class="absolute inset-0 z-10 flex items-center justify-center bg-black/40 p-4">
          <div
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="form-close-title"
            aria-describedby="form-close-body"
            class="w-full max-w-md space-y-3 rounded-xl border bg-white p-5 dark:bg-dark-800"
            cdkTrapFocus
            [cdkTrapFocusAutoCapture]="true"
            (keydown.escape)="onPromptEscape($event)"
          >
            <h3 id="form-close-title" class="font-semibold">
              {{ t('Save your changes to this form?', 'هل تريد حفظ تغييراتك على هذا النموذج؟') }}
            </h3>
            <p id="form-close-body" class="text-sm">
              {{
                t(
                  'Your changes have not been saved. Saving keeps them as a draft; the form is not used in the Step until you publish it.',
                  'لم تُحفظ تغييراتك. الحفظ يبقيها كمسودة، ولا يُستخدم النموذج في الخطوة قبل نشره.'
                )
              }}
            </p>
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                class="wf-btn-primary"
                #promptFirst
                cdkFocusInitial
                [disabled]="busy()"
                (click)="decide('save')"
              >
                {{ t('Save draft', 'حفظ كمسودة') }}
              </button>
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="busy()"
                (click)="decide('discard')"
              >
                {{ t('Discard changes', 'تجاهل التغييرات') }}
              </button>
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="busy()"
                (click)="decide('keep')"
              >
                {{ t('Keep editing', 'متابعة التحرير') }}
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [':host { display: block; height: 100%; position: relative; }'],
})
export class WorkflowEmbeddedFormEditorComponent implements OnInit {
  @Input({ required: true }) context!: FormEditorContext;
  @ViewChild(FormBuilderComponent) private builder?: FormBuilderComponent;
  @ViewChild('heading') private heading?: ElementRef<HTMLElement>;
  @ViewChild('promptFirst') private promptFirst?: ElementRef<HTMLElement>;
  private readonly locale = inject(LocaleService);
  private readonly translate = inject(TranslateService);
  private readonly formsApi = inject(FormsService);
  private readonly taskTypes = inject(TaskTypesService);
  readonly categories: string[] = [...FORM_CATEGORIES];
  readonly stage = signal<'details' | 'design'>('design');
  readonly formId = signal<string>('');
  readonly busy = signal(false);
  readonly dirty = signal(false);
  readonly error = signal('');
  readonly closePrompt = signal(false);
  readonly submitted = signal(false);
  readonly nameEn = signal('');
  readonly nameAr = signal('');
  readonly code = signal('');
  readonly category = signal('GENERAL');
  readonly codeValid = computed(() => CODE_PATTERN.test(this.code()) && this.code().length <= 50);
  /** The Field Activity Type chosen here when neither the Step, its parent nor the form already has one. */
  readonly activityTypeId = signal('');
  readonly activityTypes = signal<ReferenceItem[]>([]);
  readonly activityTypesLoading = signal(false);
  readonly activityTypesError = signal('');
  readonly pickerAttempted = signal(false);
  private departments: ReferenceItem[] = [];
  /** What the server last said about the form being designed (names, classification). */
  private detail?: Pick<
    FormDetail,
    'nameEn' | 'nameAr' | 'category' | 'departmentCode' | 'fieldActivityCode'
  >;
  /** The form made by this window; a retry after a failed classification does not create it twice. */
  private created?: FormDetail;
  private codeEdited = false;
  private decision?: (keep: boolean) => void;
  private returnFocus: HTMLElement | null = null;
  private readonly injector = inject(Injector);

  /** The Step's (or its parent's) classification, when it has a complete one. */
  inheritedClassification(): ActivityClassification | undefined {
    return isClassified(this.context.classification) ? this.context.classification : undefined;
  }
  /**
   * Whether the author must choose a classification here: the Step has none, and the form it would
   * carry has none either. A form that already has one gives its classification to the Step when it is used.
   */
  needsPicker(): boolean {
    if (this.inheritedClassification()) return false;
    return !this.formIsClassified();
  }
  /** Whether the form being designed already carries its own classification (independent of any the Step inherits). */
  private formIsClassified(): boolean {
    const source =
      this.created ??
      this.detail ??
      (this.context.mode === 'create' ? undefined : this.context.form);
    return isClassified(source);
  }
  /** What the form must carry: the Step's classification, else the one chosen here, else what it already has. */
  private wantedClassification(): ActivityClassification | undefined {
    const inherited = this.inheritedClassification();
    if (inherited) return inherited;
    const picked = this.activityTypes().find((item) => item.id === this.activityTypeId());
    return picked?.parentCode
      ? { departmentCode: picked.parentCode, fieldActivityCode: picked.code }
      : undefined;
  }
  detailsValid(): boolean {
    return (
      !!this.nameEn().trim() &&
      !!this.nameAr().trim() &&
      this.codeValid() &&
      (!this.needsPicker() || !!this.wantedClassification())
    );
  }
  activityTypeLabel(item: ReferenceItem) {
    const department = this.departments.find((d) => d.code === item.parentCode);
    return (
      this.t(item.nameEn, item.nameAr || item.nameEn) +
      (department ? ' · ' + this.t(department.nameEn, department.nameAr || department.nameEn) : '')
    );
  }
  private loadActivityTypes() {
    if (this.activityTypes().length || this.activityTypesLoading()) return;
    const api = this.injector.get(WorkflowWorkspaceService);
    this.activityTypesLoading.set(true);
    this.activityTypesError.set('');
    Promise.all([
      firstValueFrom(api.references('field-activity-types')),
      firstValueFrom(api.references('departments')),
    ])
      .then(([types, departments]) => {
        this.activityTypes.set(types);
        this.departments = departments;
      })
      .catch(() =>
        this.activityTypesError.set(
          this.t('Could not load the Field Activity Types.', 'تعذر تحميل أنواع النشاط الميداني.'),
        ),
      )
      .finally(() => this.activityTypesLoading.set(false));
  }

  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  categoryName(category: string) {
    const names = CATEGORY_NAMES[category];
    return names ? this.t(...names) : category;
  }
  title() {
    return this.context.mode === 'edit'
      ? this.t('Edit shared form', 'تعديل النموذج المشترك')
      : this.context.mode === 'duplicate'
        ? this.t('Duplicate form for this Step', 'تكرار النموذج لهذه الخطوة')
        : this.t('Create form for this Step', 'إنشاء نموذج لهذه الخطوة');
  }

  ngOnInit() {
    this.context.requestClose = () => this.requestClose();
    this.context.isDirty = () => this.isDirty();
    this.context.isBusy = () => this.busy();
    this.context.saveDraft = () => this.saveDraft();
    const form = this.context.form;
    if (this.context.mode === 'edit' && form) {
      this.formId.set(form.id);
      this.detail = form;
      if (this.needsPicker()) this.loadActivityTypes();
      return;
    }
    this.stage.set('details');
    if (this.context.mode === 'duplicate' && form) {
      this.nameEn.set(form.nameEn + ' (copy)');
      this.nameAr.set(form.nameAr + ' (نسخة)');
      this.code.set(this.suggestCode(form.code + '-COPY'));
      this.codeEdited = true;
    }
    if (this.needsPicker()) this.loadActivityTypes();
  }

  changeNameEn(value: string) {
    this.nameEn.set(value);
    if (!this.codeEdited) this.code.set(this.suggestCode(value));
  }
  changeCode(value: string) {
    this.codeEdited = true;
    this.code.set(value);
  }

  /** Whether the design differs from what was last saved; the builder's own revision is the truth, not the last event it sent. */
  isDirty(): boolean {
    return this.stage() === 'design' && (this.dirty() || !!this.builder?.dirty());
  }

  /**
   * Creates (or copies) the form on the server; its identity comes back before any design starts. The form carries
   * the Step's classification, so using it makes the Step publishable. A retry after a failed classification
   * continues with the form that was already made instead of creating a second one.
   */
  async createForm() {
    this.submitted.set(true);
    if (!this.detailsValid() || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const wanted = this.wantedClassification();
      let form = this.created;
      if (!form) {
        const source = this.context.form;
        const result =
          this.context.mode === 'duplicate' && source
            ? await firstValueFrom(
                this.formsApi.clone(source.id, {
                  newCode: this.code().trim(),
                  newNameEn: this.nameEn().trim(),
                  newNameAr: this.nameAr().trim(),
                }),
              )
            : await firstValueFrom(
                this.formsApi.create({
                  code: this.code().trim(),
                  nameEn: this.nameEn().trim(),
                  nameAr: this.nameAr().trim(),
                  category: this.category(),
                  ...(wanted ?? {}),
                }),
              );
        if (!result.value) throw result;
        form = this.created = result.value;
      }
      // A copy starts with its source's classification; it belongs to this Step, so it takes the Step's.
      if (this.context.mode === 'duplicate' && wanted && !sameClassification(form, wanted)) {
        const updated = await firstValueFrom(this.formsApi.update(form.id, details(form, wanted)));
        if (!updated.value) throw updated;
        form = this.created = updated.value;
      }
      this.detail = form;
      this.formId.set(form.id);
      this.stage.set('design');
    } catch (error) {
      this.error.set(apiErrorMessage(error, this.translate, 'formBuilder.saveError'));
    } finally {
      this.busy.set(false);
    }
  }

  async saveDraft(): Promise<boolean> {
    if (this.busy() || !this.builder) return false;
    this.error.set('');
    this.busy.set(true);
    try {
      return await this.builder.saveDraft();
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Publishes the design (unless it is already the published version), files the form under the Task Type and
   * hands the new version to the Step. The builder is inert for the whole operation, and the window closes only
   * once it is no longer busy and the design on screen is what was published; edits made meanwhile keep it open.
   * Any failure leaves the window open with the design intact; a retry after a failed filing does not publish again.
   */
  async publishAndUse() {
    const builder = this.builder;
    const formId = this.formId();
    if (!builder || !formId || this.busy()) return;
    this.error.set('');
    this.pickerAttempted.set(true);
    let finished = false;
    this.busy.set(true);
    try {
      if (!(await this.classify(formId))) return;
      let versionNo = builder.currentVersionNo();
      if (builder.dirty() || builder.formStatus() !== 'PUBLISHED' || !versionNo) {
        const published = await builder.publishForm();
        if (!published?.currentVersionNo) return;
        versionNo = published.currentVersionNo;
        this.detail = published;
      }
      try {
        await firstValueFrom(this.taskTypes.attachForm(this.context.taskTypeId, formId));
      } catch (error) {
        this.error.set(apiErrorMessage(error, this.translate, 'common.error'));
        return;
      }
      const detail = this.detail;
      this.context.use({
        formId,
        versionNo,
        name:
          (this.locale.locale() === 'ar' ? detail?.nameAr : detail?.nameEn) || detail?.nameEn || '',
        departmentCode: detail?.departmentCode ?? undefined,
        fieldActivityCode: detail?.fieldActivityCode ?? undefined,
      });
      // The window's own record of unsaved work follows the builder's actual revision, never a blind reset.
      this.dirty.set(builder.dirty());
      finished = !this.dirty();
      if (!finished)
        this.error.set(
          this.t(
            'The version was used in the Step, but you changed the form meanwhile. Those changes are not published.',
            'استُخدم الإصدار في الخطوة، لكنك غيّرت النموذج في الأثناء. هذه التغييرات غير منشورة.',
          ),
        );
    } finally {
      this.busy.set(false);
    }
    // The host refuses to close a busy window, so closing waits until it is idle again.
    if (finished) this.context.close();
  }

  /** Gives a form without a classification the one chosen here, before it is published; false stops the publish. */
  private async classify(formId: string): Promise<boolean> {
    // A shared form without a classification takes the Step's inherited one even though no picker is shown for it.
    if (this.formIsClassified()) return true;
    const wanted = this.wantedClassification();
    const detail = this.detail;
    if (!wanted || !detail) {
      this.error.set(
        this.t(
          'Choose the Department and Field Activity Type for this form first.',
          'اختر القسم ونوع النشاط الميداني لهذا النموذج أولاً.',
        ),
      );
      return false;
    }
    try {
      const updated = await firstValueFrom(this.formsApi.update(formId, details(detail, wanted)));
      if (!updated.value) throw updated;
      this.detail = updated.value;
      return true;
    } catch (error) {
      this.error.set(apiErrorMessage(error, this.translate, 'formBuilder.saveError'));
      return false;
    }
  }

  /** Asked by the host before closing the window. Unsaved design changes offer Save draft, Discard or Keep editing. */
  requestClose(): Promise<boolean> {
    if (this.busy()) return Promise.resolve(false);
    if (!this.isDirty()) return Promise.resolve(true);
    this.decision?.(false);
    this.returnFocus ??=
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.closePrompt.set(true);
    setTimeout(() => this.promptFirst?.nativeElement.focus());
    return new Promise((resolve) => (this.decision = resolve));
  }

  async decide(choice: 'save' | 'discard' | 'keep') {
    const resolve = this.decision;
    if (!resolve) return;
    let result = choice !== 'keep';
    // A failed save keeps the window open with the design intact; the builder reports why.
    if (choice === 'save' && !(await this.saveDraft())) result = false;
    this.closePrompt.set(false);
    this.decision = undefined;
    if (!result) this.restoreFocus();
    else this.returnFocus = null;
    resolve(result);
  }

  /** Escape answers only the question: it means Keep editing and must not reach the windows behind it. */
  onPromptEscape(event: Event) {
    event.preventDefault();
    event.stopPropagation();
    if (!this.busy()) void this.decide('keep');
  }

  /** After Keep editing (or a failed save) focus goes back to what had it, or to the window's heading. */
  private restoreFocus() {
    const target = this.returnFocus;
    this.returnFocus = null;
    setTimeout(() => {
      if (target?.isConnected) target.focus();
      else this.heading?.nativeElement.focus();
    });
  }
  private suggestCode(source: string) {
    const code = source
      .normalize('NFKD')
      .replace(/[^A-Za-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .toUpperCase()
      .slice(0, 50)
      .replace(/-+$/, '');
    return code;
  }
}
