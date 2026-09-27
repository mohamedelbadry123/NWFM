import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { HasPermissionDirective, PERMISSIONS } from '../../../core/auth/permissions';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { OrgNamesService } from '../../../core/lookups/org-names.service';
import { Team, TeamsService } from '../../../core/teams/teams.service';
import { LEVEL_LABEL_KEYS, OrgScopeAssignment, OrgScopeLevel } from '../../../shared/components/org-scope/org-scope.model';
import { TeamDialogComponent } from './team-dialog.component';

interface FilterOption {
  readonly label: string;
  readonly value: boolean;
}

/** Field teams: the crews tasks are handed to, their logins, and the territory each works. */
@Component({
  selector: 'app-team-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateContextDirective,
    ButtonModule,
    ConfirmDialogModule,
    DialogModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    PasswordModule,
    SelectModule,
    TableModule,
    TagModule,
    ToastModule,
    TooltipModule,
    HasPermissionDirective,
    TeamDialogComponent,
  ],
  providers: [MessageService, ConfirmationService],
  template: `
    <ng-container *translateContext="let t">
      <p-toast />
      <p-confirmDialog />

      <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
        <ng-container *hasPermission="PERMISSIONS.manageTeams">
          <p-button [label]="t('teams.new')" icon="pi pi-plus" (onClick)="openNew()" />
        </ng-container>
      </div>

      <div class="card p-4">
        <p-table
          [value]="teams()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [rows]="10"
          [paginator]="true"
          [totalRecords]="totalRecords()"
          [loading]="loading()"
          [rowsPerPageOptions]="[10, 25, 50]"
          styleClass="app-table p-datatable-sm p-datatable-striped"
          [scrollable]="true"
          [tableStyle]="{ 'min-width': '60rem' }"
          [rowHover]="true"
          [showCurrentPageReport]="true"
          [currentPageReportTemplate]="t('common.pageReport')"
        >
          <ng-template pTemplate="caption">
            <div class="flex flex-wrap items-center gap-2.5 p-1">
              <p-iconfield iconPosition="left" class="w-full sm:w-72">
                <p-inputicon styleClass="pi pi-search" />
                <input pInputText type="text" [(ngModel)]="search" (keyup.enter)="reload()" [placeholder]="t('teams.searchPlaceholder')" class="w-full" />
              </p-iconfield>
              <p-select
                [(ngModel)]="activeFilter"
                [options]="statusOptions()"
                optionLabel="label"
                optionValue="value"
                [showClear]="true"
                [placeholder]="t('teams.status')"
                (onChange)="reload()"
                appendTo="body"
                styleClass="w-full sm:w-40"
              />
              <p-button [label]="t('common.apply')" icon="pi pi-filter" severity="secondary" [outlined]="true" (onClick)="reload()" />
            </div>
          </ng-template>

          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('teams.name') }}</th>
              <th>{{ t('teams.userCode') }}</th>
              <th>{{ t('teams.mobile') }}</th>
              <th class="app-col-wrap">{{ t('teams.territory') }}</th>
              <th>{{ t('teams.status') }}</th>
              <th>{{ t('teams.lastActive') }}</th>
              <th class="w-36 text-end">{{ t('common.actions') }}</th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-team>
            <tr>
              <td class="font-medium">{{ team.name }}</td>
              <td class="font-mono text-sm">{{ team.userCode || '—' }}</td>
              <td dir="ltr">{{ team.mobile || '—' }}</td>
              <td class="app-col-wrap max-w-[22rem]">
                <div class="flex flex-wrap gap-1">
                  @for (scope of team.scopes; track $index) {
                    <span class="app-badge app-badge--neutral">{{ scopeLabel(scope) }}</span>
                  } @empty {
                    <p-tag [value]="t('teams.noScope')" severity="warn" />
                  }
                </div>
              </td>
              <td>
                <p-tag [value]="t(team.isActive ? 'common.active' : 'common.inactive')" [severity]="team.isActive ? 'success' : 'secondary'" />
              </td>
              <td>{{ team.lastActiveAt ? (team.lastActiveAt | date: 'yyyy-MM-dd HH:mm') : '—' }}</td>
              <td class="text-end">
                <ng-container *hasPermission="PERMISSIONS.manageTeams">
                  <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" severity="secondary" [pTooltip]="t('common.edit')" (onClick)="openEdit(team)" />
                  <p-button
                    icon="pi pi-key"
                    [rounded]="true"
                    [text]="true"
                    severity="secondary"
                    [disabled]="!team.userCode"
                    [pTooltip]="t('users.resetPassword')"
                    (onClick)="openReset(team)"
                  />
                  <p-button
                    [icon]="team.isActive ? 'pi pi-ban' : 'pi pi-check-circle'"
                    [rounded]="true"
                    [text]="true"
                    [severity]="team.isActive ? 'danger' : 'success'"
                    [loading]="busyId() === team.id"
                    [pTooltip]="t(team.isActive ? 'teams.deactivate' : 'teams.activate')"
                    (onClick)="toggleStatus(team)"
                  />
                </ng-container>
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr><td colspan="7" class="p-6 text-center text-surface-500">{{ t('teams.empty') }}</td></tr>
          </ng-template>
        </p-table>
      </div>

      <app-team-dialog [(visible)]="dialogVisible" [team]="selected()" (saved)="load()" />

      <p-dialog
        [visible]="resetVisible()"
        (visibleChange)="resetVisible.set($event)"
        [header]="t('users.resetPassword')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '26rem' }"
      >
        <p class="mb-3 text-sm text-surface-500">{{ t('teams.resetHint', { code: selected()?.userCode }) }}</p>
        <p-password [(ngModel)]="newPassword" [feedback]="false" [toggleMask]="true" styleClass="w-full" inputStyleClass="w-full" autocomplete="new-password" />
        @if (newPassword && newPassword.length < 8) {
          <small class="text-red-500">{{ t('teams.passwordTooShort') }}</small>
        }
        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="resetVisible.set(false)" />
          <p-button [label]="t('users.resetPassword')" icon="pi pi-key" [loading]="resetting()" [disabled]="!newPassword || newPassword.length < 8" (onClick)="resetPassword()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TeamListComponent implements OnInit {
  private readonly api = inject(TeamsService);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly orgNames = inject(OrgNamesService);

  protected readonly PERMISSIONS = PERMISSIONS;
  protected readonly teams = signal<Team[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly dialogVisible = signal(false);
  protected readonly resetVisible = signal(false);
  protected readonly resetting = signal(false);
  protected readonly selected = signal<Team | null>(null);

  protected readonly statusOptions = computed<FilterOption[]>(() => [
    { label: this.translate.instant('common.active'), value: true },
    { label: this.translate.instant('common.inactive'), value: false },
  ]);

  protected search = '';
  protected activeFilter: boolean | null = null;
  protected newPassword = '';
  private page = 1;
  private pageSize = 10;

  ngOnInit(): void {
    this.orgNames.ensureLoaded();
  }

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    this.loading.set(true);
    this.api
      .list(this.page, this.pageSize, this.search.trim() || null, this.activeFilter)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (res) => {
          this.teams.set(res.value?.items ?? []);
          this.totalRecords.set(res.value?.totalCount ?? 0);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  protected reload(): void {
    this.page = 1;
    this.load();
  }

  /** `Branch: R-16 — Al Moraba · 10 — Water`, or as much of it as the row names. */
  protected scopeLabel(scope: OrgScopeAssignment): string {
    const parts: string[] = [];
    const level = scope.level as OrgScopeLevel | null | undefined;

    if (level && scope.code) {
      // Scope levels and lookup types share their names, so the level names its own table.
      const levelLabel = this.translate.instant(LEVEL_LABEL_KEYS[level]);
      parts.push(`${levelLabel}: ${this.orgNames.label(level, scope.code)}`);
    } else {
      parts.push(this.translate.instant('org.everywhere'));
    }

    if (scope.departmentId) {
      parts.push(this.orgNames.label('Department', scope.departmentId));
    }

    return parts.join(' · ');
  }

  protected openNew(): void {
    this.selected.set(null);
    this.dialogVisible.set(true);
  }

  protected openEdit(team: Team): void {
    this.selected.set(team);
    this.dialogVisible.set(true);
  }

  protected openReset(team: Team): void {
    this.selected.set(team);
    this.newPassword = '';
    this.resetVisible.set(true);
  }

  protected resetPassword(): void {
    const team = this.selected();
    if (!team || this.newPassword.length < 8) {
      return;
    }

    this.resetting.set(true);
    this.api
      .resetPassword(team.id, this.newPassword)
      .pipe(finalize(() => this.resetting.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: this.translate.instant('users.passwordReset') });
          this.resetVisible.set(false);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  protected toggleStatus(team: Team): void {
    const activate = !team.isActive;

    this.confirm.confirm({
      header: this.translate.instant('common.confirm'),
      message: this.translate.instant(activate ? 'teams.confirmActivate' : 'teams.confirmDeactivate', { name: team.name }),
      accept: () => {
        this.busyId.set(team.id);
        this.api
          .setStatus(team.id, activate)
          .pipe(finalize(() => this.busyId.set(null)))
          .subscribe({
            next: () => this.load(),
            error: (error: unknown) => this.fail(error),
          });
      },
    });
  }

  private fail(error: unknown): void {
    this.messages.add({
      severity: 'error',
      summary: this.translate.instant('common.error'),
      detail: apiErrorMessage(error, this.translate),
      life: 8000,
    });
  }
}
