import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { LocaleService } from '@core/i18n/locale.service';

@Component({
  selector: 'app-workflow-hierarchy-palette',
  standalone: true,
  template: `
    <nav class="p-3 space-y-3" [attr.aria-label]="t('Workflow elements', 'عناصر سير العمل')">
      <h2 class="text-sm font-semibold">
        {{
          main ? t('Main workflow', 'سير العمل الرئيسي') : t('Child workflow', 'سير العمل الفرعي')
        }}
      </h2>
      <p class="text-xs opacity-70">
        {{
          main
            ? t(
                'Build the overall process with Task Types. Open each child workflow to design its steps.',
                'ابنِ المسار العام بأنواع المهام. افتح سير العمل الفرعي لكل نوع لتصميم خطواته.'
              )
            : t(
                'Build the steps for this Task Type. Each Step uses one form.',
                'ابنِ خطوات نوع المهمة الحالي. تستخدم كل خطوة نموذجاً واحداً.'
              )
        }}
      </p>
      @for (item of items; track item.type) {
        <button
          type="button"
          class="w-full border rounded-lg p-3 text-start disabled:opacity-40"
          [disabled]="
            readonly ||
            (item.type === 'MainActivity' && !main) ||
            (item.type === 'UserTask' && main)
          "
          (click)="add.emit(item.type)"
        >
          <span class="block font-semibold">{{ t(item.en, item.ar) }}</span>
          <span class="block text-xs opacity-70 mt-1">{{
            item.type === 'UserTask' && main
              ? t('Available inside a child workflow', 'متاح داخل سير العمل الفرعي')
              : item.type === 'MainActivity' && !main
                ? t('Added in the main workflow', 'يُضاف في سير العمل الرئيسي')
                : t(item.hint, item.hintAr)
          }}</span>
        </button>
      }
      @if (routing) {
        <h3 class="text-sm font-semibold pt-3">
          {{ t('Routing & coordination', 'التوجيه والتنسيق') }}
        </h3>
        @for (item of routingItems; track item.type) {
          <button
            type="button"
            class="w-full border rounded-lg p-3 text-start disabled:opacity-40"
            [disabled]="readonly"
            (click)="add.emit(item.type)"
          >
            <span class="block font-semibold">{{ t(item.en, item.ar) }}</span
            ><span class="block text-xs opacity-70 mt-1">{{ t(item.hint, item.hintAr) }}</span>
          </button>
        }
      }
    </nav>
  `,
})
export class WorkflowHierarchyPaletteComponent {
  @Input() main = true;
  @Input() readonly = false;
  @Input() routing = false;
  @Output() add = new EventEmitter<
    | 'MainActivity'
    | 'UserTask'
    | 'ServiceTask'
    | 'ExclusiveGateway'
    | 'ParallelGateway'
    | 'JoinGateway'
  >();
  readonly routingItems = [
    {
      type: 'ExclusiveGateway' as const,
      en: 'Condition',
      ar: 'شرط',
      hint: 'Choose where to go based on data.',
      hintAr: 'اختيار المسار بناءً على البيانات',
    },
    {
      type: 'ParallelGateway' as const,
      en: 'Fork',
      ar: 'تفرع',
      hint: 'Start several branches together.',
      hintAr: 'بدء فروع متزامنة',
    },
    {
      type: 'JoinGateway' as const,
      en: 'Join / Wait',
      ar: 'التقاء / انتظار',
      hint: 'Wait for branches before continuing.',
      hintAr: 'انتظار كل الفروع أو أي منها',
    },
  ];
  private readonly locale = inject(LocaleService);
  readonly items = [
    {
      type: 'MainActivity' as const,
      en: 'Task Type',
      ar: 'نوع المهمة',
      hint: 'Owns a child workflow',
      hintAr: 'يحتوي على سير عمل فرعي',
    },
    {
      type: 'UserTask' as const,
      en: 'Step',
      ar: 'خطوة',
      hint: 'One form, with configured routes',
      hintAr: 'نموذج واحد ومسارات محددة',
    },
    {
      type: 'ServiceTask' as const,
      en: 'Events',
      ar: 'الأحداث',
      hint: 'Integrations and automated routing',
      hintAr: 'التكامل والتوجيه الآلي',
    },
  ];
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
}
