import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthStore } from '../../../core/auth/auth.store';

const CODE_PARAM = 'code';

/**
 * Where the platform console sends a support user into this workspace. The link carries a
 * single-use code in its fragment, never a token, so nothing reusable reaches browser history,
 * server logs or a referrer header. The code is traded here for this workspace's own token, valid
 * only while the workspace's grant lasts.
 */
@Component({
  selector: 'app-support-handoff',
  standalone: true,
  imports: [TranslateModule, ProgressSpinnerModule, RouterLink],
  templateUrl: './support-handoff.component.html',
  styleUrl: '../access-denied/access-denied.component.scss',
})
export class SupportHandoffComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly store = inject(AuthStore);

  protected readonly failed = signal(false);

  ngOnInit(): void {
    const code = new URLSearchParams(this.route.snapshot.fragment ?? '').get(CODE_PARAM);

    // Drop the fragment at once: the code is spent below either way and must not linger in the URL.
    history.replaceState(history.state, '', location.pathname);

    if (!code) {
      this.failed.set(true);
      return;
    }

    // Whatever this browser was signed in as here, the support session replaces it.
    this.store.clearSession();
    this.auth.exchangeSupportHandoff(code).subscribe({
      next: (result) => {
        const token = result.isSuccess ? result.value : undefined;
        if (!token?.accessToken) {
          this.failed.set(true);
          return;
        }
        // No refresh token: a support session ends with its grant, never by renewal.
        this.store.setSession({
          isSuccess: true,
          value: {
            accessToken: token.accessToken,
            refreshToken: '',
            expiresInSeconds: token.expiresInSeconds,
            userName: token.impersonatedUserName,
            roles: token.roles,
            permissions: token.permissions,
          },
        } as Parameters<AuthStore['setSession']>[0]);
        this.auth.getProfile().subscribe({
          next: (profile) => {
            if (profile.isSuccess) this.store.updateProfile(profile.value);
            void this.router.navigateByUrl('/');
          },
          error: () => void this.router.navigateByUrl('/'),
        });
      },
      error: () => this.failed.set(true),
    });
  }
}
