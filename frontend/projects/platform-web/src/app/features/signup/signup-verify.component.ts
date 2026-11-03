import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import {
  I18nDirective,
  LanguageService,
  apiErrorMessage,
  emailLinkParam,
  NWFM_LOGO_WITH_SLOGAN,
} from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinner } from 'primeng/progressspinner';
import { PublicApiService, SignupStatus } from '../../core/api/public-api.service';

@Component({
  selector: 'app-signup-verify',
  imports: [RouterLink, I18nDirective, ButtonModule, ProgressSpinner],
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
            <img [src]="logo" [alt]="t('common.appName')" />
            <h1>{{ t('signup.verify.title') }}</h1>
          </div>
          @if (loading()) {
            <div class="flex justify-center"><p-progress-spinner /></div>
          } @else if (error()) {
            <p class="error">{{ error() }}</p>
            <div class="actions">
              <p-button
                [label]="t('signup.verify.resend')"
                [loading]="resending()"
                [disabled]="resending()"
                (onClick)="resend()"
              />
            </div>
          } @else {
            <p>
              {{
                t(
                  status()?.status === 'AwaitingApproval'
                    ? 'signup.verify.awaitingApproval'
                    : 'signup.verify.success'
                )
              }}
            </p>
            <div class="actions">
              <a routerLink="/signup/status/{{ status()?.signupId }}">{{
                t('signup.verify.continue')
              }}</a>
            </div>
          }
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: './public-page.scss',
})
export class SignupVerifyComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(PublicApiService);
  private readonly translate = inject(TranslateService);
  protected readonly language = inject(LanguageService);
  protected readonly logo = NWFM_LOGO_WITH_SLOGAN;

  protected readonly loading = signal(true);
  protected readonly resending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<SignupStatus | null>(null);

  private signupId = '';
  private token = '';

  ngOnInit(): void {
    this.signupId = this.route.snapshot.queryParamMap.get('id') ?? '';
    this.token = emailLinkParam(this.route.snapshot.queryParamMap, 'token');
    if (!this.signupId || !this.token) {
      this.loading.set(false);
      this.error.set(this.translate.instant('signup.verify.missing'));
      return;
    }

    this.api.verifyEmail(this.signupId, this.token).subscribe({
      next: (status) => {
        this.status.set(status);
        this.loading.set(false);
        if (status.status === 'AwaitingDatabase') {
          void this.router.navigate(['/signup/database', status.signupId], {
            queryParams: { token: this.token },
          });
          return;
        }
        void this.router.navigate(['/signup/status', status.signupId]);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(apiErrorMessage(err));
      },
    });
  }

  protected resend(): void {
    if (!this.signupId) {
      return;
    }
    this.resending.set(true);
    this.api.resendVerification(this.signupId).subscribe({
      next: () => this.resending.set(false),
      error: () => this.resending.set(false),
    });
  }
}
