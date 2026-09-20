import { Component, DestroyRef, inject, input, model, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { DialogModule } from 'primeng/dialog';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { FormsService } from '../../../core/form-engine/forms.service';
import { FormListItem, FormVersionSummary } from '../../../core/form-engine/form-engine.models';

/**
 * A form's publish history. Each row is a frozen schema that submissions may still point at, which
 * is why versions are listed rather than hidden behind "current".
 */
@Component({
  selector: 'app-form-versions-dialog',
  standalone: true,
  imports: [CommonModule, TranslateModule, DialogModule, TableModule, TagModule],
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [draggable]="false"
      [style]="{ width: '40rem' }"
      [header]="'forms.versions.title' | translate"
      (onShow)="load()"
    >
      <p-table
        [value]="versions()"
        [loading]="loading()"
        [paginator]="versions().length > 10"
        [rows]="10"
        paginatorDropdownAppendTo="body"
        styleClass="p-datatable-sm"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>{{ 'forms.versions.no' | translate }}</th>
            <th>{{ 'forms.versions.publishedBy' | translate }}</th>
            <th>{{ 'forms.versions.publishedAt' | translate }}</th>
          </tr>
        </ng-template>

        <ng-template pTemplate="body" let-version>
          <tr>
            <td>
              <p-tag
                [value]="'v' + version.versionNo"
                [severity]="version.versionNo === form()?.currentVersionNo ? 'success' : 'secondary'"
              />
            </td>
            <td>{{ version.publishedBy || '—' }}</td>
            <td>{{ version.publishedAt | date: 'medium' }}</td>
          </tr>
        </ng-template>

        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="3" class="p-6 text-center opacity-70">{{ 'forms.versions.empty' | translate }}</td>
          </tr>
        </ng-template>
      </p-table>
    </p-dialog>
  `,
})
export class FormVersionsDialogComponent {
  private readonly formsApi = inject(FormsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model.required<boolean>();
  readonly form = input<FormListItem | null>(null);

  protected readonly versions = signal<FormVersionSummary[]>([]);
  protected readonly loading = signal(false);

  protected load(): void {
    const form = this.form();

    if (!form) {
      this.versions.set([]);
      return;
    }

    this.loading.set(true);
    this.formsApi
      .versions(form.id)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => this.versions.set(result.value ?? []),
        error: () => this.versions.set([]),
      });
  }
}
