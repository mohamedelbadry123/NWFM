import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PrvAvatarSize = 'xs' | 'sm' | 'md' | 'lg';
export type PrvAvatarStatus = 'online' | 'offline' | 'busy' | 'none';

/**
 * prv-avatar — user avatar with image fallback to initials. Adapted from TailAdmin's avatar.
 *
 *   <prv-avatar name="Sara Al-Otaibi" [src]="user.photo" size="md" status="online" />
 */
@Component({
  selector: 'prv-avatar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span [class]="wrapClass()">
      @if (src()) {
        <img [src]="src()" [alt]="name()" class="w-full h-full object-cover rounded-full" />
      } @else {
        <span class="font-semibold text-primary-700 dark:text-primary-200">{{ initials() }}</span>
      }
      @if (status() !== 'none') {
        <span [class]="dotClass()"></span>
      }
    </span>
  `,
  styles: [':host { display: inline-flex; }'],
})
export class PrvAvatarComponent {
  readonly name = input('');
  readonly src = input('');
  readonly size = input<PrvAvatarSize>('md');
  readonly status = input<PrvAvatarStatus>('none');

  private static readonly SIZES: Record<PrvAvatarSize, string> = {
    xs: 'w-7 h-7 text-[11px]',
    sm: 'w-9 h-9 text-xs',
    md: 'w-11 h-11 text-sm',
    lg: 'w-14 h-14 text-base',
  };

  private static readonly STATUS: Record<PrvAvatarStatus, string> = {
    online: 'bg-success-500',
    offline: 'bg-ink-300',
    busy: 'bg-danger-500',
    none: '',
  };

  protected readonly initials = computed(() => {
    const parts = this.name().trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return '?';
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  });

  protected wrapClass(): string {
    return (
      'relative inline-flex items-center justify-center rounded-full bg-primary-50 ' +
      'dark:bg-primary/15 flex-shrink-0 ' +
      PrvAvatarComponent.SIZES[this.size()]
    );
  }

  protected dotClass(): string {
    return (
      'absolute bottom-0 end-0 w-2.5 h-2.5 rounded-full ring-2 ring-white dark:ring-surface-800 ' +
      PrvAvatarComponent.STATUS[this.status()]
    );
  }
}
