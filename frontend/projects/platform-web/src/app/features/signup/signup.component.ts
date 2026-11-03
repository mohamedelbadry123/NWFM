import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import {
  I18nDirective,
  LanguageService,
  apiErrorMessage,
  NWFM_LOGO_WITH_SLOGAN,
} from '@platform/shared';

import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { InputGroup } from 'primeng/inputgroup';
import { InputGroupAddon } from 'primeng/inputgroupaddon';
import { InputText } from 'primeng/inputtext';
import { RadioButtonModule } from 'primeng/radiobutton';
import { Select } from 'primeng/select';
import { StepsModule } from 'primeng/steps';
import { MessageService } from 'primeng/api';

import { PublicApiService, SignupOptions, SignupPlan } from '../../core/api/public-api.service';
import { isValidTenantKey, slugifyTenantKey, tenantKeyValidator, toTenantKey } from './tenant-key';
import { SIGNUP_PLAN_PARAM } from './signup-params';

const KEY_PLACEHOLDER = '{key}';
const KNOWN_KEY_REASONS = ['invalid', 'reserved', 'taken'];

@Component({
  selector: 'app-signup',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    I18nDirective,
    ButtonModule,
    InputGroup,
    InputGroupAddon,
    InputText,
    RadioButtonModule,
    Select,
    StepsModule,
  ],
  templateUrl: './signup.component.html',
  styleUrl: './public-page.scss',
})
export class SignupComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(PublicApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly language = inject(LanguageService);
  protected readonly logo = NWFM_LOGO_WITH_SLOGAN;

  protected readonly step = signal(0);
  protected readonly loading = signal(true);
  protected readonly submitting = signal(false);
  protected readonly checkingKey = signal(false);
  protected readonly keyAvailable = signal<boolean | null>(null);
  protected readonly keyReason = signal<string | null>(null);
  protected readonly options = signal<SignupOptions | null>(null);
  protected readonly plans = signal<SignupPlan[]>([]);
  /** While true the address follows the organisation name; typing in the address field turns it off. */
  protected readonly autoKey = signal(true);

  protected readonly stepItems = computed(() => {
    this.language.current();
    return [
      { label: this.translate.instant('signup.steps.workspace') },
      { label: this.translate.instant('signup.steps.admin') },
      { label: this.translate.instant('signup.steps.database') },
    ];
  });

  protected readonly form = this.fb.nonNullable.group({
    tenantKey: ['', [Validators.required, tenantKeyValidator]],
    organizationName: ['', [Validators.required, Validators.maxLength(200)]],
    planCode: [''],
    language: [this.language.current()],
    adminFullName: ['', [Validators.required, Validators.maxLength(200)]],
    adminEmail: ['', [Validators.required, Validators.email]],
    adminPhone: [''],
    databaseMode: [1 as 1 | 2, Validators.required],
  });

  ngOnInit(): void {
    this.api.getSignupOptions().subscribe({
      next: (options) => {
        this.options.set(options);
        this.plans.set([...options.plans]);
        // A plan picked on the landing page arrives as ?plan=CODE; an unknown code falls back to the default.
        const requested = this.route.snapshot.queryParamMap.get(SIGNUP_PLAN_PARAM);
        const planCode =
          options.plans.find((plan) => plan.code === requested)?.code ?? options.defaultPlanCode;
        this.form.patchValue({ planCode });
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.messages.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: apiErrorMessage(error),
        });
      },
    });

    this.form.controls.organizationName.valueChanges.subscribe((name) => {
      if (this.autoKey()) {
        this.setKey(slugifyTenantKey(name));
      }
    });

    this.form.controls.tenantKey.valueChanges
      .pipe(debounceTime(400), distinctUntilChanged())
      .subscribe((key) => {
        this.checkKey(key);
      });
  }

  /** The address template split around the key, e.g. "http://" and ".localhost:4200". */
  protected readonly urlPrefix = computed(() => this.urlTemplate().split(KEY_PLACEHOLDER)[0]);
  protected readonly urlSuffix = computed(() => this.urlTemplate().split(KEY_PLACEHOLDER)[1] ?? '');

  protected workspaceUrl(): string {
    return this.urlTemplate().replace(
      KEY_PLACEHOLDER,
      this.form.controls.tenantKey.value || 'workspace',
    );
  }

  /** Server reasons are codes; unknown ones fall back to the generic message. */
  protected keyReasonKey(): string {
    const reason = this.keyReason();
    return reason && KNOWN_KEY_REASONS.includes(reason)
      ? `signup.keyReasons.${reason}`
      : 'signup.keyTaken';
  }

  protected onKeyTyped(): void {
    this.autoKey.set(false);
  }

  /** Lower-cases the key and strips a pasted full address down to its first label. */
  protected normalizeKey(): void {
    const raw = this.form.controls.tenantKey.value;
    const key = toTenantKey(raw);
    if (key !== raw) {
      this.setKey(key);
    }
  }

  protected generateKey(): void {
    this.autoKey.set(true);
    this.setKey(slugifyTenantKey(this.form.controls.organizationName.value));
  }

  private urlTemplate(): string {
    return this.options()?.workspaceUrlTemplate ?? KEY_PLACEHOLDER;
  }

  private setKey(key: string): void {
    this.form.controls.tenantKey.setValue(key);
    this.form.controls.tenantKey.markAsTouched();
  }

  protected next(): void {
    if (
      this.step() === 0 &&
      (this.form.controls.tenantKey.invalid ||
        this.form.controls.organizationName.invalid ||
        this.keyAvailable() === false)
    ) {
      this.form.controls.tenantKey.markAsTouched();
      this.form.controls.organizationName.markAsTouched();
      return;
    }
    if (
      this.step() === 1 &&
      (this.form.controls.adminFullName.invalid || this.form.controls.adminEmail.invalid)
    ) {
      this.form.controls.adminFullName.markAsTouched();
      this.form.controls.adminEmail.markAsTouched();
      return;
    }
    this.step.update((value) => Math.min(value + 1, 2));
  }

  protected back(): void {
    this.step.update((value) => Math.max(value - 1, 0));
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.api
      .submitSignup({
        tenantKey: value.tenantKey,
        organizationName: value.organizationName,
        adminFullName: value.adminFullName,
        adminEmail: value.adminEmail,
        adminPhone: value.adminPhone || null,
        planCode: value.planCode || null,
        language: value.language,
        databaseMode: value.databaseMode,
      })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (result) => {
          void this.router.navigate(['/signup/status', result.signupId]);
        },
        error: (error: unknown) => {
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('signup.failed'),
            detail: apiErrorMessage(error),
          });
        },
      });
  }

  private checkKey(key: string): void {
    if (!isValidTenantKey(key)) {
      this.keyAvailable.set(null);
      return;
    }

    this.checkingKey.set(true);
    this.api.checkTenantKey(key).subscribe({
      next: (result) => {
        this.checkingKey.set(false);
        this.keyAvailable.set(result.isAvailable);
        this.keyReason.set(result.reason ?? null);
      },
      error: () => {
        this.checkingKey.set(false);
        this.keyAvailable.set(null);
      },
    });
  }
}
