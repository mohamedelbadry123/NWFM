import { AbstractControl, ValidationErrors } from '@angular/forms';

// Mirrors Tenancy.Domain.Entities.TenantKey: the server stays the authority, this only spares a round trip.
export const TENANT_KEY_MIN_LENGTH = 3;
export const TENANT_KEY_MAX_LENGTH = 32;
const TENANT_KEY_PATTERN = /^[a-z][a-z0-9-]*[a-z0-9]$/;
const TENANT_KEY_ERROR = 'tenantKeyFormat';

export function isValidTenantKey(key: string): boolean {
  return (
    key.length >= TENANT_KEY_MIN_LENGTH &&
    key.length <= TENANT_KEY_MAX_LENGTH &&
    TENANT_KEY_PATTERN.test(key) &&
    !key.includes('--')
  );
}

export function tenantKeyValidator(control: AbstractControl<string>): ValidationErrors | null {
  const key = control.value ?? '';
  return key === '' || isValidTenantKey(key) ? null : { [TENANT_KEY_ERROR]: true };
}

/** "Nile Water Co." → "nile-water-co". Returns '' when nothing usable is left (e.g. an Arabic-only name). */
export function slugifyTenantKey(text: string): string {
  return (text ?? '')
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^[^a-z]+/, '')
    .slice(0, TENANT_KEY_MAX_LENGTH)
    .replace(/-+$/, '');
}

/** Accepts a pasted address ("https://nwc.localhost:4200") as well as a bare key and keeps only the key. */
export function toTenantKey(input: string): string {
  const host = (input ?? '')
    .trim()
    .replace(/^[a-z][a-z0-9+.-]*:\/\//i, '')
    .split(/[/:?#]/)[0];
  const label = host.includes('.') ? host.split('.')[0] : host;
  return slugifyTenantKey(label);
}
