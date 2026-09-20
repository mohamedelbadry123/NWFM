import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MenuModule } from 'primeng/menu';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';
import { MenuItem } from 'primeng/api';
import { HasPermissionDirective, PERMISSIONS } from '../../../core/auth/permissions';
import { LocaleService } from '../../../core/i18n/locale.service';
import { FormsService } from '../../../core/form-engine/forms.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';
import { FORM_CATEGORIES, FormListItem } from '../../../core/form-engine/form-engine.models';
import {
  FORM_STATUS_VALUES,
  FormStatusFilter,
  FormStatusFilterValue,
  canArchive,
  canDeprecate,
  canFill,
  canPublish,
  formStatusFilterLabelKey,
  formStatusLabelKey,
  formStatusSeverity,
} from '../form-status';
import { FormDetailsDialogComponent } from '../modals/form-details-dialog.component';
import { FormCloneDialogComponent } from '../modals/form-clone-dialog.component';
import { FormVersionsDialogComponent } from '../modals/form-versions-dialog.component';
import { FormPreviewDialogComponent } from '../builder/components/form-preview-dialog.component';
import type { SerializedForm } from '../builder/store/form-builder.store';

interface FilterOption {
  value: FormStatusFilterValue | string | null;
  labelKey: string;
}

/** The forms grid: design, publish and retire forms, and jump to filling or reviewing one. */
@Component({
  selector: 'app-form-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TranslateModule,
    ButtonModule, ConfirmDialogModule, InputTextModule, MenuModule, SelectModule,
    TableModule, TagModule, ToastModule, TooltipModule,
    HasPermissionDirective,
    FormDetailsDialogComponent, FormCloneDialogComponent, FormVersionsDialogComponent,
    FormPreviewDialogComponent,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './form-list.component.html',
})
export class FormListComponent {
  private readonly formsApi = inject(FormsService);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly PERMISSIONS = PERMISSIONS;
  protected readonly statusSeverity = formStatusSeverity;
  protected readonly statusLabelKey = formStatusLabelKey;

  protected readonly forms = signal<FormListItem[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyId = signal<string | null>(null);

  protected readonly search = signal('');
  protected readonly category = signal<string | null>(null);
  protected readonly status = signal<FormStatusFilterValue>(FormStatusFilter.Active);

  protected readonly detailsVisible = signal(false);
  protected readonly cloneVisible = signal(false);
  protected readonly versionsVisible = signal(false);
  protected readonly previewVisible = signal(false);
  protected readonly selected = signal<FormListItem | null>(null);

  /** The schema behind the preview dialog, fetched on demand — the grid rows do not carry it. */
  protected readonly previewDefinition = signal<SerializedForm | null>(null);

  protected readonly statusOptions: FilterOption[] = [
    { value: FormStatusFilter.Active, labelKey: formStatusFilterLabelKey(FormStatusFilter.Active) },
    { value: FormStatusFilter.All, labelKey: formStatusFilterLabelKey(FormStatusFilter.All) },
    ...FORM_STATUS_VALUES.map((value) => ({ value, labelKey: formStatusFilterLabelKey(value) })),
  ];

  protected readonly categoryOptions: FilterOption[] = [
    { value: null, labelKey: 'forms.allCategories' },
    ...FORM_CATEGORIES.map((value) => ({ value, labelKey: `forms.categories.${value}` })),
  ];

  private page = 1;
  private pageSize = 10;

  /** Rows show the name in the reader's own language. */
  protected readonly nameOf = computed(() => (form: FormListItem) =>
    this.locale.locale() === 'ar' ? form.nameAr : form.nameEn);

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    const status = this.status();

    this.loading.set(true);
    this.formsApi
      .list({
        pageNumber: this.page,
        pageSize: this.pageSize,
        searchTerm: this.search() || null,
        category: this.category(),
        status: status === FormStatusFilter.Active || status === FormStatusFilter.All ? null : status,
        excludeArchived: status === FormStatusFilter.Active,
      })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.forms.set(result.value?.items ?? []);
          this.totalRecords.set(result.value?.totalCount ?? 0);
        },
        error: (error) => this.fail(error),
      });
  }

  protected applyFilters(): void {
    this.page = 1;
    this.load();
  }

  protected openNew(): void {
    this.selected.set(null);
    this.detailsVisible.set(true);
  }

  protected openEdit(form: FormListItem): void {
    this.selected.set(form);
    this.detailsVisible.set(true);
  }

  protected openClone(form: FormListItem): void {
    this.selected.set(form);
    this.cloneVisible.set(true);
  }

  protected openVersions(form: FormListItem): void {
    this.selected.set(form);
    this.versionsVisible.set(true);
  }

  protected design(form: FormListItem): void {
    void this.router.navigate(['/forms', form.id, 'designer']);
  }

  /**
   * Previews the working design in a dialog rather than on a page of its own: the design is judged
   * against the grid it was picked from, and closing it puts the reader straight back there.
   *
   * No form id reaches the renderer, so media fields stay local and a preview leaves no pending
   * uploads behind.
   */
  protected preview(form: FormListItem): void {
    this.selected.set(form);
    this.busyId.set(form.id);

    this.formsApi
      .get(form.id)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (result) => {
          const schemaJson = result.value?.schemaJson;

          try {
            this.previewDefinition.set(
              schemaJson ? (JSON.parse(schemaJson) as SerializedForm) : null,
            );
          } catch {
            this.messages.add({
              severity: 'error',
              summary: this.translate.instant('forms.preview.invalidSchema'),
            });
            return;
          }

          this.previewVisible.set(true);
        },
        error: (error) => this.fail(error),
      });
  }

  protected fill(form: FormListItem): void {
    void this.router.navigate(['/forms', form.id, 'fill']);
  }

  protected submissions(form: FormListItem): void {
    void this.router.navigate(['/forms', form.id, 'submissions']);
  }

  /** The row overflow menu — the actions that are rarer or destructive. */
  protected menuFor(form: FormListItem): MenuItem[] {
    return [
      {
        label: this.translate.instant('forms.actions.versions'),
        icon: 'pi pi-history',
        command: () => this.openVersions(form),
      },
      {
        label: this.translate.instant('forms.actions.clone'),
        icon: 'pi pi-copy',
        command: () => this.openClone(form),
      },
      {
        label: this.translate.instant('forms.actions.submissions'),
        icon: 'pi pi-inbox',
        command: () => this.submissions(form),
      },
      { separator: true },
      {
        label: this.translate.instant('forms.actions.publish'),
        icon: 'pi pi-cloud-upload',
        disabled: !canPublish(form.status),
        command: () => this.publish(form),
      },
      {
        label: this.translate.instant('forms.actions.deprecate'),
        icon: 'pi pi-ban',
        disabled: !canDeprecate(form.status),
        command: () => this.deprecate(form),
      },
      {
        label: this.translate.instant('forms.actions.archive'),
        icon: 'pi pi-inbox',
        disabled: !canArchive(form.status),
        command: () => this.archive(form),
      },
    ];
  }

  protected canFillForm(form: FormListItem): boolean {
    return canFill(form.status, form.currentVersionNo);
  }

  protected publish(form: FormListItem): void {
    this.confirmAction(form, 'forms.confirm.publish', () => this.formsApi.publish(form.id), 'forms.publishSuccess');
  }

  protected deprecate(form: FormListItem): void {
    this.confirmAction(form, 'forms.confirm.deprecate', () => this.formsApi.deprecate(form.id), 'forms.deprecateSuccess');
  }

  protected archive(form: FormListItem): void {
    this.confirmAction(form, 'forms.confirm.archive', () => this.formsApi.archive(form.id), 'forms.archiveSuccess');
  }

  private confirmAction(
    form: FormListItem,
    messageKey: string,
    action: () => ReturnType<FormsService['publish']>,
    successKey: string,
  ): void {
    this.confirm.confirm({
      header: this.translate.instant('common.confirm'),
      message: this.translate.instant(messageKey, { name: this.nameOf()(form) }),
      accept: () => {
        this.busyId.set(form.id);
        action()
          .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busyId.set(null)))
          .subscribe({
            next: () => {
              this.messages.add({ severity: 'success', summary: this.translate.instant(successKey) });
              this.load();
            },
            error: (error) => this.fail(error),
          });
      },
    });
  }

  private fail(error: unknown): void {
    this.messages.add({
      severity: 'error',
      summary: this.translate.instant('common.error'),
      detail: formEngineErrorMessage(error, this.translate),
      life: 8000,
    });
  }
}
