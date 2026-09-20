import { Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { TagModule } from 'primeng/tag';
import { DynamicFormRendererComponent } from '../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { PageHeaderService } from '../../../core/layout/page-header.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { FormsService } from '../../../core/form-engine/forms.service';
import { FormSubmissionsService } from '../../../core/form-engine/form-submissions.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';

/**
 * Fills a form and records the submission.
 *
 * The version is resolved up front and sent with the answers, so a redesign that starts while
 * someone is filling cannot change what they are answering. A client key is generated per fill, so
 * re-sending after a lost response returns the original submission instead of writing a second row.
 */
@Component({
  selector: 'app-form-fill',
  standalone: true,
  imports: [
    CommonModule, TranslateModule,
    ButtonModule, MessageModule, TagModule, ToastModule,
    DynamicFormRendererComponent,
  ],
  providers: [MessageService],
  template: `
    <p-toast />

    <div class="flex flex-col gap-4">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 class="text-lg font-semibold">{{ name() }}</h2>
          <div class="flex items-center gap-2 text-sm opacity-70">
            <span>{{ code() }}</span>
            @if (versionNo() !== null) {
              <p-tag [value]="'v' + versionNo()" severity="secondary" />
            }
          </div>
        </div>

        <div class="flex items-center gap-2">
          <p-button
            [label]="'forms.backToForms' | translate"
            icon="pi pi-arrow-left"
            [text]="true"
            [disabled]="submitting()"
            (onClick)="back()"
          />
          <p-button
            [label]="'forms.fill.submit' | translate"
            icon="pi pi-check"
            [loading]="submitting()"
            [disabled]="submitting() || loading() || schema() === null"
            (onClick)="submit()"
          />
        </div>
      </div>

      @if (error()) {
        <p-message severity="error" [text]="error()!" />
      }

      @if (submittedId()) {
        <p-message severity="success" [text]="'forms.fill.success' | translate" />
      }

      <div class="card p-4" style="background: var(--app-surface); border-radius: 0.75rem;">
        <app-dynamic-form-renderer
          [definition]="schema()"
          [formId]="formId()"
          [versionNo]="versionNo()"
          [contextType]="contextType()"
          [contextId]="contextId()"
          [emptyMessage]="'forms.fill.empty' | translate"
          (submitted)="submit()"
        />
      </div>
    </div>
  `,
})
export class FormFillComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly formsApi = inject(FormsService);
  private readonly submissionsApi = inject(FormSubmissionsService);
  private readonly pageHeader = inject(PageHeaderService);
  private readonly locale = inject(LocaleService);
  private readonly translate = inject(TranslateService);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly renderer = viewChild(DynamicFormRendererComponent);

  protected readonly formId = signal<string | null>(null);
  protected readonly versionNo = signal<number | null>(null);
  protected readonly schema = signal<Record<string, unknown> | null>(null);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly submittedId = signal<string | null>(null);
  protected readonly code = signal('');

  private readonly nameEn = signal('');
  private readonly nameAr = signal('');

  protected readonly name = computed(() => (this.locale.locale() === 'ar' ? this.nameAr() : this.nameEn()));

  /** What this fill belongs to, when the caller said so — e.g. a workflow work item. */
  protected readonly contextType = signal<string | null>(null);
  protected readonly contextId = signal<string | null>(null);

  /**
   * One key per fill session. It is what makes a re-send idempotent: the server answers with the
   * submission it already recorded rather than writing the fill twice.
   */
  private clientSubmissionId = crypto.randomUUID();

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const query = this.route.snapshot.queryParamMap;

    this.contextType.set(query.get('contextType'));
    this.contextId.set(query.get('contextId'));

    if (!id) {
      return;
    }

    this.formId.set(id);
    this.loadVersion(id, Number(query.get('versionNo')) || null);
  }

  private loadVersion(id: string, versionNo: number | null): void {
    this.loading.set(true);
    this.formsApi
      .version(id, versionNo)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          const version = result.value;

          if (!version) {
            this.error.set(this.translate.instant('forms.notFound'));
            return;
          }

          this.versionNo.set(version.versionNo);
          this.code.set(version.code);
          this.nameEn.set(version.nameEn);
          this.nameAr.set(version.nameAr);
          this.pageHeader.set({ titleText: this.name(), subtitleKey: 'forms.fill.subtitle' });

          if (!version.acceptsSubmissions) {
            this.error.set(this.translate.instant('forms.fill.closed'));
          }

          try {
            this.schema.set(JSON.parse(version.schemaJson) as Record<string, unknown>);
          } catch {
            this.error.set(this.translate.instant('forms.preview.invalidSchema'));
          }
        },
        error: (error) => this.error.set(formEngineErrorMessage(error, this.translate)),
      });
  }

  protected submit(): void {
    const renderer = this.renderer();
    const id = this.formId();

    if (!renderer || !id || this.submitting()) {
      return;
    }

    if (!renderer.validate()) {
      this.messages.add({
        severity: 'warn',
        summary: this.translate.instant('forms.fill.invalid'),
        detail: this.translate.instant('forms.fill.invalidDetail', { count: renderer.invalidCount() }),
      });
      return;
    }

    this.submitting.set(true);
    this.submissionsApi
      .submit(id, {
        versionNo: this.versionNo(),
        contextType: this.contextType(),
        contextId: this.contextId(),
        clientSubmissionId: this.clientSubmissionId,
        clientFilledAt: new Date().toISOString(),
        answers: renderer.payload(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (result) => {
          this.submittedId.set(result.value?.submissionId ?? null);
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('forms.fill.success'),
          });

          // A fresh key and a blank form, so the next fill is a new submission rather than a replay.
          this.clientSubmissionId = crypto.randomUUID();
          renderer.rebuild();
        },
        error: (error) => this.messages.add({
          severity: 'error',
          summary: this.translate.instant('forms.fill.error'),
          detail: formEngineErrorMessage(error, this.translate),
          life: 10000,
        }),
      });
  }

  protected back(): void {
    void this.router.navigate(['/forms']);
  }
}
