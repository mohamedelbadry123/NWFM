import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { ButtonModule } from 'primeng/button';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';

@Component({
  selector: 'app-json-output',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonModule, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <!-- Deliberately dark in both schemes (code surface), but drawn from the
           theme's cool-neutral ramp rather than Tailwind slate so it sits in
           the same colour family as the rest of the app. -->
      <div class="border border-[#22343d] rounded-xl overflow-hidden shadow-app-lg">
        <div class="flex items-center justify-between bg-[#18242b] px-4 py-2.5 text-[#8ba5b1] font-mono text-xs border-b border-[#22343d]">
          <div class="flex items-center gap-2">
            <span class="w-3 h-3 rounded-full bg-[#e06c6c] inline-block"></span>
            <span class="w-3 h-3 rounded-full bg-[#d9a441] inline-block"></span>
            <span class="w-3 h-3 rounded-full bg-[#57b98a] inline-block"></span>
            <span class="ml-2 font-semibold text-[#d3e0e6]">{{ t('formBuilder.json.title') }}</span>
          </div>
          <p-button
            [label]="copied() ? t('formBuilder.json.copied') : t('formBuilder.json.copy')"
            [icon]="copied() ? 'pi pi-check' : 'pi pi-copy'"
            size="small"
            [text]="true"
            (onClick)="copy()"
          />
        </div>
        <pre class="bg-[#0a1116] text-[#7ecadc] p-4 font-mono text-xs overflow-auto max-h-[420px] leading-relaxed m-0">{{ json() }}</pre>
      </div>
    </ng-container>
  `,
})
export class JsonOutputComponent {
  private readonly clipboard = inject(Clipboard);

  readonly json = input.required<string>();
  protected readonly copied = signal(false);

  protected copy(): void {
    this.clipboard.copy(this.json());
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 1500);
  }
}
