import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { AuthService } from '../../../core/auth/auth.service';

const KNOWN_REASONS = [
  'notProvisioned',
  'inactive',
  'noScope',
  'crewAccount',
  'exchangeFailed',
  'noNameId',
] as const;

type DenialReason = (typeof KNOWN_REASONS)[number] | 'unknown';

@Component({
  selector: 'app-access-denied',
  standalone: true,
  imports: [TranslateModule, ButtonModule],
  templateUrl: './access-denied.component.html',
  styleUrl: './access-denied.component.scss',
})
export class AccessDeniedComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly retrying = signal(false);
  protected readonly username = this.route.snapshot.queryParamMap.get('username');

  protected readonly reason = computed<DenialReason>(() => {
    const raw = this.route.snapshot.queryParamMap.get('reason');
    return KNOWN_REASONS.includes(raw as (typeof KNOWN_REASONS)[number])
      ? (raw as DenialReason)
      : 'unknown';
  });

  protected readonly messageKey = computed(() => `auth.sso.denied.${this.reason()}`);

  protected retry(): void {
    if (this.retrying()) return;
    this.retrying.set(true);

    this.auth.getSsoStatus().subscribe({
      next: (res) => {
        if (res.isSuccess && res.value.enabled) {
          window.location.href = '/api/v1/auth/sso/redirect';
          return;
        }
        this.retrying.set(false);
        void this.router.navigate(['/login']);
      },
      error: () => {
        this.retrying.set(false);
        void this.router.navigate(['/login']);
      },
    });
  }
}
