import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TENANT_HEADER } from './tenancy.constants';
import { TenantContextService, isTenantRefusal, keyFromHost } from './tenant-context.service';
import { tenantInterceptor } from './tenant.interceptor';

describe('keyFromHost', () => {
  it('reads the workspace from its own subdomain', () => {
    expect(keyFromHost('acme.example.com')).toBe('acme');
    expect(keyFromHost('ACME.localhost')).toBe('acme');
  });

  it('never treats a bare, platform or reserved host as a workspace', () => {
    expect(keyFromHost('localhost')).toBeNull();
    expect(keyFromHost('example.com')).toBeNull();
    expect(keyFromHost('www.example.com')).toBeNull();
    expect(keyFromHost('platform.localhost')).toBeNull();
  });
});

describe('tenant refusals', () => {
  it('recognises only the workspace-level refusal codes', () => {
    expect(isTenantRefusal('Tenant.Suspended')).toBe(true);
    expect(isTenantRefusal('Tenant.Mismatch')).toBe(false);
    expect(isTenantRefusal('Auth.InvalidCredentials')).toBe(false);
  });
});

describe('tenantInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let tenancy: TenantContextService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([tenantInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    tenancy = TestBed.inject(TenantContextService);
  });

  it('sends the development hint on anonymous calls only', () => {
    tenancy.key.set('acme');
    tenancy.fromQuery.set(true);

    http.get('/api/app-context').subscribe();
    http.get('/api/v1/auth/me', { headers: { Authorization: 'Bearer token' } }).subscribe();

    expect(backend.expectOne('/api/app-context').request.headers.get(TENANT_HEADER)).toBe('acme');
    expect(backend.expectOne('/api/v1/auth/me').request.headers.has(TENANT_HEADER)).toBe(false);
  });

  it('never sends a header for a workspace that came from the host', () => {
    tenancy.key.set('acme');
    tenancy.fromQuery.set(false);

    http.get('/api/app-context').subscribe();

    expect(backend.expectOne('/api/app-context').request.headers.has(TENANT_HEADER)).toBe(false);
  });

  it('records a workspace refusal so the guard can explain it', () => {
    http.get('/api/app-context').subscribe({ error: () => undefined });
    backend
      .expectOne('/api/app-context')
      .flush(
        { isSuccess: false, error: { code: 'Tenant.Suspended' } },
        { status: 403, statusText: 'Forbidden' },
      );

    expect(tenancy.refusal()).toBe('Tenant.Suspended');
  });
});
