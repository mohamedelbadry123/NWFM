import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthStore } from '../../../core/auth/auth.store';

@Component({
  selector: 'app-saml-callback',
  standalone: true,
  imports: [TranslateModule, ProgressSpinnerModule],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-ink-50">
      <div class="text-center">
        @if (loading()) {
          <p-progressSpinner strokeWidth="4" />
          <p class="mt-4 text-ink-600">{{ 'auth.sso_callback.loading' | translate }}</p>
        } @else if (error()) {
          <div class="rounded-xl border border-red-200 bg-red-50 p-6">
            <p class="text-red-700">{{ 'auth.sso_callback.error' | translate }}</p>
            <a routerLink="/login" class="mt-3 inline-block text-primary underline">
              {{ 'auth.access_denied.back_to_login' | translate }}
            </a>
          </div>
        }
      </div>
    </div>
  `
})
export class SamlCallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly authStore = inject(AuthStore);

  readonly loading = signal(true);
  readonly error = signal(false);

  ngOnInit(): void {
    const code = this.route.snapshot.queryParamMap.get('code');
    if (!code) {
      this.loading.set(false);
      this.error.set(true);
      return;
    }

    this.authService.exchangeSsoCode(code).subscribe({
      next: (response) => {
        this.loading.set(false);
        if (response.isSuccess) {
          this.authStore.setSession(response);
          this.router.navigate(['/']);
        } else {
          this.error.set(true);
        }
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      }
    });
  }
}
