import { Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { FieldCatalogService } from '../../../core/form-engine/field-catalog.service';
import { FieldCatalogItem } from '../../../core/form-engine/form-engine.models';

/**
 * Every canonical field name and the type its stored column is built on.
 *
 * Read-only by design: a name's type is fixed the moment it is first published, because changing it
 * would change what every existing answer under that name means. Names are added by publishing a
 * form that uses them.
 */
@Component({
  selector: 'app-field-catalog',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, ButtonModule, InputTextModule, TableModule, TagModule],
  template: `
    <div class="card mt-4">
      <p-table
        [value]="entries()"
        [lazy]="true"
        (onLazyLoad)="load($event)"
        [rows]="10"
        [paginator]="true"
        [totalRecords]="totalRecords()"
        [loading]="loading()"
        [rowsPerPageOptions]="[10, 25, 50]"
        [showCurrentPageReport]="true"
        [currentPageReportTemplate]="'common.pageReport' | translate"
        [rowHover]="true"
        styleClass="app-table p-datatable-sm p-datatable-striped"
      >
        <ng-template pTemplate="caption">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span class="text-sm opacity-70">{{ 'fieldCatalog.hint' | translate }}</span>
            <div class="flex items-center gap-2">
              <input
                pInputText
                type="search"
                [(ngModel)]="search"
                (keyup.enter)="applySearch()"
                [placeholder]="'common.search' | translate"
                class="w-64"
              />
              <p-button icon="pi pi-search" [text]="true" (onClick)="applySearch()" [ariaLabel]="'common.search' | translate" />
            </div>
          </div>
        </ng-template>

        <ng-template pTemplate="header">
          <tr>
            <th>{{ 'fieldCatalog.dataName' | translate }}</th>
            <th>{{ 'fieldCatalog.fieldType' | translate }}</th>
            <th>{{ 'fieldCatalog.labelEn' | translate }}</th>
            <th>{{ 'fieldCatalog.labelAr' | translate }}</th>
          </tr>
        </ng-template>

        <ng-template pTemplate="body" let-entry>
          <tr>
            <td class="font-mono text-sm">{{ entry.dataName }}</td>
            <td><p-tag [value]="'formBuilder.types.' + entry.fieldType | translate" severity="secondary" /></td>
            <td>{{ entry.labelEn || '—' }}</td>
            <td dir="rtl">{{ entry.labelAr || '—' }}</td>
          </tr>
        </ng-template>

        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="4" class="p-6 text-center opacity-70">{{ 'fieldCatalog.empty' | translate }}</td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
})
export class FieldCatalogComponent {
  private readonly catalog = inject(FieldCatalogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly entries = signal<FieldCatalogItem[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly search = signal('');

  private page = 1;
  private pageSize = 10;

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    this.loading.set(true);
    this.catalog
      .paged(this.page, this.pageSize, this.search() || null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.entries.set(result.value?.items ?? []);
          this.totalRecords.set(result.value?.totalCount ?? 0);
        },
        error: () => this.entries.set([]),
      });
  }

  protected applySearch(): void {
    this.page = 1;
    this.load();
  }
}
