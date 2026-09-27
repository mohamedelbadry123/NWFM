import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ProgressSpinner } from 'primeng/progressspinner';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { AppContextService } from '../../../core/context/app-context.service';

@Component({
  selector: 'app-saml-callback',
  standalone: true,
  imports: [TranslateModule, ProgressSpinner],
  templateUrl: './saml-callback.component.html',
  styleUrl: './saml-callback.component.scss',
})
export class SamlCallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly store = inject(AuthStore);
  private readonly appContext = inject(AppContextService);

  protected readonly failed = signal(false);

  ngOnInit(): void {
    const code = this.route.snapshot.queryParamMap.get('code');
    const returnUrl =
      this.route.snapshot.queryParamMap.get('returnUrl') ??
      sessionStorage.getItem('nwfm.sso.returnUrl');

    if (!code) {
      this.denied('exchangeFailed');
      return;
    }

    this.auth.exchangeSsoCode(code).subscribe({
      next: (response) => {
        if (response.isSuccess) {
          this.store.setSession(response);
          sessionStorage.removeItem('nwfm.sso.returnUrl');
          this.auth.getProfile().subscribe({
            next: async (res) => {
              if (res.isSuccess) this.store.updateProfile(res.value);
              await this.appContext.load();
              await this.router.navigateByUrl(returnUrl && returnUrl !== '/login' ? returnUrl : '/');
            },
            error: async () => {
              await this.appContext.load();
              await this.router.navigateByUrl(returnUrl && returnUrl !== '/login' ? returnUrl : '/');
            },
          });
        } else {
          this.denied('exchangeFailed');
        }
      },
      error: () => this.denied('exchangeFailed'),
    });
  }

  private denied(reason: string): void {
    this.failed.set(true);
    void this.router.navigate(['/auth/access-denied'], { queryParams: { reason } });
  }
}
