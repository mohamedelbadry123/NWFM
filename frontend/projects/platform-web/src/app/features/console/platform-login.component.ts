import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import {
  I18nDirective,
  LanguageService,
  apiErrorMessage,
  NWFM_LOGO_WITH_SLOGAN,
} from '@platform/shared';
import { finalize } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { PlatformApiService } from '../../core/api/platform-api.service';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';

@Component({
  selector: 'app-platform-login',
  imports: [ReactiveFormsModule, RouterLink, I18nDirective, ButtonModule, InputText, Password],
  template: `
    <ng-container *appI18n="let t">
      <div class="public-page">
        <div class="public-topbar">
          <button type="button" class="lang-toggle" (click)="language.toggle()">
            <i class="pi pi-globe"></i>
            <span>{{
              language.current() === 'ar' ? t('common.english') : t('common.arabic')
            }}</span>
          </button>
        </div>
        <div class="public-card">
          <div class="public-heading">
            <img [src]="logo" [alt]="t('platform.title')" />
            <h1>{{ t('platform.login.title') }}</h1>
            <p>{{ t('platform.login.subtitle') }}</p>
          </div>
          <form [formGroup]="form" (ngSubmit)="submit()">
            <div class="field">
              <label for="email">{{ t('platform.login.email') }}</label>
              <input
                pInputText
                id="email"
                type="email"
                formControlName="email"
                autocomplete="username"
              />
            </div>
            <div class="field">
              <label for="password">{{ t('platform.login.password') }}</label>
              <p-password
                inputId="password"
                formControlName="password"
                [feedback]="false"
                [toggleMask]="true"
                styleClass="w-full"
                inputStyleClass="w-full"
              />
            </div>
            <div class="actions">
              <p-button
                type="submit"
                [label]="t('auth.signIn')"
                [loading]="submitting()"
                [disabled]="submitting()"
              />
            </div>
          </form>
          <p class="form-footer">
            <a routerLink="/">{{ t('platform.login.tenantSignIn') }}</a>
          </p>
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: '../signup/public-page.scss',
})
export class PlatformLoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(PlatformApiService);
  private readonly store = inject(PlatformAuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly language = inject(LanguageService);
  protected readonly logo = NWFM_LOGO_WITH_SLOGAN;
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.api
      .login(email, password)
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (login) => {
          this.store.setSession(login);
          void this.router.navigateByUrl(
            this.route.snapshot.queryParamMap.get('returnUrl') ?? '/console/tenants',
          );
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('auth.loginFailed'),
            detail: apiErrorMessage(error),
          }),
      });
  }
}
