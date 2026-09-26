import { ChangeDetectionStrategy, Component, input, model, signal, viewChild } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { DynamicFormRendererComponent } from '../../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import type { SerializedForm } from '../store/form-builder.store';

/**
 * Renders the builder's current definition as a real ngx-formly form so it can
 * be filled in exactly as an end user would. Client-only: nothing is persisted,
 * and no form id is passed, so media fields stay local instead of uploading.
 *
 * The form itself is `app-dynamic-form-renderer`, shared with the fill
 * dialog and the fill page — what the designer previews here is
 * literally what a respondent gets.
 */
@Component({
  selector: 'app-form-preview-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DialogModule,
    ButtonModule,
    MessageModule,
    TranslateContextDirective,
    DynamicFormRendererComponent,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [header]="t('formBuilder.preview.title')"
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [style]="{ width: '68rem' }"
        [contentStyle]="{ overflow: 'auto' }"
        [draggable]="false"
        [maximizable]="true"
        styleClass="preview-dialog"
      >
        <!-- Hint + live validity -->
        <div class="flex flex-wrap items-center gap-3 mb-4">
          <p class="flex-1 min-w-[16rem] text-sm text-[var(--p-text-muted-color)] m-0">
            <i class="pi pi-info-circle me-1.5 text-[var(--p-primary-color)]"></i>
            {{ t('formBuilder.preview.hint') }}
          </p>
          @if (renderer.fields().length > 0) {
            <span
              class="status-pill"
              [class.is-valid]="renderer.form().valid"
              [class.is-invalid]="!renderer.form().valid"
            >
              <i [class]="renderer.form().valid ? 'pi pi-check-circle' : 'pi pi-exclamation-circle'"></i>
              {{
                renderer.form().valid
                  ? t('formBuilder.preview.valid')
                  : t('formBuilder.preview.invalidCount', { count: renderer.invalidCount() })
              }}
            </span>
          }
        </div>

        <app-dynamic-form-renderer
          #renderer
          [definition]="definition()"
          [emptyMessage]="t('formBuilder.preview.empty')"
          (submitted)="submit()"
        />

        @if (renderer.fields().length > 0) {
          @if (submittedJson(); as payload) {
            <div class="mt-4">
              <p-message severity="success" [text]="t('formBuilder.preview.submitSuccess')" styleClass="w-full" />
              <pre class="json-panel mt-2">{{ payload }}</pre>
            </div>
          } @else if (submitAttempted()) {
            <p-message
              severity="error"
              [text]="t('formBuilder.preview.submitInvalid')"
              styleClass="w-full mt-4"
            />
          }

          <!-- Live payload, collapsed by default so the form stays the focus -->
          <div class="mt-4 rounded-xl border border-[var(--app-border)] overflow-hidden">
            <button
              type="button"
              class="w-full flex items-center gap-2 px-4 py-2.5 text-xs font-semibold bg-[var(--app-surface-alt)] hover:bg-[var(--app-surface)] transition"
              (click)="jsonExpanded.set(!jsonExpanded())"
            >
              <i [class]="jsonExpanded() ? 'pi pi-chevron-down' : 'pi pi-chevron-right'"></i>
              <span class="flex-1 text-start">{{ t('formBuilder.preview.liveModel') }}</span>
              <span class="font-mono text-[10px] text-[var(--p-text-muted-color)]">
                {{ t('formBuilder.preview.answered', { count: renderer.answeredCount() }) }}
              </span>
            </button>
            @if (jsonExpanded()) {
              <pre class="json-panel rounded-none border-0">{{ payloadJson(renderer) }}</pre>
            }
          </div>
        }

        <ng-template pTemplate="footer">
          <p-button [label]="t('formBuilder.preview.reset')" icon="pi pi-refresh" [text]="true" (onClick)="rebuild()" />
          <p-button [label]="t('formBuilder.actions.cancel')" [text]="true" severity="secondary" (onClick)="visible.set(false)" />
          <p-button
            [label]="t('formBuilder.preview.submit')"
            icon="pi pi-check"
            [disabled]="!hasFields()"
            (onClick)="submit()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
  styles: [
    `
      /* Control styling lives with the renderer; what is left is this dialog's own chrome. */

      /* ── Panels ─────────────────────────────────────────────────────── */
      .status-pill {
        display: inline-flex;
        align-items: center;
        gap: 0.35rem;
        padding: 0.25rem 0.7rem;
        border-radius: 9999px;
        font-size: 0.75rem;
        font-weight: 600;
        white-space: nowrap;
      }
      .status-pill.is-valid {
        background: var(--p-green-50, #f0fdf4);
        color: var(--p-green-700, #15803d);
      }
      .status-pill.is-invalid {
        background: var(--p-orange-50, #fff7ed);
        color: var(--p-orange-700, #c2410c);
      }
      :host-context([data-theme='dark']) .status-pill.is-valid {
        background: rgba(34, 197, 94, 0.15);
        color: #4ade80;
      }
      :host-context([data-theme='dark']) .status-pill.is-invalid {
        background: rgba(249, 115, 22, 0.15);
        color: #fb923c;
      }

      .json-panel {
        margin: 0;
        padding: 1rem;
        background: #020617;
        color: #34d399;
        font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
        font-size: 0.75rem;
        line-height: 1.6;
        border-radius: 0.75rem;
        overflow: auto;
        max-height: 18rem;
      }
    `,
  ],
})
export class FormPreviewDialogComponent {
  readonly visible = model(false);
  /**
   * The exported definition — `FormBuilderStore.definition()`. Building from the exported shape
   * rather than the builder's editing state means the preview reflects the JSON it produces.
   */
  readonly definition = input<SerializedForm | null>(null);

  /** The footer sits in its own template, out of reach of the `#renderer` reference. */
  private readonly rendererRef = viewChild(DynamicFormRendererComponent);

  protected readonly submittedJson = signal<string | null>(null);
  protected readonly submitAttempted = signal(false);
  /** Collapsed by default so the rendered form stays the focus. */
  protected readonly jsonExpanded = signal(false);

  /** Read through the query rather than `#renderer` — the footer is a sibling view. */
  protected hasFields(): boolean {
    return (this.rendererRef()?.fields().length ?? 0) > 0;
  }

  /**
   * Called on every change detection cycle: Formly mutates the model object in
   * place, so there is no reference change to react to.
   */
  protected payloadJson(renderer: DynamicFormRendererComponent): string {
    return JSON.stringify(renderer.payload(), null, 2);
  }

  protected rebuild(): void {
    this.submittedJson.set(null);
    this.submitAttempted.set(false);
    this.rendererRef()?.rebuild();
  }

  protected submit(): void {
    const renderer = this.rendererRef();
    if (!renderer) {
      return;
    }

    this.submitAttempted.set(true);

    if (!renderer.validate()) {
      this.submittedJson.set(null);
      return;
    }

    this.submittedJson.set(this.payloadJson(renderer));
  }
}
