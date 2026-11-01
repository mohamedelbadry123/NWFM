import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  I18nDirective,
  LanguageService,
  apiErrorMessage,
  NWFM_LOGO_WITH_SLOGAN,
} from '@platform/shared';

import { ProgressBar } from 'primeng/progressbar';
import { ButtonModule } from 'primeng/button';

import { PublicApiService, SignupStatus } from '../../core/api/public-api.service';

@Component({
  selector: 'app-signup-status',
  imports: [RouterLink, I18nDirective, ProgressBar, ButtonModule],
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
            <h1>{{ t('signup.status.title') }}</h1>
            <p>{{ status()?.organizationName }}</p>
          </div>
          @if (error()) {
            <p class="error">{{ error() }}</p>
          } @else if (status(); as current) {
            <p>{{ t('signup.status.current', { status: current.status }) }}</p>
            <p-progressBar [value]="progress(current)" />
            @if (current.status === 'AwaitingApproval') {
              <p>{{ t('signup.status.awaitingApproval') }}</p>
            } @else if (current.status === 'Rejected') {
              <p>{{ t('signup.status.rejected') }}</p>
            } @else if (current.message) {
              <p>{{ current.message }}</p>
            }
            @if (current.status === 'PendingEmailVerification') {
              <p>{{ t('signup.status.checkEmail') }}</p>
              <p-button
                [label]="t('signup.verify.resend')"
                [loading]="resending()"
                [disabled]="resending()"
                (onClick)="resend()"
              />
            }
            @if (current.status === 'AwaitingDatabase') {
              <a [routerLink]="['/signup/database', current.signupId]">{{
                t('signup.status.continueDatabase')
              }}</a>
            }
            @if (current.workspaceUrl) {
              <p>{{ t('signup.status.ready') }}</p>
              <a [href]="current.workspaceUrl">{{ current.workspaceUrl }}</a>
            }
          }
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: './public-page.scss',
})
export class SignupStatusComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(PublicApiService);
  protected readonly language = inject(LanguageService);
  protected readonly logo = NWFM_LOGO_WITH_SLOGAN;

  protected readonly status = signal<SignupStatus | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly resending = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), 4000);
  }

  ngOnDestroy(): void {
    if (this.timer) {
      clearInterval(this.timer);
    }
  }

  protected progress(status: SignupStatus): number {
    if (!status.totalSteps) {
      return 0;
    }
    return Math.round((status.completedSteps / status.totalSteps) * 100);
  }

  protected resend(): void {
    const id = this.status()?.signupId;
    if (!id) {
      return;
    }
    this.resending.set(true);
    this.api.resendVerification(id).subscribe({
      next: () => this.resending.set(false),
      error: () => this.resending.set(false),
    });
  }

  private refresh(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    this.api.getSignupStatus(id).subscribe({
      next: (status) => {
        this.status.set(status);
        if (
          status.status === 'Completed' ||
          status.status === 'Failed' ||
          status.status === 'Rejected'
        ) {
          if (this.timer) {
            clearInterval(this.timer);
            this.timer = null;
          }
        }
      },
      error: (err: unknown) => this.error.set(apiErrorMessage(err)),
    });
  }
}
