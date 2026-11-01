import { describe, expect, it } from 'vitest';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { I18nDirective } from '@platform/shared';
import en from '../../public/i18n/en.json';
import ar from '../../public/i18n/ar.json';
import { isValidTenantKey, slugifyTenantKey, toTenantKey } from './features/signup/tenant-key';

type Dictionary = { [key: string]: string | Dictionary };

const keys = (dictionary: Dictionary, prefix = ''): string[] =>
  Object.entries(dictionary).flatMap(([key, value]) =>
    typeof value === 'string' ? [`${prefix}${key}`] : keys(value, `${prefix}${key}.`),
  );

describe('platform dictionaries', () => {
  it('carry the same keys in Arabic and English', () => {
    expect(keys(ar as Dictionary).sort()).toEqual(keys(en as Dictionary).sort());
  });
});

describe('workspace keys', () => {
  it('accept only a single DNS label the API also accepts', () => {
    expect(isValidTenantKey('acme-water')).toBe(true);
    expect(isValidTenantKey('ab')).toBe(false);
    expect(isValidTenantKey('1acme')).toBe(false);
    expect(isValidTenantKey('acme--water')).toBe(false);
  });

  it('turn names and pasted addresses into a key', () => {
    expect(slugifyTenantKey('Nile Water Co.')).toBe('nile-water-co');
    expect(toTenantKey('https://acme.localhost:4200/login')).toBe('acme');
  });
});

@Component({
  imports: [I18nDirective],
  template: `<ng-container *appI18n="let t"
    ><span>{{ t('hello', { name: 'Sam' }) }}</span></ng-container
  >`,
})
class HostComponent {}

describe('appI18n directive', () => {
  it('renders translations and follows a language change', async () => {
    TestBed.configureTestingModule({ imports: [HostComponent, TranslateModule.forRoot()] });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { hello: 'Hello {{name}}' });
    translate.setTranslation('ar', { hello: 'مرحبا {{name}}' });
    translate.use('en');
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('Hello Sam');

    translate.use('ar');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('مرحبا Sam');
  });
});
