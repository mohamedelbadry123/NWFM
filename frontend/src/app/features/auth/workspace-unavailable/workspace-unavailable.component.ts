import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { AppConfigService } from '../../../core/config/app-config.service';
import { isTenantRefusal } from '../../../core/tenancy/tenant-context.service';

/**
 * Explains why this workspace cannot be used right now: the address names no workspace, it is
 * suspended, its subscription lapsed, or it is being upgraded. Never says more than the API did.
 */
@Component({
  selector: 'app-workspace-unavailable',
  standalone: true,
  imports: [TranslateModule, ButtonModule],
  templateUrl: './workspace-unavailable.component.html',
  styleUrl: '../access-denied/access-denied.component.scss',
})
export class WorkspaceUnavailableComponent {
  private readonly route = inject(ActivatedRoute);
  protected readonly platformUrl = inject(AppConfigService).snapshot.platformUrl;

  protected readonly reasonKey = computed(() => {
    const reason = this.route.snapshot.queryParamMap.get('reason');
    return isTenantRefusal(reason)
      ? `tenancy.unavailable.reasons.${reason.replace('Tenant.', '')}`
      : 'tenancy.unavailable.reasons.Unknown';
  });

  protected retry(): void {
    location.assign('/');
  }
}
