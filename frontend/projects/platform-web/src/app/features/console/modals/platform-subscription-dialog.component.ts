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
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { Dialog } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PlatformApiService,
  PlatformPlan,
  PlatformSubscriptionAction,
  PlatformTenantDetail,
} from '../../../core/api/platform-api.service';

const DEFAULT_TRIAL_DAYS = 14;

/**
 * Manual subscription management — there is no payment provider. Each action shows only the
 * fields it uses; the API validates the same rules.
 */
@Component({
  selector: 'app-platform-subscription-dialog',
  imports: [
    FormsModule,
    I18nDirective,
    ButtonModule,
    DatePickerModule,
    Dialog,
    InputNumberModule,
    Select,
    TextareaModule,
  ],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t('platform.subscription.title')"
        [style]="{ width: '34rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <div class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label>{{ t('platform.subscription.action') }}</label>
            <p-select
              [ngModel]="action()"
              (ngModelChange)="action.set($event)"
              [options]="actions"
              optionValue="value"
              appendTo="body"
              styleClass="w-full"
            >
              <ng-template #selectedItem let-item>{{ t(item.labelKey) }}</ng-template>
              <ng-template #item let-item>{{ t(item.labelKey) }}</ng-template>
            </p-select>
            <small class="text-muted-color">{{ t(hintKey()) }}</small>
          </div>

          @if (showPlan()) {
            <div class="flex flex-col gap-1">
              <label>{{ t('platform.tenants.plan') }}</label>
              <p-select
                [(ngModel)]="planCode"
                [options]="plans()"
                optionLabel="code"
                optionValue="code"
                [loading]="loadingPlans()"
                [showClear]="action() !== subscriptionActions.ChangePlan"
                appendTo="body"
                styleClass="w-full"
              />
            </div>
          }

          @if (action() === subscriptionActions.ExtendTrial) {
            <div class="flex flex-col gap-1">
              <label>{{ t('platform.subscription.trialDays') }}</label>
              <p-inputNumber
                [(ngModel)]="trialDays"
                [min]="1"
                [max]="365"
                [showButtons]="true"
                styleClass="w-full"
              />
            </div>
          }

          @if (action() === subscriptionActions.Activate) {
            <div class="flex flex-col gap-1">
              <label>{{ t('platform.subscription.periodEnds') }}</label>
              <p-datepicker
                [(ngModel)]="periodEnds"
                [showIcon]="true"
                [showClear]="true"
                appendTo="body"
                styleClass="w-full"
              />
              <small class="text-muted-color">{{
                t('platform.subscription.periodEndsHint')
              }}</small>
            </div>
          }

          @if (showNotes()) {
            <div class="flex flex-col gap-1">
              <label>{{ t('platform.subscription.notes') }}</label>
              <textarea pTextarea [(ngModel)]="notes" rows="3"></textarea>
            </div>
          }
        </div>
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
            [disabled]="saving() || !canSave()"
            (onClick)="save()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformSubscriptionDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();
  readonly saved = output<PlatformTenantDetail>();

  protected readonly subscriptionActions = PlatformSubscriptionAction;
  protected readonly actions = [
    {
      value: PlatformSubscriptionAction.Activate,
      labelKey: 'platform.subscription.actions.activate',
    },
    {
      value: PlatformSubscriptionAction.ExtendTrial,
      labelKey: 'platform.subscription.actions.extendTrial',
    },
    {
      value: PlatformSubscriptionAction.ChangePlan,
      labelKey: 'platform.subscription.actions.changePlan',
    },
    {
      value: PlatformSubscriptionAction.Suspend,
      labelKey: 'platform.subscription.actions.suspend',
    },
    { value: PlatformSubscriptionAction.Cancel, labelKey: 'platform.subscription.actions.cancel' },
  ];

  protected readonly action = signal<number>(PlatformSubscriptionAction.Activate);
  protected readonly plans = signal<PlatformPlan[]>([]);
  protected readonly loadingPlans = signal(false);
  protected readonly saving = signal(false);
  protected planCode: string | null = null;
  protected trialDays: number | null = DEFAULT_TRIAL_DAYS;
  protected periodEnds: Date | null = null;
  protected notes = '';

  protected readonly showPlan = computed(
    () =>
      this.action() === PlatformSubscriptionAction.Activate ||
      this.action() === PlatformSubscriptionAction.ChangePlan,
  );
  protected readonly showNotes = computed(
    () =>
      this.action() === PlatformSubscriptionAction.Activate ||
      this.action() === PlatformSubscriptionAction.Suspend ||
      this.action() === PlatformSubscriptionAction.Cancel,
  );
  protected readonly hintKey = computed(
    () => this.actions.find((a) => a.value === this.action())!.labelKey + 'Hint',
  );

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.open());
      }
    });
  }

  protected canSave(): boolean {
    switch (this.action()) {
      case PlatformSubscriptionAction.ChangePlan:
        return !!this.planCode;
      case PlatformSubscriptionAction.ExtendTrial:
        return !!this.trialDays && this.trialDays >= 1 && this.trialDays <= 365;
      default:
        return true;
    }
  }

  protected save(): void {
    if (this.saving() || !this.canSave()) {
      return;
    }

    const action = this.action();
    this.saving.set(true);
    this.api
      .updateSubscription(this.tenant().id, {
        action,
        planCode: this.showPlan() ? this.planCode : null,
        trialDays: action === PlatformSubscriptionAction.ExtendTrial ? this.trialDays : null,
        periodEndsAtUtc:
          action === PlatformSubscriptionAction.Activate && this.periodEnds
            ? endOfDayUtc(this.periodEnds)
            : null,
        notes: this.showNotes() ? this.notes || null : null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (tenant) => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.subscription.saved'),
          });
          this.visible.set(false);
          this.saved.emit(tenant);
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
    const subscription = this.tenant().subscription;
    this.action.set(PlatformSubscriptionAction.Activate);
    this.planCode = subscription?.planCode ?? null;
    this.trialDays = DEFAULT_TRIAL_DAYS;
    this.periodEnds = subscription?.periodEndsAtUtc ? new Date(subscription.periodEndsAtUtc) : null;
    this.notes = '';

    this.loadingPlans.set(true);
    this.api
      .getPlans()
      .pipe(finalize(() => this.loadingPlans.set(false)))
      .subscribe({ next: (plans) => this.plans.set(plans.filter((plan) => plan.isActive)) });
  }
}

/** A picked calendar day means "service through that day", so the period ends at its last instant (UTC). */
function endOfDayUtc(day: Date): string {
  return new Date(
    Date.UTC(day.getFullYear(), day.getMonth(), day.getDate(), 23, 59, 59),
  ).toISOString();
}
