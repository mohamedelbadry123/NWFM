import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators, FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Tabs, TabList, Tab, TabPanel, TabPanels } from 'primeng/tabs';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { InputTextModule } from 'primeng/inputtext';
import { ToastModule } from 'primeng/toast';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService } from 'primeng/api';
import { HasPermissionDirective, PERMISSIONS } from '../../core/auth/permissions';
import { LookupItem, LookupType, LookupsService } from '../../core/lookups/lookups.service';
import { LocaleService } from '../../core/i18n/locale.service';

interface LookupTab {
  type: LookupType;
  labelKey: string;
  parentType?: LookupType;
  parentLabelKey?: string;
}

@Component({
  selector: 'app-lookups',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ReactiveFormsModule, TranslateModule,
    TableModule, Tabs, TabList, Tab, TabPanel, TabPanels,
    ButtonModule, TagModule, InputTextModule, ToastModule, DialogModule, SelectModule, TooltipModule,
    HasPermissionDirective,
  ],
  providers: [MessageService],
  templateUrl: './lookups.component.html',
})
export class LookupsComponent {
  private readonly lookups = inject(LookupsService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);
  protected readonly locale = inject(LocaleService);
  protected readonly PERMISSIONS = PERMISSIONS;

  protected readonly tabs: LookupTab[] = [
    { type: 'Department', labelKey: 'lookups.tabs.departments' },
    { type: 'Cluster', labelKey: 'lookups.tabs.clusters' },
    { type: 'Cbu', labelKey: 'lookups.tabs.cbus', parentType: 'Cluster', parentLabelKey: 'lookups.cluster' },
    { type: 'Branch', labelKey: 'lookups.tabs.branches', parentType: 'Cbu', parentLabelKey: 'lookups.cbu' },
    { type: 'FieldActivityType', labelKey: 'workspace.fieldActivityTypes', parentType: 'Department', parentLabelKey: 'workspace.department' },
  ];

  protected readonly activeTab = signal(0);
  protected readonly items = signal<LookupItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected searchTerm = '';
  private page = 1;
  private pageSize = 10;

  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<LookupItem | null>(null);
  protected readonly saving = signal(false);
  protected readonly parentOptions = signal<{ label: string; value: string }[]>([]);

  protected readonly form = this.fb.group({
    code: this.fb.control('', [Validators.required, Validators.maxLength(50)]),
    nameEn: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    nameAr: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    parentCode: this.fb.control<string | null>(null),
  });

  protected currentTab(): LookupTab {
    return this.tabs[this.activeTab()];
  }

  protected onTabChange(value: string | number | undefined): void {
    const index = Number(value ?? 0);
    this.activeTab.set(index);
    this.searchTerm = '';
    this.page = 1;
    this.load();
  }

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.page = Math.floor((event.first ?? 0) / (event.rows ?? this.pageSize)) + 1;
      this.pageSize = event.rows ?? this.pageSize;
    }
    const tab = this.currentTab();
    this.loading.set(true);
    this.lookups.list(tab.type, this.page, this.pageSize, this.searchTerm || undefined).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.isSuccess && res.value) {
          this.items.set(res.value.items);
          this.total.set(res.value.totalCount);
        }
      },
      error: () => this.loading.set(false),
    });
  }

  protected onSearch(): void {
    this.page = 1;
    this.load();
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', nameEn: '', nameAr: '', parentCode: null });
    this.form.controls.code.enable();
    this.loadParents();
    this.dialogVisible.set(true);
  }

  protected openEdit(item: LookupItem): void {
    this.editing.set(item);
    this.form.reset({ code: item.code, nameEn: item.nameEn, nameAr: item.nameAr, parentCode: item.parentCode ?? null });
    this.form.controls.code.disable();
    this.loadParents();
    this.dialogVisible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const tab = this.currentTab();
    const raw = this.form.getRawValue();
    this.saving.set(true);
    const editing = this.editing();
    const request = editing
      ? this.lookups.update(tab.type, editing.id, { nameEn: raw.nameEn!, nameAr: raw.nameAr!, parentCode: raw.parentCode })
      : this.lookups.create(tab.type, { code: raw.code!, nameEn: raw.nameEn!, nameAr: raw.nameAr!, parentCode: raw.parentCode });

    request.subscribe({
      next: (res) => {
        this.saving.set(false);
        if (res.isSuccess) {
          this.dialogVisible.set(false);
          this.messages.add({ severity: 'success', summary: this.translate.instant('common.save') });
          this.load();
        } else {
          this.messages.add({ severity: 'error', summary: res.error?.message ?? this.translate.instant('error.generic') });
        }
      },
      error: () => {
        this.saving.set(false);
        this.messages.add({ severity: 'error', summary: this.translate.instant('error.generic') });
      },
    });
  }

  protected toggleStatus(item: LookupItem): void {
    this.lookups.setStatus(this.currentTab().type, item.id, !item.isActive).subscribe({
      next: (res) => {
        if (res.isSuccess) this.load();
        else this.messages.add({ severity: 'error', summary: res.error?.message ?? this.translate.instant('error.generic') });
      },
      error: () => this.messages.add({ severity: 'error', summary: this.translate.instant('error.generic') }),
    });
  }

  protected displayName(item: LookupItem): string {
    return this.locale.isRtl() ? item.nameAr : item.nameEn;
  }

  private loadParents(): void {
    const parentType = this.currentTab().parentType;
    if (!parentType) {
      this.parentOptions.set([]);
      return;
    }
    this.lookups.list(parentType, 1, 200).subscribe({
      next: (res) => {
        const items = res.value?.items ?? [];
        this.parentOptions.set(items.map(i => ({
          label: `${i.code} — ${this.locale.isRtl() ? i.nameAr : i.nameEn}`,
          value: i.code,
        })));
      },
    });
  }
}
