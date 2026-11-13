import { WorkflowLibraryViewState } from './workflow-library-view-state';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, of, startWith, switchMap } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import {
  WorkflowLibraryService,
  LibraryPage,
  LibraryQuery,
  LibraryScope,
  LibraryWorkflow,
} from './workflow-library.service';

@Component({
  selector: 'app-workflow-library',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, NgTemplateOutlet],
  templateUrl: './workflow-library.component.html',
  styleUrl: './workflow-library.component.css',
})
export class WorkflowLibraryComponent {
  private readonly viewState = inject(WorkflowLibraryViewState);
  private expansionRestored = false;
  private readonly api = inject(WorkflowLibraryService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<void>();
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly data = signal<LibraryPage | null>(null);
  readonly expanded = signal(new Set<string>());
  readonly pages = computed(() =>
    Math.max(1, Math.ceil((this.data()?.totalGroups ?? 0) / this.query.pageSize)),
  );
  readonly first = computed(() => ((this.data()?.page ?? 1) - 1) * this.query.pageSize + 1);
  readonly last = computed(() =>
    Math.min((this.data()?.page ?? 1) * this.query.pageSize, this.data()?.totalGroups ?? 0),
  );
  query: LibraryQuery = {
    page: 1,
    pageSize: 10,
    search: '',
    status: 'Active',
    scope: '',
    sort: 'Updated',
  };

  constructor() {
    if (this.viewState.query) this.query = { ...this.viewState.query };
    if (this.viewState.expanded) {
      this.expanded.set(new Set(this.viewState.expanded));
      this.expansionRestored = true;
    }
    this.requests
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.loading.set(true);
          this.error.set(false);
          return this.api.list(this.query).pipe(
            catchError(() => {
              this.error.set(true);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.data.set(page);
        this.loading.set(false);
        if (page) {
          this.query.page = page.page;
          const open = new Set<string>();
          const visit = (items: LibraryWorkflow[], depth: number) =>
            items.forEach((item) => {
              if (
                item.children.length &&
                (depth === 0 ||
                  this.query.search ||
                  this.query.scope ||
                  this.query.status === 'Archived')
              )
                open.add(item.id);
              visit(item.children, depth + 1);
            });
          visit(page.items, 0);
          if (!this.expansionRestored) {
            this.expanded.set(open);
            this.expansionRestored = true;
          }
          this.viewState.expanded = new Set(this.expanded());
        }
      });
  }
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  name(item: LibraryWorkflow) {
    return this.t(item.name, item.nameAr || item.name);
  }
  scopeName(scope: LibraryScope) {
    return this.t(scope.nameEn || scope.code, scope.nameAr || scope.nameEn || scope.code);
  }
  scopeLevel(level: string) {
    const labels: Record<string, [string, string]> = {
      Cluster: ['Cluster', 'التجمع'],
      Cbu: ['CBU', 'وحدة الأعمال'],
      Branch: ['Branch', 'الفرع'],
      OperationArea: ['Operation area', 'منطقة التشغيل'],
    };
    const label = labels[level];
    return label ? this.t(...label) : level;
  }
  versionStatus(status: string) {
    const labels: Record<string, [string, string]> = {
      Draft: ['Draft', 'مسودة'],
      Published: ['Published', 'منشور'],
      Retired: ['Retired', 'متقاعد'],
      None: ['No version', 'بلا إصدار'],
    };
    return labels[status] ? this.t(...labels[status]) : status;
  }
  load(reset = true) {
    if (reset) this.query.page = 1;
    this.viewState.query = { ...this.query };
    this.requests.next();
  }
  go(page: number) {
    this.query.page = page;
    this.load(false);
  }
  reset() {
    this.query = {
      ...this.query,
      page: 1,
      search: '',
      status: 'Active',
      scope: '',
      sort: 'Updated',
    };
    this.load();
  }
  toggle(id: string) {
    this.expanded.update((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      this.viewState.expanded = new Set(next);
      return next;
    });
  }
  designer(item: LibraryWorkflow) {
    const base = ['/admin/workflow/definitions', item.id, 'versions'];
    return item.versionId ? [...base, item.versionId, 'designer'] : base;
  }
}
