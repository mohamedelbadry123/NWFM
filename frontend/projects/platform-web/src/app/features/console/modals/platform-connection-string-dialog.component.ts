import { Component, effect, inject, input, model, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformTenantDetail } from '../../../core/api/platform-api.service';

/**
 * Reveals the full tenant connection string, password included, for hands-on support. The API
 * requires a reason and audits every reveal; the string is dropped as soon as the dialog closes.
 */
@Component({
  selector: 'app-platform-connection-string-dialog',
  imports: [FormsModule, I18nDirective, ButtonModule, Dialog, TextareaModule],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!revealing()"
        [header]="t('platform.connection.revealTitle')"
        [style]="{ width: '40rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        @if (connectionString(); as full) {
          <div class="flex flex-col gap-2">
            <textarea
              pTextarea
              readonly
              rows="4"
              class="w-full font-mono"
              dir="ltr"
              [value]="full"
            ></textarea>
          </div>
        } @else {
          <div class="flex flex-col gap-3">
            <p class="muted m-0">{{ t('platform.connection.revealHint') }}</p>
            <div class="flex flex-col gap-1">
              <label for="revealReason">{{ t('platform.connection.reason') }}</label>
              <textarea
                pTextarea
                id="revealReason"
                [(ngModel)]="reason"
                rows="2"
                class="w-full"
              ></textarea>
            </div>
          </div>
        }
        <ng-template #footer>
          <p-button
            [label]="t('common.close')"
            [outlined]="true"
            [disabled]="revealing()"
            (onClick)="visible.set(false)"
          />
          @if (connectionString()) {
            <p-button
              [label]="t('platform.connection.copy')"
              icon="pi pi-copy"
              (onClick)="copy()"
            />
          } @else {
            <p-button
              [label]="t('platform.connection.revealSubmit')"
              icon="pi pi-eye"
              severity="warn"
              [loading]="revealing()"
              [disabled]="revealing() || !reason.trim()"
              (onClick)="reveal()"
            />
          }
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformConnectionStringDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();

  protected readonly revealing = signal(false);
  protected readonly connectionString = signal<string | null>(null);
  protected reason = '';

  constructor() {
    // Opening starts clean and closing forgets the secret, so it never lingers in the page.
    effect(() => {
      const open = this.visible();
      untracked(() => {
        this.connectionString.set(null);
        if (open) {
          this.reason = '';
        }
      });
    });
  }

  protected reveal(): void {
    if (this.revealing() || !this.reason.trim()) {
      return;
    }
    this.revealing.set(true);
    this.api
      .revealTenantConnection(this.tenant().id, this.reason.trim())
      .pipe(finalize(() => this.revealing.set(false)))
      .subscribe({
        next: (connection) => this.connectionString.set(connection.full ?? null),
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  protected copy(): void {
    const value = this.connectionString();
    if (!value) {
      return;
    }
    void navigator.clipboard.writeText(value).then(() =>
      this.messages.add({
        severity: 'success',
        summary: this.translate.instant('platform.connection.copied'),
      }),
    );
  }
}
