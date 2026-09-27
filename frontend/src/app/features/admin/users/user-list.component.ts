import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';
import { HasPermissionDirective, PERMISSIONS } from '../../../core/auth/permissions';
import { UserListItem, UsersService } from '../../../core/users/users.service';
import { UserFormDialogComponent } from './modals/user-form-dialog.component';
import { UserResetPasswordDialogComponent } from './modals/user-reset-password-dialog.component';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TranslateModule,
    ButtonModule, ConfirmDialogModule, InputTextModule, SelectModule, TableModule, TagModule, ToastModule, TooltipModule,
    HasPermissionDirective, UserFormDialogComponent, UserResetPasswordDialogComponent,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './user-list.component.html',
})
export class UserListComponent {
  private readonly usersApi = inject(UsersService);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly PERMISSIONS = PERMISSIONS;

  protected readonly users = signal<UserListItem[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyUserId = signal<string | null>(null);
  protected searchTerm = '';
  protected statusFilter: boolean | null = null;
  protected readonly formVisible = signal(false);
  protected readonly resetVisible = signal(false);
  protected readonly selectedUser = signal<UserListItem | null>(null);
  protected readonly statusOptions = signal<{ label: string; value: boolean }[]>([]);

  private page = 1;
  private pageSize = 10;

  constructor() {
    this.buildStatusOptions();
    this.translate.onLangChange.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.buildStatusOptions());
  }

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.page = Math.floor((event.first ?? 0) / (event.rows ?? this.pageSize)) + 1;
      this.pageSize = event.rows ?? this.pageSize;
    }
    this.loading.set(true);
    this.usersApi.list(this.page, this.pageSize, this.searchTerm || undefined, this.statusFilter).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.isSuccess && res.value) {
          this.users.set(res.value.items);
          this.totalRecords.set(res.value.totalCount);
        }
      },
      error: () => this.loading.set(false),
    });
  }

  protected onSearch(): void {
    this.page = 1;
    this.load();
  }

  protected clearSearch(): void {
    this.searchTerm = '';
    this.onSearch();
  }

  protected openNew(): void {
    this.selectedUser.set(null);
    this.formVisible.set(true);
  }

  protected openEdit(user: UserListItem): void {
    this.selectedUser.set(user);
    this.formVisible.set(true);
  }

  protected openReset(user: UserListItem): void {
    this.selectedUser.set(user);
    this.resetVisible.set(true);
  }

  protected toggleStatus(user: UserListItem): void {
    this.confirm.confirm({
      message: this.translate.instant(user.isEnabled ? 'users.confirmDisable' : 'users.confirmEnable', { name: user.userName }),
      accept: () => {
        this.busyUserId.set(user.id);
        this.usersApi.setStatus(user.id, !user.isEnabled).subscribe({
          next: (res) => {
            this.busyUserId.set(null);
            if (res.isSuccess) this.load();
            else this.messages.add({ severity: 'error', summary: res.error?.message });
          },
          error: () => this.busyUserId.set(null),
        });
      },
    });
  }

  private buildStatusOptions(): void {
    this.statusOptions.set([
      { label: this.translate.instant('users.enabled'), value: true },
      { label: this.translate.instant('users.disabled'), value: false },
    ]);
  }
}
