import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { Observable, finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { PasswordModule } from 'primeng/password';
import { ToggleSwitchModule } from 'primeng/toggleswitch';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { ApiResult } from '../../../core/api/api-result';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { Team, TeamsService } from '../../../core/teams/teams.service';
import { OrgScopeSelectorComponent } from '../../../shared/components/org-scope/org-scope-selector.component';
import { OrgScopeAssignment } from '../../../shared/components/org-scope/org-scope.model';

/**
 * Creates or edits a field team. A new team is raised together with its login — the code its crew
 * signs in with — because a crew is a login as much as a record. The territory decides what the
 * crew may sign in to, which tasks it can be handed, and which it then sees.
 */
@Component({
  selector: 'app-team-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    PasswordModule,
    ToggleSwitchModule,
    OrgScopeSelectorComponent,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t(isEdit() ? 'teams.edit' : 'teams.new')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '46rem' }"
        [breakpoints]="{ '960px': '90vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-4">
          <div class="grid grid-cols-1 gap-3 md:grid-cols-2">
            <div class="flex flex-col gap-1">
              <label for="team-name" class="text-sm font-medium">{{ t('teams.name') }} *</label>
              <input pInputText id="team-name" formControlName="name" class="w-full" autocomplete="off" />
              @if (form.controls.name.touched && form.controls.name.invalid) {
                <small class="text-red-500">{{ t('teams.nameRequired') }}</small>
              }
            </div>

            <div class="flex flex-col gap-1">
              <label for="team-mobile" class="text-sm font-medium">{{ t('teams.mobile') }}</label>
              <input pInputText id="team-mobile" type="tel" dir="ltr" formControlName="mobile" class="w-full" autocomplete="off" />
            </div>

            <div class="flex flex-col gap-1">
              <label for="team-code" class="text-sm font-medium">{{ t('teams.userCode') }} *</label>
              <!-- The crew's devices sign in with it; changing it later would lock them out. -->
              <input pInputText id="team-code" formControlName="userCode" class="w-full font-mono" autocomplete="off" [readonly]="isEdit()" />
              @if (form.controls.userCode.touched && form.controls.userCode.invalid) {
                <small class="text-red-500">{{ t('teams.userCodeRequired') }}</small>
              }
            </div>

            <div class="flex flex-col gap-1">
              <label for="team-email" class="text-sm font-medium">{{ t('users.email') }}</label>
              <input pInputText id="team-email" formControlName="email" class="w-full" autocomplete="off" />
            </div>

            @if (!isEdit()) {
              <div class="flex flex-col gap-1">
                <label for="team-password" class="text-sm font-medium">{{ t('users.password') }} *</label>
                <p-password
                  inputId="team-password"
                  formControlName="password"
                  [feedback]="false"
                  [toggleMask]="true"
                  styleClass="w-full"
                  inputStyleClass="w-full"
                  autocomplete="new-password"
                />
                @if (form.controls.password.touched && form.controls.password.invalid) {
                  <small class="text-red-500">{{ t('teams.passwordTooShort') }}</small>
                }
              </div>

              <div class="flex items-center gap-2 self-end pb-2">
                <p-toggleswitch inputId="team-active" formControlName="isActive" />
                <label for="team-active" class="text-sm">{{ t('common.active') }}</label>
              </div>
            }
          </div>

          <div class="flex flex-col gap-2 border-t border-surface-200 pt-4 dark:border-surface-700">
            <span class="text-sm font-semibold">{{ t('teams.territory') }} *</span>
            <small class="text-surface-500">{{ t('teams.territoryHint') }}</small>
            <app-org-scope-selector
              mode="multi"
              [initialScopes]="initialScopes()"
              [disabled]="saving()"
              (scopesChange)="scopes.set($event)"
            />
            @if (scopesTouched() && scopes().length === 0) {
              <small class="text-red-500">{{ t('teams.scopeRequired') }}</small>
            }
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" icon="pi pi-times" [text]="true" severity="secondary" [disabled]="saving()" (onClick)="visible.set(false)" />
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" [disabled]="saving()" (onClick)="save()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TeamDialogComponent {
  readonly visible = model.required<boolean>();
  readonly team = input<Team | null>(null);
  readonly saved = output<void>();

  private readonly api = inject(TeamsService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly scopes = signal<OrgScopeAssignment[]>([]);
  protected readonly initialScopes = signal<OrgScopeAssignment[]>([]);
  protected readonly scopesTouched = signal(false);

  protected readonly isEdit = computed(() => this.team() !== null);

  protected readonly form = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    mobile: this.fb.control<string>('', Validators.maxLength(20)),
    userCode: this.fb.control('', [Validators.required, Validators.maxLength(50)]),
    email: this.fb.control<string>('', Validators.email),
    password: this.fb.control('', [Validators.required, Validators.minLength(8)]),
    isActive: this.fb.control(true),
  });

  protected onShow(): void {
    const team = this.team();

    this.form.reset({
      name: team?.name ?? '',
      mobile: team?.mobile ?? '',
      userCode: team?.userCode ?? '',
      email: team?.email ?? '',
      password: '',
      isActive: team?.isActive ?? true,
    });

    // Only a new team sets a password here; an existing one resets it from the grid.
    if (team) {
      this.form.controls.password.disable();
      this.form.controls.userCode.clearValidators();
    } else {
      this.form.controls.password.enable();
      this.form.controls.userCode.setValidators([Validators.required, Validators.maxLength(50)]);
    }
    this.form.controls.userCode.updateValueAndValidity();

    const scopes = [...(team?.scopes ?? [])];
    this.scopes.set(scopes);
    // A new array identity is what tells the picker to reseed.
    this.initialScopes.set([...scopes]);
    this.scopesTouched.set(false);
  }

  protected save(): void {
    this.scopesTouched.set(true);
    if (this.form.invalid || this.saving() || this.scopes().length === 0) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const base = {
      name: value.name!.trim(),
      mobile: value.mobile?.trim() || null,
      email: value.email?.trim() || null,
      scopes: this.scopes(),
    };

    const current = this.team();
    const request: Observable<ApiResult<Team>> = current
      ? this.api.update(current.id, base)
      : this.api.create({ ...base, userCode: value.userCode!.trim(), password: value.password!, isActive: !!value.isActive });

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('common.success'),
          detail: this.translate.instant(current ? 'teams.updated' : 'teams.created'),
        });
        this.visible.set(false);
        this.saved.emit();
      },
      error: (error: unknown) => {
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: apiErrorMessage(error, this.translate),
          life: 8000,
        });
      },
    });
  }
}
