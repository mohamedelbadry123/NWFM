import {
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformDatabaseServer } from '../../../core/api/platform-api.service';
import { HelpTipComponent } from '../help-tip.component';

const DEFAULT_MAX_DATABASES = 100;

/**
 * Registers or edits a SQL Server that managed workspaces are created on. The admin connection
 * string is write-only: the API never returns it, so an edit leaves it empty to keep the stored one.
 */
@Component({
  selector: 'app-platform-database-server-dialog',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Checkbox,
    Dialog,
    InputNumberModule,
    InputText,
    Password,
    HelpTipComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t(isNew() ? 'platform.databaseServers.add' : 'platform.databaseServers.edit')"
        [style]="{ width: '38rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <form [formGroup]="form" (ngSubmit)="save()" class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label for="dbsName">{{ t('platform.databaseServers.name') }}</label>
            <input pInputText id="dbsName" formControlName="name" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="dbsConnection"
              >{{ t('platform.databaseServers.connectionString')
              }}<app-help-tip [text]="t('platform.tips.serverConnection')"
            /></label>
            <p-password
              inputId="dbsConnection"
              formControlName="adminConnectionString"
              [feedback]="false"
              [toggleMask]="true"
              autocomplete="off"
              styleClass="w-full"
              inputStyleClass="w-full"
            />
            <small class="text-muted-color">
              {{
                t(
                  isNew()
                    ? 'platform.databaseServers.connectionStringHint'
                    : 'platform.databaseServers.connectionStringKeep'
                )
              }}
            </small>
          </div>
          <div class="flex flex-col gap-1">
            <label for="dbsMax"
              >{{ t('platform.databaseServers.maxDatabases')
              }}<app-help-tip [text]="t('platform.tips.serverLimit')"
            /></label>
            <p-inputNumber
              inputId="dbsMax"
              formControlName="maxDatabases"
              [min]="1"
              [max]="10000"
              styleClass="w-full"
            />
          </div>
          <div class="flex items-center gap-2">
            <p-checkbox
              inputId="dbsContained"
              formControlName="useContainedUsers"
              [binary]="true"
            />
            <label for="dbsContained">{{ t('platform.databaseServers.containedUsers') }}</label>
            <app-help-tip [text]="t('platform.tips.containedUsers')" />
          </div>
          <div class="flex items-center gap-2">
            <p-checkbox inputId="dbsActive" formControlName="isActive" [binary]="true" />
            <label for="dbsActive">{{ t('platform.databaseServers.active') }}</label>
            <app-help-tip [text]="t('platform.tips.serverActive')" />
          </div>
        </form>
        <ng-template #footer>
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="saving()"
            (onClick)="visible.set(false)"
          />
          <p-button
            [label]="t('common.save')"
            icon="pi pi-check"
            [loading]="saving()"
            [disabled]="saving()"
            (onClick)="save()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformDatabaseServerDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  /** Null registers a new server. */
  readonly server = input<PlatformDatabaseServer | null>(null);
  readonly saved = output<void>();

  protected readonly saving = signal(false);
  protected readonly isNew = computed(() => this.server() === null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    adminConnectionString: ['', Validators.maxLength(2000)],
    maxDatabases: [
      DEFAULT_MAX_DATABASES as number | null,
      [Validators.required, Validators.min(1), Validators.max(10000)],
    ],
    useContainedUsers: [false],
    isActive: [true],
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.fill(this.server()));
      }
    });
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .saveDatabaseServer({
        id: this.server()?.id ?? null,
        name: value.name.trim(),
        adminConnectionString: value.adminConnectionString.trim() || null,
        useContainedUsers: value.useContainedUsers,
        maxDatabases: value.maxDatabases ?? DEFAULT_MAX_DATABASES,
        isActive: value.isActive,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.databaseServers.saved'),
          });
          this.visible.set(false);
          this.saved.emit();
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private fill(server: PlatformDatabaseServer | null): void {
    const connection = this.form.controls.adminConnectionString;
    connection.setValidators(
      server ? [Validators.maxLength(2000)] : [Validators.required, Validators.maxLength(2000)],
    );
    this.form.reset({
      name: server?.name ?? '',
      adminConnectionString: '',
      maxDatabases: server?.maxDatabases ?? DEFAULT_MAX_DATABASES,
      useContainedUsers: server?.useContainedUsers ?? false,
      isActive: server?.isActive ?? true,
    });
  }
}
