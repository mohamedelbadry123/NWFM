import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { HelpTipComponent } from './help-tip.component';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformDatabaseServer } from '../../core/api/platform-api.service';
import { PlatformDatabaseServerDialogComponent } from './modals/platform-database-server-dialog.component';

/** SQL Servers that managed workspaces are created on. Admin only; the list is small, so it is not paged. */
@Component({
  selector: 'app-platform-database-servers',
  imports: [
    DatePipe,
    I18nDirective,
    ButtonModule,
    MessageModule,
    TableModule,
    TagModule,
    TooltipModule,
    HelpTipComponent,
    PlatformDatabaseServerDialogComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      @if (!loading() && servers().length === 0) {
        <p-message severity="warn" styleClass="mb-3">{{
          t('platform.databaseServers.noneWarning')
        }}</p-message>
      }
      <div class="card p-4">
        <p-table [value]="servers()" [loading]="loading()" [tableStyle]="{ 'min-width': '48rem' }">
          <ng-template #caption>
            <div class="flex justify-end">
              <p-button
                [label]="t('platform.databaseServers.add')"
                icon="pi pi-plus"
                [pTooltip]="t('platform.tips.addServer')"
                tooltipPosition="bottom"
                (onClick)="open(null)"
              />
            </div>
          </ng-template>
          <ng-template #header>
            <tr>
              <th>{{ t('platform.databaseServers.name') }}</th>
              <th>
                {{ t('platform.databaseServers.usage')
                }}<app-help-tip [text]="t('platform.tips.serverLimit')" />
              </th>
              <th>
                {{ t('platform.databaseServers.containedUsers')
                }}<app-help-tip [text]="t('platform.tips.containedUsers')" />
              </th>
              <th>
                {{ t('platform.tenants.status')
                }}<app-help-tip [text]="t('platform.tips.serverActive')" />
              </th>
              <th>{{ t('platform.tenants.created') }}</th>
              <th>{{ t('common.actions') }}</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td>{{ row.name }}</td>
              <td>{{ row.databaseCount }} / {{ row.maxDatabases }}</td>
              <td>{{ row.useContainedUsers ? t('common.yes') : t('common.no') }}</td>
              <td>
                <p-tag
                  [value]="
                    row.isActive
                      ? t('platform.databaseServers.active')
                      : t('platform.databaseServers.inactive')
                  "
                  [severity]="row.isActive ? 'success' : 'secondary'"
                />
              </td>
              <td>{{ row.createdAtUtc | date: 'mediumDate' }}</td>
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
              <td colspan="6" class="text-center p-4">{{ t('platform.databaseServers.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      <app-platform-database-server-dialog
        [(visible)]="dialogOpen"
        [server]="editing()"
        (saved)="reload()"
      />
    </ng-container>
  `,
})
export class PlatformDatabaseServersComponent implements OnInit {
  private readonly api = inject(PlatformApiService);
  protected readonly servers = signal<PlatformDatabaseServer[]>([]);
  protected readonly loading = signal(false);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<PlatformDatabaseServer | null>(null);

  ngOnInit(): void {
    this.reload();
  }

  protected open(server: PlatformDatabaseServer | null): void {
    this.editing.set(server);
    this.dialogOpen.set(true);
  }

  protected reload(): void {
    this.loading.set(true);
    this.api
      .getDatabaseServers()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (servers) => this.servers.set([...servers]) });
  }
}
