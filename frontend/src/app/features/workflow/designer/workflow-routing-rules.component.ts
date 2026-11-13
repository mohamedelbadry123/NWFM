import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { LocaleService } from '@core/i18n/locale.service';
import type { RoutingRuleTrace } from './workflow-routing.service';

/**
 * A Condition's routes in the order they were tried, as the backend recorded them: which one matched, which were
 * checked and did not match, which could not be evaluated (with the reason) and which were never reached.
 * It only presents the recorded trace; nothing is evaluated here.
 */
@Component({
  selector: 'app-workflow-routing-rules',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol
      class="rules"
      [attr.aria-label]="t('Routes in the order they were tried', 'المسارات بترتيب تجربتها')"
    >
      @for (rule of rules(); track $index) {
        <li class="rule" [attr.data-rule-status]="state(rule)">
          <span class="badge" aria-hidden="true">{{ $index + 1 }}</span>
          <span class="body">
            <strong>
              {{ t('Route', 'المسار') }} {{ $index + 1 }}
              @if (destination()(rule.transitionKey); as name) {
                <span dir="auto"> → {{ name }}</span>
              }
            </strong>
            <span class="state">{{ text(rule) }}</span>
            @if (rule.problem && state(rule) === 'error') {
              <span class="problem" role="alert" dir="auto">{{ rule.problem }}</span>
            }
          </span>
        </li>
      }
    </ol>
  `,
  styles: [
    `
      .rules {
        margin: 4px 0 0;
        padding: 0;
        list-style: none;
        display: grid;
        gap: 4px;
      }
      .rule {
        display: flex;
        gap: 8px;
        align-items: flex-start;
        padding: 4px 6px;
        border-inline-start: 3px solid var(--ds-border-strong, #ccc);
        font-size: 12px;
      }
      .rule[data-rule-status='matched'] {
        border-inline-start-color: var(--ds-success, #15803d);
        background: var(--ds-surface-2, transparent);
      }
      .rule[data-rule-status='error'] {
        border-inline-start-color: var(--ds-danger, #b91c1c);
      }
      .rule[data-rule-status='skipped'] {
        opacity: 0.7;
      }
      .badge {
        flex: none;
        min-width: 20px;
        height: 20px;
        border-radius: 10px;
        text-align: center;
        line-height: 20px;
        font-size: 11px;
        color: #fff;
        background: var(--ds-primary, #5040d6);
      }
      .body {
        display: grid;
        gap: 2px;
        min-width: 0;
        overflow-wrap: anywhere;
      }
      .problem {
        color: var(--ds-danger, #b91c1c);
      }
    `,
  ],
})
export class WorkflowRoutingRulesComponent {
  readonly rules = input<RoutingRuleTrace[]>([]);
  /** Names the destination of a route by its connection key; empty when it cannot be told. */
  readonly destination = input<(key: string) => string>(() => '');
  private readonly locale = inject(LocaleService);

  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  state(rule: RoutingRuleTrace): 'matched' | 'checked' | 'skipped' | 'error' {
    if (rule.problem) return 'error';
    return rule.status === 'Matched'
      ? 'matched'
      : rule.status === 'Skipped'
        ? 'skipped'
        : 'checked';
  }
  text(rule: RoutingRuleTrace): string {
    switch (this.state(rule)) {
      case 'matched':
        return this.t('Matched — this route was chosen.', 'مطابق — تم اختيار هذا المسار.');
      case 'error':
        return this.t(
          'Could not be checked. The workflow stops for attention here; later routes were not tried.',
          'تعذر فحصه. يتوقف سير العمل هنا للمراجعة ولم تُجرَّب المسارات اللاحقة.',
        );
      case 'skipped':
        return this.t(
          'Not tried — an earlier route already decided.',
          'لم يُجرَّب — حسم مسار سابق القرار.',
        );
      default:
        return this.t('Checked — did not match.', 'تم فحصه — غير مطابق.');
    }
  }
}
