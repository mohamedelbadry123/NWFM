import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { PageHeaderService } from '../../../core/layout/page-header.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { FormsService } from '../../../core/form-engine/forms.service';
import { FormSubmissionsService } from '../../../core/form-engine/form-submissions.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';
import { FormSubmissionRow } from '../../../core/form-engine/form-engine.models';
import { SubmissionViewDialogComponent } from './submission-view-dialog.component';

/**
 * What has been filled in for a form. The grid shows the submission's metadata only; the answers are
 * read in the viewer, rendered through the version's own schema so they are shown in context.
 */
@Component({
  selector: 'app-form-submissions',
  standalone: true,
  imports: [
    CommonModule, TranslateModule,
    ButtonModule, MessageModule, TableModule, TagModule,
    SubmissionViewDialogComponent,
  ],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex items-center justify-between">
        <div>
          <h2 class="text-lg font-semibold">{{ name() }}</h2>
          <p class="text-sm opacity-70">{{ 'forms.submissions.subtitle' | translate }}</p>
        </div>
        <p-button
          [label]="'forms.backToForms' | translate"
          icon="pi pi-arrow-left"
          [text]="true"
          (onClick)="back()"
        />
      </div>

      @if (error()) {
        <p-message severity="error" [text]="error()!" />
      }

      <div class="card p-4" style="background: var(--app-surface); border-radius: 0.75rem;">
        <p-table
          [value]="rows()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [rows]="20"
          [paginator]="true"
          [totalRecords]="totalRecords()"
          [loading]="loading()"
          [rowsPerPageOptions]="[20, 50, 100]"
          [rowHover]="true"
          styleClass="p-datatable-sm p-datatable-striped"
        >
          <ng-template pTemplate="header">
            <tr>
              <th>{{ 'forms.submissions.submittedAt' | translate }}</th>
              <th>{{ 'forms.submissions.submittedBy' | translate }}</th>
              <th>{{ 'forms.version' | translate }}</th>
              <th>{{ 'forms.submissions.context' | translate }}</th>
              <th class="w-24">{{ 'common.actions' | translate }}</th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ text(row, 'SubmittedDate') | date: 'medium' }}</td>
              <td>{{ text(row, 'SubmittedByName') || text(row, 'SubmittedBy') || '—' }}</td>
              <td><p-tag [value]="'v' + text(row, 'VersionNo')" severity="secondary" /></td>
              <td>
                @if (text(row, 'ContextType')) {
                  <span class="text-xs opacity-70">{{ text(row, 'ContextType') }} · {{ text(row, 'ContextId') }}</span>
                } @else {
                  —
                }
              </td>
              <td>
                <p-button
                  icon="pi pi-eye"
                  [text]="true"
                  [rounded]="true"
                  [ariaLabel]="'forms.submissions.view' | translate"
                  (onClick)="view(row)"
                />
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="5" class="p-6 text-center opacity-70">{{ 'forms.submissions.empty' | translate }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    </div>

    <app-submission-view-dialog
      [(visible)]="viewerVisible"
      [formId]="formId()"
      [row]="selected()"
    />
  `,
})
export class FormSubmissionsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly formsApi = inject(FormsService);
  private readonly submissionsApi = inject(FormSubmissionsService);
  private readonly pageHeader = inject(PageHeaderService);
  private readonly locale = inject(LocaleService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly formId = signal<string | null>(null);
  protected readonly rows = signal<FormSubmissionRow[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly viewerVisible = signal(false);
  protected readonly selected = signal<FormSubmissionRow | null>(null);

  private readonly nameEn = signal('');
  private readonly nameAr = signal('');

  protected readonly name = computed(() => (this.locale.locale() === 'ar' ? this.nameAr() : this.nameEn()));

  private page = 1;
  private pageSize = 20;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      return;
    }

    this.formId.set(id);

    this.formsApi
      .get(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.nameEn.set(result.value?.nameEn ?? '');
          this.nameAr.set(result.value?.nameAr ?? '');
          this.pageHeader.set({ titleText: this.name(), subtitleKey: 'forms.submissions.subtitle' });
        },
      });
  }

  protected load(event?: TableLazyLoadEvent): void {
    const id = this.formId();

    if (!id) {
      return;
    }

    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    this.loading.set(true);
    this.submissionsApi
      .list(id, this.page, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.rows.set(result.value?.items ?? []);
          this.totalRecords.set(result.value?.totalCount ?? 0);
        },
        error: (error) => this.error.set(formEngineErrorMessage(error, this.translate)),
      });
  }

  /** A submission row is a plain column map, so the template reads it by column name. */
  protected text(row: FormSubmissionRow, column: string): string {
    const value = row[column];

    return value === null || value === undefined ? '' : String(value);
  }

  protected view(row: FormSubmissionRow): void {
    this.selected.set(row);
    this.viewerVisible.set(true);
  }

  protected back(): void {
    void this.router.navigate(['/forms']);
  }
}
