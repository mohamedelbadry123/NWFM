import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { forkJoin } from 'rxjs';
import { HasPermissionDirective, ADMINISTRATOR_ROLE, PERMISSIONS } from '../../../core/auth/permissions';
import { AdminService, PermissionDto, RoleDto } from '../../../core/admin/admin.service';
import { LocaleService } from '../../../core/i18n/locale.service';

interface PermissionRow {
  readonly code: string;
  readonly module: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

@Component({
  selector: 'app-role-permissions',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TranslateModule,
    ButtonModule, CheckboxModule, TableModule, TagModule, ToastModule, HasPermissionDirective,
  ],
  providers: [MessageService],
  templateUrl: './role-permissions.component.html',
})
export class RolePermissionsComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly locale = inject(LocaleService);
  protected readonly PERMISSIONS = PERMISSIONS;

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly roles = signal<RoleDto[]>([]);
  protected readonly rows = signal<PermissionRow[]>([]);
  private readonly grants = signal<Record<string, ReadonlySet<string>>>({});
  private readonly baseline = signal<Record<string, ReadonlySet<string>>>({});

  protected readonly dirtyRoles = computed(() => {
    const current = this.grants();
    const original = this.baseline();
    return Object.keys(current).filter((roleName) => {
      const now = current[roleName];
      const before = original[roleName] ?? new Set<string>();
      return now.size !== before.size || [...now].some((code) => !before.has(code));
    });
  });

  protected readonly hasChanges = computed(() => this.dirtyRoles().length > 0);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    forkJoin({
      roles: this.admin.getRoles(),
      permissions: this.admin.getPermissions(),
    }).subscribe({
      next: ({ roles, permissions }) => {
        this.loading.set(false);
        const roleList = roles.value ?? [];
        const permissionList = permissions.value ?? [];
        this.roles.set(roleList);
        this.rows.set(permissionList.map(p => ({
          code: p.code, module: p.module, nameEn: p.nameEn, nameAr: p.nameAr,
        })));
        const next: Record<string, Set<string>> = {};
        for (const role of roleList) {
          next[role.name] = new Set(role.permissionCodes ?? []);
        }
        this.grants.set(next);
        this.baseline.set(structuredClone(next));
      },
      error: () => this.loading.set(false),
    });
  }

  protected isGranted(roleName: string, code: string): boolean {
    return this.grants()[roleName]?.has(code) ?? false;
  }

  protected isSystem(role: RoleDto): boolean {
    return role.name === ADMINISTRATOR_ROLE;
  }

  protected toggle(roleName: string, code: string, checked: boolean): void {
    this.grants.update((current) => {
      const next = { ...current };
      const set = new Set(next[roleName] ?? []);
      if (checked) set.add(code);
      else set.delete(code);
      next[roleName] = set;
      return next;
    });
  }

  protected reset(): void {
    this.grants.set(structuredClone(this.baseline()));
  }

  protected label(row: PermissionRow): string {
    return this.locale.isRtl() ? row.nameAr : row.nameEn;
  }

  protected save(): void {
    const dirty = this.dirtyRoles();
    if (!dirty.length) return;
    this.saving.set(true);
    const requests = dirty.map((roleName) =>
      this.admin.assignRolePermissions(roleName, [...(this.grants()[roleName] ?? [])])
    );
    forkJoin(requests).subscribe({
      next: () => {
        this.saving.set(false);
        this.messages.add({ severity: 'success', summary: this.translate.instant('common.save') });
        this.load();
      },
      error: () => this.saving.set(false),
    });
  }
}
