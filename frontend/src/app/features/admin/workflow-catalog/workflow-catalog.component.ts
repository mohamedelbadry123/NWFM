import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowModuleCatalogService } from '../../workflow/workflow-module-catalog.service';
import type { ModuleCatalogEntry } from '@shared/models/models/Workflow/Application/Constants/module-catalog-entry';
import type { EntityTypeEntry } from '@shared/models/models/Workflow/Application/Constants/entity-type-entry';
import { LocaleService } from '@core/i18n/locale.service';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

export type CatalogView = 'systems' | 'modules' | 'screens';

interface CatalogScreenRow {
  moduleKey: string;
  moduleName: string;
  entityType: string;
  entityName: string;
  triggerCount: number;
}

@Component({
  selector: 'app-workflow-catalog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TranslatePipe,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-catalog.component.html',
})
export class WorkflowCatalogComponent implements OnInit {
  private readonly service = inject(WorkflowModuleCatalogService);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LocaleService);

  protected readonly view = signal<CatalogView>('modules');
  protected readonly catalog = signal<ModuleCatalogEntry[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly titleKey = computed(() => `workflow.catalog.${this.view()}.title`);
  protected readonly subtitleKey = computed(() => `workflow.catalog.${this.view()}.subtitle`);

  protected readonly screens = computed<CatalogScreenRow[]>(() => {
    const rows: CatalogScreenRow[] = [];
    for (const mod of this.catalog()) {
      for (const entity of mod.entityTypes ?? []) {
        rows.push({
          moduleKey: mod.moduleKey ?? '',
          moduleName: this.moduleName(mod),
          entityType: entity.entityType ?? '',
          entityName: this.entityName(entity),
          triggerCount: entity.triggerEvents?.length ?? 0,
        });
      }
    }
    return rows;
  });

  ngOnInit(): void {
    this.route.data.subscribe(data => {
      this.view.set((data['catalogView'] as CatalogView | undefined) ?? 'modules');
    });
    this.service.getCatalog().subscribe({
      next: items => {
        this.catalog.set(items ?? []);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('error.generic');
        this.isLoading.set(false);
      },
    });
  }

  protected moduleName(mod: ModuleCatalogEntry): string {
    return this.locale.isRtl()
      ? (mod.nameAr || mod.nameEn || mod.moduleKey || '—')
      : (mod.nameEn || mod.nameAr || mod.moduleKey || '—');
  }

  protected entityName(entity: EntityTypeEntry): string {
    return this.locale.isRtl()
      ? (entity.nameAr || entity.nameEn || entity.entityType || '—')
      : (entity.nameEn || entity.nameAr || entity.entityType || '—');
  }

  protected tabClass(view: CatalogView): string {
    return this.view() === view
      ? 'rounded-lg bg-primary px-3 py-1.5 text-sm font-semibold text-white shadow-sm ring-2 ring-primary/25'
      : 'rounded-lg border border-ink-200 bg-white px-3 py-1.5 text-sm text-ink-600 hover:bg-ink-50 hover:text-ink-900 dark:border-dark-600 dark:bg-dark-800 dark:text-dark-300 dark:hover:bg-dark-700 dark:hover:text-white';
  }
}
