import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { HelpTipComponent } from './help-tip.component';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformUser } from '../../core/api/platform-api.service';
import { PlatformUserDialogComponent } from './modals/platform-user-dialog.component';

/** NWFM staff accounts (admin and support). Admin only; the list is small, so it is not paged. */
@Component({
  selector: 'app-platform-users',
  imports: [
    DatePipe,
    I18nDirective,
    ButtonModule,
    TableModule,
    TagModule,
    TooltipModule,
    HelpTipComponent,
    PlatformUserDialogComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <div class="card p-4">
        <p-table [value]="users()" [loading]="loading()" [tableStyle]="{ 'min-width': '52rem' }">
          <ng-template #caption>
            <div class="flex justify-end">
              <p-button
                [label]="t('platform.users.add')"
                icon="pi pi-plus"
                [pTooltip]="t('platform.tips.addStaff')"
                tooltipPosition="bottom"
                (onClick)="open(null)"
              />
            </div>
          </ng-template>
          <ng-template #header>
            <tr>
              <th>{{ t('platform.users.displayName') }}</th>
              <th>{{ t('platform.login.email') }}</th>
              <th>
                {{ t('platform.users.role') }}<app-help-tip [text]="t('platform.tips.staffRole')" />
              </th>
              <th>
                {{ t('platform.tenants.status')
                }}<app-help-tip [text]="t('platform.tips.staffActive')" />
              </th>
              <th>{{ t('platform.users.lastLogin') }}</th>
              <th>{{ t('common.actions') }}</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td>{{ row.displayName }}</td>
              <td>{{ row.email }}</td>
              <td>{{ t('platform.users.roles.' + row.role) }}</td>
              <td>
                <p-tag
                  [value]="row.isActive ? t('platform.users.active') : t('platform.users.inactive')"
                  [severity]="row.isActive ? 'success' : 'secondary'"
                />
              </td>
              <td>{{ (row.lastLoginAtUtc | date: 'short') || '—' }}</td>
              <td>
                <p-button
                  [label]="t('common.edit')"
                  size="small"
                  [text]="true"
                  icon="pi pi-pencil"
                  (onClick)="open(row)"
                />
              </td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr>
              <td colspan="6" class="text-center p-4">{{ t('platform.users.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      <app-platform-user-dialog [(visible)]="dialogOpen" [user]="editing()" (saved)="reload()" />
    </ng-container>
  `,
})
export class PlatformUsersComponent implements OnInit {
  private readonly api = inject(PlatformApiService);
  protected readonly users = signal<PlatformUser[]>([]);
  protected readonly loading = signal(false);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<PlatformUser | null>(null);

  ngOnInit(): void {
    this.reload();
  }

  protected open(user: PlatformUser | null): void {
    this.editing.set(user);
    this.dialogOpen.set(true);
  }

  protected reload(): void {
    this.loading.set(true);
    this.api
      .getUsers()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (users) => this.users.set([...users]) });
  }
}
