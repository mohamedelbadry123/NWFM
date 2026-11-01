import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { HelpTipComponent } from './help-tip.component';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformPlan } from '../../core/api/platform-api.service';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';

@Component({
  selector: 'app-platform-plans',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Checkbox,
    Dialog,
    InputText,
    InputNumberModule,
    TableModule,
    TagModule,
    TooltipModule,
    HelpTipComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      @if (store.isAdmin()) {
        <div class="mb-4 flex justify-end">
          <p-button
            [label]="t('platform.plans.add')"
            icon="pi pi-plus"
            [pTooltip]="t('platform.tips.addPlan')"
            tooltipPosition="bottom"
            (onClick)="open()"
          />
        </div>
      }
      <div class="card p-4">
        <p-table [value]="plans()" [loading]="loading()">
          <ng-template pTemplate="header">
            <tr>
              <th>
                {{ t('platform.plans.code') }}<app-help-tip [text]="t('platform.tips.planCode')" />
              </th>
              <th>{{ t('platform.plans.name') }}</th>
              <th>
                {{ t('platform.plans.trialDays')
                }}<app-help-tip [text]="t('platform.tips.trialDays')" />
              </th>
              <th>{{ t('platform.plans.tenants') }}</th>
              <th>{{ t('common.actions') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.code }}</td>
              <td>{{ row.nameEn }} / {{ row.nameAr }}</td>
              <td>{{ row.trialDays }}</td>
              <td>{{ row.tenantCount }}</td>
              <td>
                @if (store.isAdmin()) {
                  <p-button [label]="t('common.edit')" size="small" (onClick)="open(row)" />
                }
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [header]="t('platform.plans.edit')"
        [style]="{ width: '36rem' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label for="planCode"
              >{{ t('platform.plans.code') }}<app-help-tip [text]="t('platform.tips.planCode')"
            /></label>
            <input pInputText id="planCode" formControlName="code" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planNameEn">{{ t('platform.plans.nameEn') }}</label>
            <input pInputText id="planNameEn" formControlName="nameEn" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planNameAr">{{ t('platform.plans.nameAr') }}</label>
            <input pInputText id="planNameAr" formControlName="nameAr" dir="rtl" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planTrial"
              >{{ t('platform.plans.trialDays')
              }}<app-help-tip [text]="t('platform.tips.trialDays')"
            /></label>
            <p-inputNumber inputId="planTrial" formControlName="trialDays" styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planUsers"
              >{{ t('platform.plans.maxUsers') }}<app-help-tip [text]="t('platform.tips.maxUsers')"
            /></label>
            <p-inputNumber inputId="planUsers" formControlName="maxUsers" styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planTeams"
              >{{ t('platform.plans.maxTeams') }}<app-help-tip [text]="t('platform.tips.maxTeams')"
            /></label>
            <p-inputNumber inputId="planTeams" formControlName="maxTeams" styleClass="w-full" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="planStorage"
              >{{ t('platform.plans.storage')
              }}<app-help-tip [text]="t('platform.tips.maxStorage')"
            /></label>
            <p-inputNumber
              inputId="planStorage"
              formControlName="maxStorageGb"
              styleClass="w-full"
            />
          </div>
          <div class="flex items-center gap-2">
            <p-checkbox inputId="planPublic" formControlName="isPublic" [binary]="true" />
            <label for="planPublic">{{ t('platform.plans.public') }}</label>
            <app-help-tip [text]="t('platform.tips.planPublic')" />
          </div>
          <div class="flex items-center gap-2">
            <p-checkbox inputId="planActive" formControlName="isActive" [binary]="true" />
            <label for="planActive">{{ t('platform.plans.active') }}</label>
            <app-help-tip [text]="t('platform.tips.planActive')" />
          </div>
        </form>
        <div class="flex justify-end gap-2 mt-4">
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="saving()"
            (onClick)="visible.set(false)"
          />
          <p-button
            [label]="t('common.save')"
            [loading]="saving()"
            [disabled]="saving()"
            (onClick)="save()"
          />
        </div>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformPlansComponent implements OnInit {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly store = inject(PlatformAuthStore);
  protected readonly plans = signal<PlatformPlan[]>([]);
  protected readonly loading = signal(false);
  protected readonly visible = signal(false);
  protected readonly saving = signal(false);
  private editingId: number | null = null;

  protected readonly form = this.fb.nonNullable.group({
    code: ['', Validators.required],
    nameEn: ['', Validators.required],
    nameAr: ['', Validators.required],
    trialDays: [14],
    maxUsers: [null as number | null],
    maxTeams: [null as number | null],
    maxStorageGb: [null as number | null],
    isPublic: [true],
    isActive: [true],
    sortOrder: [0],
  });

  ngOnInit(): void {
    this.reload();
  }

  protected open(plan?: PlatformPlan): void {
    this.editingId = plan?.id ?? null;
    this.form.reset({
      code: plan?.code ?? '',
      nameEn: plan?.nameEn ?? '',
      nameAr: plan?.nameAr ?? '',
      trialDays: plan?.trialDays ?? 14,
      maxUsers: plan?.maxUsers ?? null,
      maxTeams: plan?.maxTeams ?? null,
      maxStorageGb: plan?.maxStorageGb ?? null,
      isPublic: plan?.isPublic ?? true,
      isActive: plan?.isActive ?? true,
      sortOrder: plan?.sortOrder ?? 0,
    });
    this.visible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.api
      .savePlan({ id: this.editingId ?? undefined, ...this.form.getRawValue() })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.visible.set(false);
          this.reload();
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private reload(): void {
    this.loading.set(true);
    this.api.getPlans().subscribe({
      next: (plans) => {
        this.plans.set([...plans]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
