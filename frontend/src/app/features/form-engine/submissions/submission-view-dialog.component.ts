import { Component, DestroyRef, effect, inject, input, model, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import { DynamicFormRendererComponent } from '../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { FormsService } from '../../../core/form-engine/forms.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';
import { FormSubmissionRow } from '../../../core/form-engine/form-engine.models';

/**
 * Reads one submission back through the schema of the version it answered.
 *
 * Deliberately the form rather than a list of label/value pairs: the grouping, the ordering and the
 * conditional sections are the form, and an answer read outside them can be read wrongly. Rendering
 * the pinned version is also what keeps a two-year-old submission honest after the form has moved on.
 */
@Component({
  selector: 'app-submission-view-dialog',
  standalone: true,
  imports: [CommonModule, TranslateModule, DialogModule, MessageModule, TagModule, DynamicFormRendererComponent],
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [draggable]="false"
      [style]="{ width: '56rem' }"
      [header]="'forms.submissions.view' | translate"
    >
      <div class="flex flex-col gap-3">
        @if (versionNo() !== null) {
          <div class="flex items-center gap-2 text-sm opacity-70">
            <p-tag [value]="'v' + versionNo()" severity="secondary" />
            <span>{{ 'forms.submissions.renderedWithVersion' | translate }}</span>
          </div>
        }

        @if (error()) {
          <p-message severity="error" [text]="error()!" />
        }

        @if (loading()) {
          <p class="p-6 text-center opacity-70">{{ 'common.loading' | translate }}</p>
        } @else if (schema()) {
          <app-dynamic-form-renderer
            [definition]="schema()"
            [answers]="answers()"
            [readOnly]="true"
            [emptyMessage]="'forms.submissions.empty' | translate"
          />
        }
      </div>
    </p-dialog>
  `,
})
export class SubmissionViewDialogComponent {
  private readonly formsApi = inject(FormsService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model.required<boolean>();
  readonly formId = input<string | null>(null);
  readonly row = input<FormSubmissionRow | null>(null);

  protected readonly schema = signal<Record<string, unknown> | null>(null);
  protected readonly answers = signal<Record<string, unknown> | null>(null);
  protected readonly versionNo = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The version a row names is the one it must be read with, so each row reloads its own schema. */
  private loadedVersion: number | null = null;

  constructor() {
    effect(() => {
      const row = this.row();
      const formId = this.formId();

      if (!this.visible() || !row || !formId) {
        return;
      }

      const versionNo = Number(row['VersionNo']) || null;
      this.answers.set(row);
      this.versionNo.set(versionNo);

      if (versionNo !== null && versionNo !== this.loadedVersion) {
        this.loadSchema(formId, versionNo);
      }
    });
  }

  private loadSchema(formId: string, versionNo: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.formsApi
      .version(formId, versionNo)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          const version = result.value;

          if (!version) {
            this.error.set(this.translate.instant('forms.notFound'));
            return;
          }

          try {
            this.schema.set(JSON.parse(version.schemaJson) as Record<string, unknown>);
            this.loadedVersion = versionNo;
          } catch {
            this.error.set(this.translate.instant('forms.preview.invalidSchema'));
          }
        },
        error: (error) => this.error.set(formEngineErrorMessage(error, this.translate)),
      });
  }
}
