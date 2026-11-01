import { Component, effect, inject, input, model, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  ImpersonationCandidate,
  PlatformApiService,
  PlatformTenantDetail,
} from '../../../core/api/platform-api.service';
import { HelpTipComponent } from '../help-tip.component';

/**
 * Opens a consented support session inside the workspace, in a new tab on the workspace's own host.
 * Needs an active grant from the tenant; the API refuses otherwise. The console stays signed in.
 */
@Component({
  selector: 'app-platform-support-session-dialog',
  imports: [FormsModule, I18nDirective, ButtonModule, Dialog, InputText, Select, HelpTipComponent],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!starting()"
        [header]="t('platform.support.start')"
        [style]="{ width: '32rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <div class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label for="supportTicket"
              >{{ t('platform.support.ticket')
              }}<app-help-tip [text]="t('platform.tips.supportTicket')"
            /></label>
            <input pInputText id="supportTicket" [(ngModel)]="ticketReference" />
          </div>
          <div class="flex flex-col gap-1">
            <label
              >{{ t('platform.support.asUser')
              }}<app-help-tip [text]="t('platform.tips.supportAsUser')"
            /></label>
            <p-select
              [(ngModel)]="impersonatedUserId"
              [options]="users()"
              optionLabel="userName"
              optionValue="userId"
              [loading]="loadingUsers()"
              [filter]="true"
              [showClear]="true"
              appendTo="body"
              styleClass="w-full"
            />
          </div>
        </div>
        <ng-template #footer>
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="starting()"
            (onClick)="visible.set(false)"
          />
          <p-button
            [label]="t('platform.support.start')"
            icon="pi pi-sign-in"
            [loading]="starting()"
            [disabled]="starting() || !ticketReference.trim()"
            (onClick)="start()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformSupportSessionDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();

  protected readonly users = signal<ImpersonationCandidate[]>([]);
  protected readonly loadingUsers = signal(false);
  protected readonly starting = signal(false);
  protected ticketReference = '';
  protected impersonatedUserId: string | null = null;

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.open());
      }
    });
  }

  protected start(): void {
    const tenant = this.tenant();
    if (this.starting() || !this.ticketReference.trim()) {
      return;
    }
    this.starting.set(true);
    this.api
      .startSupportSession(
        tenant.id,
        this.ticketReference.trim(),
        this.impersonatedUserId ?? undefined,
      )
      .pipe(finalize(() => this.starting.set(false)))
      .subscribe({
        next: (handoff) => {
          this.visible.set(false);
          // The code in the URL fragment is single-use and short-lived; the tenant app spends it
          // as soon as the page loads. noopener keeps the workspace tab from reaching this one.
          window.open(handoff.handoffUrl, '_blank', 'noopener');
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private open(): void {
    this.ticketReference = '';
    this.impersonatedUserId = null;
    this.loadingUsers.set(true);
    this.api
      .getTenantUsers(this.tenant().id)
      .pipe(finalize(() => this.loadingUsers.set(false)))
      .subscribe({ next: (users) => this.users.set([...users]) });
  }
}
