import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { TranslateContextDirective } from './translate-context.directive';

@Component({
  standalone: true,
  imports: [TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <span class="greeting">{{ t('greeting', { name: 'Sam' }) }}</span>
    </ng-container>
  `,
})
class HostComponent {}

/**
 * The directive is what makes `t(...)` exist. A template that renders nothing is the failure it
 * exists to prevent, so these check that the content is on the page at all — not just that the
 * translation is right.
 */
describe('translateContext directive', () => {
  let fixture: ComponentFixture<HostComponent>;
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent, TranslateModule.forRoot()] });

    translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { greeting: 'Hello {{name}}' });
    translate.setTranslation('ar', { greeting: 'مرحبا {{name}}' });
    translate.use('en');

    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renders the template and interpolates the parameters', () => {
    const greeting = (fixture.nativeElement as HTMLElement).querySelector('.greeting');

    expect(greeting).not.toBeNull();
    expect(greeting!.textContent?.trim()).toBe('Hello Sam');
  });

  it('follows a language change', () => {
    translate.use('ar');
    fixture.detectChanges();

    const greeting = (fixture.nativeElement as HTMLElement).querySelector('.greeting');

    expect(greeting!.textContent?.trim()).toBe('مرحبا Sam');
  });
});
