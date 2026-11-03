import { Injectable, inject, signal } from '@angular/core';
import { AppConfigService } from '../config/app-config.service';

/** Subdomains that never name a workspace. Mirrors `Tenancy:ReservedSubdomains` on the API. */
const RESERVED = new Set([
  'www',
  'api',
  'app',
  'admin',
  'platform',
  'console',
  'mail',
  'smtp',
  'support',
  'status',
  'static',
  'cdn',
  'assets',
  'auth',
  'login',
  'signup',
  'help',
  'docs',
  'dev',
  'uat',
  'localhost',
]);

const TENANT_QUERY = 'tenant';
const TENANT_STORAGE_KEY = 'nwfm_tenant';

/** Why the API refused to serve this workspace. Each has a message on the unavailable page. */
export const TENANT_REFUSALS = [
  'Tenant.NotFound',
  'Tenant.Required',
  'Tenant.Suspended',
  'Tenant.Maintenance',
  'Tenant.SchemaUpgradePending',
  'Tenant.Unverified',
  'Tenant.SubscriptionInactive',
  'Tenant.Inactive',
] as const;

export type TenantRefusal = (typeof TENANT_REFUSALS)[number];

export function isTenantRefusal(code: unknown): code is TenantRefusal {
  return typeof code === 'string' && (TENANT_REFUSALS as readonly string[]).includes(code);
}

/**
 * The workspace this browser is talking to. Taken from the host (`acme.example.com`,
 * `acme.localhost`); the API resolves the same host, so nothing needs sending. Only where
 * `allowTenantQuery` is on (local development) may `?tenant=` name it instead, and that hint travels
 * as `X-Tenant` on anonymous calls only: once signed in, the token's tenant is authoritative.
 */
@Injectable({ providedIn: 'root' })
export class TenantContextService {
  private readonly config = inject(AppConfigService);

  /** The workspace named by the host, or by the development query hint. */
  readonly key = signal<string | null>(null);

  /** Whether the key came from the development hint and must be sent as a header. */
  readonly fromQuery = signal(false);

  /** The last refusal the API gave for this workspace; the guard sends the user to explain it. */
  readonly refusal = signal<TenantRefusal | null>(null);

  initialize(): void {
    const fromHost = keyFromHost(location.hostname);
    if (fromHost) {
      this.key.set(fromHost);
      return;
    }

    if (!this.config.snapshot.allowTenantQuery) {
      return;
    }

    const fromQuery = new URLSearchParams(location.search).get(TENANT_QUERY)?.trim().toLowerCase();
    if (fromQuery) {
      sessionStorage.setItem(TENANT_STORAGE_KEY, fromQuery);
    }

    const key = fromQuery || sessionStorage.getItem(TENANT_STORAGE_KEY);
    this.key.set(key);
    this.fromQuery.set(!!key);
  }
}

/** `acme` from `acme.example.com` or `acme.localhost`; null for a bare or reserved host. */
export function keyFromHost(hostname: string): string | null {
  const labels = hostname.toLowerCase().split('.');
  const isLocal = labels.length >= 2 && labels[labels.length - 1] === 'localhost';
  if (!isLocal && labels.length < 3) {
    return null;
  }

  const sub = labels[0];
  return RESERVED.has(sub) ? null : sub;
}
