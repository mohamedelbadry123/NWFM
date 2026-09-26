import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { TranslateService } from '@ngx-translate/core';
import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { FormBuilderStore } from './store/form-builder.store';
import { FieldPaletteComponent } from './components/field-palette.component';
import { BuilderCanvasComponent } from './components/builder-canvas.component';
import { FieldEditorDialogComponent } from './components/field-editor-dialog.component';
import { FormPreviewDialogComponent } from './components/form-preview-dialog.component';
import { JsonOutputComponent } from './components/json-output.component';
import { FORM_BUILDER_SAMPLE_FILES, formBuilderSampleUrl } from './data/form-builder-samples';
import { DROP_IDS, ELEMENT_TYPES, type ElementType } from '../../../shared/form-schema/form-schema.types';
import { FormsService } from '../../../core/form-engine/forms.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';

/**
 * The form designer. With a `:id` route parameter it edits that form's working schema; without one
 * it is a sandbox that loads the sample document and saves nothing.
 */
@Component({
  selector: 'app-form-builder',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [FormBuilderStore, MessageService],
  imports: [
    CommonModule,
    FormsModule,
    InputTextModule,
    ButtonModule,
    ToastModule,
    TranslateContextDirective,
    FieldPaletteComponent,
    BuilderCanvasComponent,
    FieldEditorDialogComponent,
    FormPreviewDialogComponent,
    JsonOutputComponent,
  ],
  templateUrl: './form-builder.component.html',
  styleUrl: './form-builder.component.css',
})
export class FormBuilderComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly formsApi = inject(FormsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(FormBuilderStore);

  /** The form being designed, or null in the sandbox. */
  protected readonly formId = signal<string | null>(null);

  protected readonly editorVisible = signal(false);
  protected readonly editingKey = signal<string | null>(null);
  protected readonly loadingSample = signal(false);
  protected readonly previewVisible = signal(false);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly publishing = signal(false);

  protected readonly isDesigner = computed(() => this.formId() !== null);

  /** True while any call is in flight — every action is disabled together. */
  protected readonly busy = computed(() => this.loading() || this.saving() || this.publishing());

  /** CDK drop-list ids the palette items can drop onto (root plus top-level sections). */
  protected readonly listIds = computed<string[]>(() => [
    DROP_IDS.CanvasRoot,
    ...this.store.elements().filter((el) => el.type === ELEMENT_TYPES.Section).map((el) => el.key),
  ]);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    this.formId.set(id);

    if (id) {
      await this.loadForm(id);
    } else {
      await this.loadSample();
    }
  }

  private async loadForm(id: string): Promise<void> {
    this.loading.set(true);
    try {
      const result = await firstValueFrom(this.formsApi.get(id));
      const form = result.value;

      if (form?.schemaJson) {
        this.store.loadFromJson(JSON.parse(form.schemaJson) as Record<string, unknown>);
      } else {
        this.store.resetForm();
      }

      if (form?.nameEn) {
        this.store.setNameEn(form.nameEn);
      }

      if (form?.nameAr) {
        this.store.setNameAr(form.nameAr);
      }
    } catch (error) {
      this.error('formBuilder.loadError', error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    const id = this.formId();
    if (id === null || this.busy()) {
      return;
    }

    this.saving.set(true);
    try {
      await firstValueFrom(this.formsApi.saveSchema(id, this.store.json()));
      this.success('formBuilder.saveSuccess');
    } catch (error) {
      this.error('formBuilder.saveError', error);
    } finally {
      this.saving.set(false);
    }
  }

  protected async publish(): Promise<void> {
    const id = this.formId();
    if (id === null || this.busy()) {
      return;
    }

    this.publishing.set(true);
    try {
      // Save first, so the version that is frozen is the design on screen.
      await firstValueFrom(this.formsApi.saveSchema(id, this.store.json()));
      await firstValueFrom(this.formsApi.publish(id));
      this.success('formBuilder.publishSuccess');
    } catch (error) {
      // Publish refusals are specific — a reused data name, an illegal one — so the server's own
      // message is worth more here than a generic failure.
      this.error('formBuilder.publishError', error);
    } finally {
      this.publishing.set(false);
    }
  }

  protected backToForms(): void {
    void this.router.navigate(['/forms']);
  }

  protected async loadSample(): Promise<void> {
    this.loadingSample.set(true);
    try {
      const url = formBuilderSampleUrl(FORM_BUILDER_SAMPLE_FILES.AllInputTypes);
      const raw = await firstValueFrom(this.http.get<Record<string, unknown>>(url));
      this.store.loadFromJson(raw);
      this.success('formBuilder.loadSampleSuccess');
    } catch (error) {
      this.error('formBuilder.loadSampleError', error);
    } finally {
      this.loadingSample.set(false);
    }
  }

  protected newForm(): void {
    this.store.resetForm();
    this.messageService.add({
      severity: 'info',
      summary: this.translate.instant('formBuilder.newFormSuccess'),
    });
  }

  protected onPaletteAdd(type: ElementType): void {
    const element = this.store.addFromType(type);

    // Sections are configured inline on the canvas; everything else opens its editor.
    if (type !== ELEMENT_TYPES.Section) {
      this.openEditor(element.key);
    }
  }

  protected openPreview(): void {
    this.previewVisible.set(true);
  }

  protected openEditor(key: string): void {
    this.editingKey.set(key);
    this.editorVisible.set(true);
  }

  private success(key: string): void {
    this.messageService.add({ severity: 'success', summary: this.translate.instant(key) });
  }

  private error(key: string, error: unknown): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate.instant(key),
      detail: formEngineErrorMessage(error, this.translate, key),
      life: 8000,
    });
  }
}
