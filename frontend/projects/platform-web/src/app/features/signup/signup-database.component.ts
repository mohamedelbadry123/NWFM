import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import {
  I18nDirective,
  LanguageService,
  apiErrorMessage,
  emailLinkParam,
  NWFM_LOGO_WITH_SLOGAN,
} from '@platform/shared';
import { finalize } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { InputText } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { Password } from 'primeng/password';
import { MessageService } from 'primeng/api';
import {
  ByoDatabaseInspection,
  PublicApiService,
  SignupOptions,
} from '../../core/api/public-api.service';

@Component({
  selector: 'app-signup-database',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Checkbox,
    InputText,
    InputNumberModule,
    Password,
  ],
  templateUrl: './signup-database.component.html',
  styleUrl: './public-page.scss',
})
export class SignupDatabaseComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(PublicApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly language = inject(LanguageService);
  protected readonly logo = NWFM_LOGO_WITH_SLOGAN;

  protected readonly options = signal<SignupOptions | null>(null);
  protected readonly script = signal<string | null>(null);
  protected readonly inspection = signal<ByoDatabaseInspection | null>(null);
  protected readonly testing = signal(false);
  protected readonly saving = signal(false);

  private signupId = '';
  private token = '';

  protected readonly form = this.fb.nonNullable.group({
    server: ['', Validators.required],
    port: [1433 as number | null],
    database: ['', Validators.required],
    userName: ['', Validators.required],
    password: ['', Validators.required],
    runtimeUserName: ['', Validators.required],
    runtimePassword: ['', Validators.required],
    trustServerCertificate: [false],
  });

  ngOnInit(): void {
    this.signupId = this.route.snapshot.paramMap.get('id') ?? '';
    this.token = emailLinkParam(this.route.snapshot.queryParamMap, 'token');
    this.api.getSignupOptions().subscribe((options) => this.options.set(options));
  }

  protected loadScript(): void {
    const { database, userName, runtimeUserName } = this.form.getRawValue();
    if (!database || !userName || !runtimeUserName) {
      this.form.controls.database.markAsTouched();
      this.form.controls.userName.markAsTouched();
      this.form.controls.runtimeUserName.markAsTouched();
      return;
    }
    this.api.getDatabaseScript(database, userName, runtimeUserName).subscribe({
      next: (result) => this.script.set(result.script),
      error: (error: unknown) =>
        this.messages.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: apiErrorMessage(error),
        }),
    });
  }

  protected test(): void {
    this.run('test');
  }

  protected save(): void {
    this.run('save');
  }

  private run(mode: 'test' | 'save'): void {
    if (this.form.invalid || !this.token) {
      this.form.markAllAsTouched();
      if (!this.token) {
        this.messages.add({
          severity: 'error',
          summary: this.translate.instant('signup.database.tokenRequired'),
        });
      }
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      token: this.token,
      server: value.server,
      port: value.port,
      database: value.database,
      userName: value.userName,
      password: value.password,
      runtimeUserName: value.runtimeUserName,
      runtimePassword: value.runtimePassword,
      trustServerCertificate: value.trustServerCertificate,
    };

    if (mode === 'test') {
      this.testing.set(true);
      this.api
        .testDatabase(this.signupId, body)
        .pipe(finalize(() => this.testing.set(false)))
        .subscribe({
          next: (inspection) => this.inspection.set(inspection),
          error: (error: unknown) =>
            this.messages.add({
              severity: 'error',
              summary: this.translate.instant('common.error'),
              detail: apiErrorMessage(error),
            }),
        });
      return;
    }

    this.saving.set(true);
    this.api
      .saveDatabase(this.signupId, body)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (result) => {
          this.inspection.set(result.inspection);
          if (result.inspection.succeeded) {
            void this.router.navigate(['/signup/status', this.signupId]);
          }
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }
}
