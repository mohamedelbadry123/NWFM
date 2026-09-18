import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { AppContextService } from './core/context/app-context.service';
import { LocaleService } from './core/i18n/locale.service';
import { ToastService } from './core/notifications/toast.service';
import { AuthStore } from './core/auth/auth.store';
import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, FormsModule, TranslateModule, ButtonModule],
  templateUrl: './app.component.html'
})
export class AppComponent {
  readonly context = inject(AppContextService);
  readonly locale = inject(LocaleService);
  readonly toast = inject(ToastService);
  readonly authStore = inject(AuthStore);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  reload() { window.location.reload(); }

  readonly links = [
    { url: '/org/workflow', en: 'Overview', ar: 'نظرة عامة' },
    { url: '/workflow/start', en: 'Start workflow', ar: 'بدء سير عمل' },
    { url: '/admin/workflow/definitions', en: 'Designer & definitions', ar: 'التصميم والتعريفات' },
    { url: '/admin/workflow/bindings', en: 'Bindings', ar: 'الارتباطات' },
    { url: '/org/workflow/tasks', en: 'Tasks', ar: 'المهام' },
    { url: '/org/workflow/requests', en: 'Requests', ar: 'الطلبات' },
    { url: '/org/workflow/participants', en: 'Participants', ar: 'المشاركون' },
    { url: '/org/workflow/assignment-groups', en: 'Assignment groups', ar: 'مجموعات الإسناد' },
    { url: '/org/workflow/departments', en: 'Departments', ar: 'الأقسام' },
    { url: '/admin/workflow/instances', en: 'Execution monitor', ar: 'مراقبة التنفيذ' },
    { url: '/org/workflow/workload', en: 'Workload', ar: 'عبء العمل' },
    { url: '/org/workflow/notifications', en: 'Notifications', ar: 'الإشعارات' },
    { url: '/admin/workflow/incidents', en: 'Incidents', ar: 'الحوادث' },
    { url: '/admin/workflow/dead-letters', en: 'Message recovery', ar: 'استعادة الرسائل' },
    { url: '/admin/workflow/calendars', en: 'Business calendars', ar: 'تقويم العمل' },
    { url: '/admin/workflow/sla-policies', en: 'SLA policies', ar: 'سياسات الخدمة' },
    { url: '/admin/workflow/actions-catalog', en: 'Actions catalog', ar: 'دليل الإجراءات' },
    { url: '/admin/workflow/modules', en: 'Workflow catalog', ar: 'دليل سير العمل' },
    { url: '/org/workflow/help', en: 'Help', ar: 'المساعدة' }
  ];

  onLogout(): void {
    const refreshToken = this.authStore.refreshToken();
    this.authService.logout(refreshToken ?? undefined).subscribe({
      complete: () => {
        this.authStore.clearSession();
        this.router.navigate(['/login']);
      },
      error: () => {
        this.authStore.clearSession();
        this.router.navigate(['/login']);
      }
    });
  }
}
