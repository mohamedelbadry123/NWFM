import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MultiSelectModule } from 'primeng/multiselect';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocaleService } from '@core/i18n/locale.service';
import { ReferenceItem, WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';

@Component({
  selector: 'app-workflow-role-assignment',
  standalone: true,
  imports: [FormsModule, MultiSelectModule],
  template: `
    <div class="role-assignment">
      <label [for]="controlId">{{ t('Roles', 'الأدوار') }}</label>
      <p-multiselect
        [inputId]="controlId"
        [options]="roles()"
        optionLabel="nameEn"
        optionValue="id"
        [ngModel]="roleIds"
        [ngModelOptions]="{ standalone: true }"
        (ngModelChange)="choose($event)"
        [disabled]="readonly || loading() || !!error()"
        [loading]="loading()"
        [filter]="true"
        display="chip"
        [showClear]="true"
        [maxSelectedLabels]="3"
        [selectionLimit]="50"
        [showToggleAll]="false"
        [selectedItemsLabel]="t('{0} roles selected', 'تم تحديد {0} أدوار')"
        [placeholder]="t('Select one or more roles', 'اختر دوراً أو أكثر')"
        appendTo="body"
        styleClass="w-full"
      />
      <p>
        {{
          t(
            'Existing user-account roles. A user with any selected role can work on the task, subject to their permissions and organization scope.',
            'أدوار حسابات المستخدمين الحالية. يمكن لحامل أي دور محدد العمل على المهمة وفق صلاحياته ونطاقه التنظيمي.'
          )
        }}
      </p>
      @if (legacyGroup) {
        <p class="legacy-note">
          {{ t('Saved assignment group: ', 'مجموعة الإسناد المحفوظة: ') }}{{ legacyGroup }}.
          {{
            t(
              'Selecting roles replaces this legacy assignment.',
              'يؤدي اختيار الأدوار إلى استبدال الإسناد القديم.'
            )
          }}
        </p>
      }
      @if (!loading() && !error() && unavailable().length) {
        <p role="alert">
          {{
            t(
              'Some saved roles are no longer available. Review the selection before publishing.',
              'بعض الأدوار المحفوظة لم تعد متاحة. راجع الاختيار قبل النشر.'
            )
          }}
        </p>
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
        <button type="button" (click)="load()">{{ t('Retry', 'إعادة المحاولة') }}</button>
      }
      @if (!loading() && !error() && !roles().length) {
        <p role="status">
          {{ t('No account roles are available.', 'لا توجد أدوار حسابات متاحة.') }}
        </p>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .role-assignment {
        display: grid;
        gap: 10px;
      }
      label {
        font-size: 13px;
        font-weight: 600;
        color: var(--ds-text);
      }
      p {
        font-size: 12px;
        line-height: 1.6;
        color: var(--ds-text-3);
      }
      .legacy-note {
        padding: 10px;
        border: 1px solid var(--ds-border);
        border-radius: 8px;
      }
      button {
        color: var(--ds-primary);
        text-decoration: underline;
        justify-self: start;
      }
      [role='alert'] {
        color: var(--ds-warn);
      }
    `,
  ],
})
export class WorkflowRoleAssignmentComponent implements OnInit {
  private static nextId = 0;
  readonly controlId = 'workflow-assignment-roles-' + WorkflowRoleAssignmentComponent.nextId++;
  @Input() roleIds: string[] = [];
  @Input() readonly = false;
  @Input() legacyGroup = '';
  @Output() roleIdsChange = new EventEmitter<string[]>();
  private readonly api = inject(WorkflowWorkspaceService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  readonly roles = signal<ReferenceItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  ngOnInit() {
    this.load();
  }
  load() {
    this.loading.set(true);
    this.error.set('');
    this.api
      .references('roles')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roles) => {
          this.roles.set(roles);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(this.t('Could not load account roles.', 'تعذر تحميل أدوار الحسابات.'));
          this.loading.set(false);
        },
      });
  }
  unavailable() {
    return this.roleIds.filter((id) => !this.roles().some((role) => role.id === id));
  }
  choose(ids: string[] | null) {
    if (this.readonly || this.loading() || this.error()) return;
    this.roleIdsChange.emit([...new Set(ids || [])]);
  }
}
