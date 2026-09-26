import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { finalize, firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { Chip } from 'primeng/chip';
import { SelectModule } from 'primeng/select';

import { LookupsService } from '../../../core/lookups/lookups.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import {
  EMPTY_ORG_LOCATION,
  LEVEL_LABEL_KEYS,
  isLevelUnder,
  ORG_SCOPE_LEVELS,
  OrgLocation,
  OrgScopeAssignment,
  OrgScopeLevel,
} from './org-scope.model';

interface SelectOption {
  readonly label: string;
  readonly value: string;
}

/**
 * Cascading Cluster → CBU → (Branch | Operation Area) picker.
 *
 * - **single** — one place. Emits an {@link OrgLocation}.
 * - **multi** — several coverage rows for a user. Emits {@link OrgScopeAssignment}[].
 */
@Component({
  selector: 'app-org-scope-selector',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, ButtonModule, Chip, SelectModule],
  templateUrl: './org-scope-selector.component.html',
})
export class OrgScopeSelectorComponent {
  readonly mode = input<'single' | 'multi'>('single');
  readonly layout = input<'grid' | 'row'>('grid');
  readonly disabled = input(false);
  readonly showOperationArea = input(true);
  readonly initialLocation = input<OrgLocation | null>(null);
  readonly initialScopes = input<readonly OrgScopeAssignment[]>([]);

  readonly locationChange = output<OrgLocation>();
  readonly scopesChange = output<OrgScopeAssignment[]>();

  private readonly lookups = inject(LookupsService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);

  protected readonly clusterCode = signal<string | null>(null);
  protected readonly cbuCode = signal<string | null>(null);
  protected readonly branchCode = signal<string | null>(null);
  protected readonly operationAreaCode = signal<string | null>(null);
  protected readonly departmentId = signal<string | null>(null);

  protected readonly clusters = signal<SelectOption[]>([]);
  protected readonly cbus = signal<SelectOption[]>([]);
  protected readonly branches = signal<SelectOption[]>([]);
  protected readonly operationAreas = signal<SelectOption[]>([]);
  protected readonly departments = signal<SelectOption[]>([]);

  protected readonly loadingClusters = signal(false);
  protected readonly loadingCbus = signal(false);
  protected readonly loadingBranches = signal(false);
  protected readonly loadingOperationAreas = signal(false);
  protected readonly loadingDepartments = signal(false);

  protected readonly scopes = signal<OrgScopeAssignment[]>([]);

  private readonly scopeLabels = signal<Map<string, string>>(new Map());
  private readonly departmentLabels = signal<Map<string, string>>(new Map());

  protected readonly selectedLevel = computed<OrgScopeLevel | null>(() => {
    if (this.operationAreaCode()) return ORG_SCOPE_LEVELS.operationArea;
    if (this.branchCode()) return ORG_SCOPE_LEVELS.branch;
    if (this.cbuCode()) return ORG_SCOPE_LEVELS.cbu;
    if (this.clusterCode()) return ORG_SCOPE_LEVELS.cluster;
    return null;
  });

  protected readonly selectedCode = computed<string | null>(
    () => this.operationAreaCode() ?? this.branchCode() ?? this.cbuCode() ?? this.clusterCode(),
  );

  protected readonly canAdd = computed(() => {
    const level = this.selectedLevel();
    const code = this.selectedCode();
    const department = this.departmentId();

    if (this.disabled()) {
      return false;
    }

    if ((!level || !code) && department === null) {
      return false;
    }

    return !this.scopes().some(scope =>
      (scope.level ?? null) === (level ?? null)
      && (scope.code ?? null) === (code ?? null)
      && (scope.departmentId ?? null) === department);
  });

  constructor() {
    this.loadClusters();
    this.loadDepartments();

    effect(() => {
      const location = this.initialLocation();
      if (location) {
        untracked(() => void this.seedFromLocation(location));
      }
    });

    effect(() => {
      const initial = this.initialScopes();
      untracked(() => {
        this.scopes.set(
          initial.map(scope => ({
            level: scope.level,
            code: scope.code,
            departmentId: scope.departmentId,
          })),
        );
        if (initial.length > 0) {
          this.loadScopeLabels();
        }
      });
    });
  }

  protected onClusterChange(code: string | null): void {
    this.clusterCode.set(code);
    this.cbuCode.set(null);
    this.branchCode.set(null);
    this.operationAreaCode.set(null);
    this.branches.set([]);
    this.operationAreas.set([]);
    this.loadCbus(code);
    this.emitLocation();
  }

  protected onCbuChange(code: string | null): void {
    this.cbuCode.set(code);
    this.branchCode.set(null);
    this.operationAreaCode.set(null);
    this.loadBranches(code);
    this.loadOperationAreas(code);
    this.emitLocation();
  }

  protected onBranchChange(code: string | null): void {
    this.branchCode.set(code);
    this.emitLocation();
  }

  protected onOperationAreaChange(code: string | null): void {
    this.operationAreaCode.set(code);
    this.emitLocation();
  }

  protected addScope(): void {
    if (!this.canAdd()) {
      return;
    }

    const level = this.selectedLevel();
    const code = this.selectedCode();
    const department = this.departmentId();
    const hasTerritory = !!level && !!code;

    const covered = hasTerritory
      ? this.scopes().filter(scope => !this.isCoveredBy(scope, level!, code!, department))
      : this.scopes();

    this.scopes.set([
      ...covered,
      {
        level: hasTerritory ? level! : undefined,
        code: hasTerritory ? code! : undefined,
        departmentId: department ?? undefined,
      },
    ]);

    if (hasTerritory) {
      this.rememberLabel(level!, code!);
    }

    this.resetSelection();
    this.scopesChange.emit(this.scopes());
  }

  protected removeScope(scope: OrgScopeAssignment): void {
    this.scopes.set(this.scopes().filter(item => !this.isSameScope(item, scope)));
    this.scopesChange.emit(this.scopes());
  }

  protected scopeLabel(scope: OrgScopeAssignment): string {
    const parts: string[] = [];

    if (scope.level && scope.code) {
      const level = this.translate.instant(
        LEVEL_LABEL_KEYS[scope.level as OrgScopeLevel] ?? 'org.cluster',
      );
      const name = this.scopeLabels().get(this.labelKey(scope.level, scope.code));
      parts.push(`${level}: ${name ?? scope.code}`);
    } else {
      parts.push(this.translate.instant('org.everywhere'));
    }

    parts.push(
      scope.departmentId == null
        ? this.translate.instant('org.allDepartments')
        : (this.departmentLabels().get(scope.departmentId) ?? `#${scope.departmentId}`),
    );

    return parts.join(' · ');
  }

  protected scopeKey(scope: OrgScopeAssignment): string {
    return `${scope.level ?? ''}|${scope.code ?? ''}|${scope.departmentId ?? ''}`;
  }

  reset(): void {
    this.resetSelection();
    this.scopes.set([]);
  }

  private isSameScope(left: OrgScopeAssignment, right: OrgScopeAssignment): boolean {
    return (left.level ?? null) === (right.level ?? null)
      && (left.code ?? null) === (right.code ?? null)
      && (left.departmentId ?? null) === (right.departmentId ?? null);
  }

  private resetSelection(): void {
    this.clusterCode.set(null);
    this.cbuCode.set(null);
    this.branchCode.set(null);
    this.operationAreaCode.set(null);
    this.departmentId.set(null);
    this.cbus.set([]);
    this.branches.set([]);
    this.operationAreas.set([]);
  }

  private emitLocation(): void {
    this.locationChange.emit({
      clusterCode: this.clusterCode(),
      cbuCode: this.cbuCode(),
      branchCode: this.branchCode(),
      operationAreaCode: this.operationAreaCode(),
    });
  }

  private async seedFromLocation(location: OrgLocation): Promise<void> {
    let clusterCode = location.clusterCode ?? null;

    if (!clusterCode && location.cbuCode) {
      const allCbus = await firstValueFrom(this.lookups.listAll('Cbu')).catch(() => []);
      clusterCode = allCbus.find(cbu => cbu.code === location.cbuCode)?.parentCode ?? null;
    }

    this.clusterCode.set(clusterCode);
    this.cbuCode.set(location.cbuCode ?? null);
    this.branchCode.set(location.branchCode ?? null);
    this.operationAreaCode.set(location.operationAreaCode ?? null);

    if (clusterCode) {
      this.loadCbus(clusterCode);
    }

    if (location.cbuCode) {
      this.loadBranches(location.cbuCode);
      this.loadOperationAreas(location.cbuCode);
    }
  }

  private loadClusters(): void {
    this.loadingClusters.set(true);
    this.lookups.listAll('Cluster', { isActive: true })
      .pipe(finalize(() => this.loadingClusters.set(false)))
      .subscribe({
        next: items => this.clusters.set(this.toOptions(items)),
        error: () => this.clusters.set([]),
      });
  }

  private loadCbus(clusterCode: string | null): void {
    if (!clusterCode) {
      this.cbus.set([]);
      return;
    }

    this.loadingCbus.set(true);
    this.lookups.listAll('Cbu', { parentCode: clusterCode, isActive: true })
      .pipe(finalize(() => this.loadingCbus.set(false)))
      .subscribe({
        next: items => this.cbus.set(this.toOptions(items)),
        error: () => this.cbus.set([]),
      });
  }

  private loadBranches(cbuCode: string | null): void {
    if (!cbuCode) {
      this.branches.set([]);
      return;
    }

    this.loadingBranches.set(true);
    this.lookups.listAll('Branch', { parentCode: cbuCode, isActive: true })
      .pipe(finalize(() => this.loadingBranches.set(false)))
      .subscribe({
        next: items => this.branches.set(this.toOptions(items)),
        error: () => this.branches.set([]),
      });
  }

  private loadOperationAreas(cbuCode: string | null): void {
    if (!cbuCode) {
      this.operationAreas.set([]);
      return;
    }

    this.loadingOperationAreas.set(true);
    this.lookups.listAll('OperationArea', { parentCode: cbuCode, isActive: true })
      .pipe(finalize(() => this.loadingOperationAreas.set(false)))
      .subscribe({
        next: items => this.operationAreas.set(this.toOptions(items)),
        error: () => this.operationAreas.set([]),
      });
  }

  private loadDepartments(): void {
    this.loadingDepartments.set(true);
    this.lookups.listAll('Department', { isActive: true })
      .pipe(finalize(() => this.loadingDepartments.set(false)))
      .subscribe({
        next: items => {
          this.departments.set(this.toDepartmentOptions(items));
          const labels = new Map(untracked(this.departmentLabels));
          for (const item of items) {
            labels.set(item.code, this.localizedName(item.nameEn, item.nameAr) || item.code);
          }
          this.departmentLabels.set(labels);
        },
        error: () => this.departments.set([]),
      });
  }

  private loadScopeLabels(): void {
    const merge = (level: OrgScopeLevel, items: { code: string; nameEn: string; nameAr: string }[]) => {
      const labels = new Map(untracked(this.scopeLabels));
      for (const item of items) {
        labels.set(this.labelKey(level, item.code), this.localizedName(item.nameEn, item.nameAr) || item.code);
      }
      this.scopeLabels.set(labels);
    };

    this.lookups.listAll('Cluster').subscribe({ next: items => merge(ORG_SCOPE_LEVELS.cluster, items), error: () => undefined });
    this.lookups.listAll('Cbu').subscribe({ next: items => merge(ORG_SCOPE_LEVELS.cbu, items), error: () => undefined });
    this.lookups.listAll('Branch').subscribe({ next: items => merge(ORG_SCOPE_LEVELS.branch, items), error: () => undefined });
    this.lookups.listAll('OperationArea').subscribe({ next: items => merge(ORG_SCOPE_LEVELS.operationArea, items), error: () => undefined });
  }

  private rememberLabel(level: OrgScopeLevel, code: string): void {
    const label = this.optionsFor(level).find(option => option.value === code)?.label;
    if (!label) return;
    const labels = new Map(untracked(this.scopeLabels));
    labels.set(this.labelKey(level, code), label);
    this.scopeLabels.set(labels);
  }

  private optionsFor(level: OrgScopeLevel): SelectOption[] {
    switch (level) {
      case ORG_SCOPE_LEVELS.cluster: return this.clusters();
      case ORG_SCOPE_LEVELS.cbu: return this.cbus();
      case ORG_SCOPE_LEVELS.branch: return this.branches();
      default: return this.operationAreas();
    }
  }

  private isCoveredBy(
    scope: OrgScopeAssignment,
    level: OrgScopeLevel,
    code: string,
    departmentId: string | null,
  ): boolean {
    if ((scope.departmentId ?? null) !== departmentId) {
      return false;
    }
    if (!scope.level || !scope.code) {
      return false;
    }
    if (!isLevelUnder(scope.level as OrgScopeLevel, level)) {
      return false;
    }

    const selectedPath: Record<string, string | null> = {
      [ORG_SCOPE_LEVELS.cluster]: this.clusterCode(),
      [ORG_SCOPE_LEVELS.cbu]: this.cbuCode(),
      [ORG_SCOPE_LEVELS.branch]: this.branchCode(),
      [ORG_SCOPE_LEVELS.operationArea]: this.operationAreaCode(),
    };

    return selectedPath[scope.level ?? ''] === scope.code && code === selectedPath[level];
  }

  private labelKey(level: string | undefined, code: string | undefined): string {
    return `${level ?? ''}|${(code ?? '').toUpperCase()}`;
  }

  private toOptions(items: { code: string; nameEn: string; nameAr: string }[]): SelectOption[] {
    return items.map(item => ({
      label: this.optionLabel(item.code, item.nameEn, item.nameAr),
      value: item.code,
    }));
  }

  private toDepartmentOptions(items: { code: string; nameEn: string; nameAr: string }[]): SelectOption[] {
    return items.map(item => ({
      label: this.localizedName(item.nameEn, item.nameAr) || item.code,
      value: item.code,
    }));
  }

  private optionLabel(code?: string, nameEn?: string, nameAr?: string): string {
    const name = this.localizedName(nameEn, nameAr);
    return name ? `${code} — ${name}` : (code ?? '');
  }

  private localizedName(nameEn?: string, nameAr?: string): string {
    return this.locale.locale() === 'ar'
      ? (nameAr || nameEn || '')
      : (nameEn || nameAr || '');
  }

  protected readonly emptyLocation = EMPTY_ORG_LOCATION;
}
