import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthStore } from '@core/auth/auth.store';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import type { WorkflowDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-definition-dto';
import { WorkflowGeographyComponent } from './workflow-geography.component';
import { WorkflowWorkspaceService, WorkspaceSettings, WorkspaceWorkflow, WorkspaceInstance } from './workflow-workspace.service';

@Component({ selector: 'app-workflow-workspace', standalone: true, imports: [FormsModule, RouterLink, DatePipe, WorkflowGeographyComponent], template: `
<main class="p-5 sm:p-8 space-y-6 max-w-7xl mx-auto">
  <header class="flex items-center justify-between gap-4"><div><h1 class="text-2xl font-semibold">{{ title() }}</h1><p class="text-sm opacity-70 mt-2">{{ subtitle() }}</p></div>
  @if (mode() === 'workflows') {<button class="wf-btn-primary" (click)="creating.set(!creating())">{{ t('Create workflow','إنشاء سير عمل') }}</button>}</header>
  @if (error()) {<p role="alert" class="rounded-xl border border-red-300 bg-red-50 text-red-800 p-4">{{ error() }}</p>}
  @if (creating()) {
    <form (ngSubmit)="create()" class="rounded-xl border p-5 space-y-4 bg-white dark:bg-dark-800">
      <div class="grid gap-4 sm:grid-cols-2"><label>{{ t('Workflow name','اسم سير العمل') }}<input class="wf-input" name="name" [(ngModel)]="name" required maxlength="200"></label><label>{{ t('Arabic name','الاسم بالعربية') }}<input class="wf-input" name="nameAr" [(ngModel)]="nameAr" maxlength="200"></label></div>
      <app-workflow-geography [settings]="settings" (settingsChange)="settings=$event" />
      <div class="flex gap-3"><button class="wf-btn-primary" [disabled]="busy() || !validCreate()">{{ t('Create and open designer','إنشاء وفتح المصمم') }}</button><button class="wf-btn-secondary" type="button" (click)="creating.set(false)">{{ t('Cancel','إلغاء') }}</button></div>
    </form>
  }
  @if (mode() === 'start') {
    <form (ngSubmit)="start()" class="rounded-xl border p-6 space-y-5 max-w-2xl bg-white dark:bg-dark-800">
      <label class="block">{{ t('Published workflow','سير عمل منشور') }}<select class="wf-input" name="workflow" [(ngModel)]="workflowId" required><option value="">{{ t('Select a workflow','اختر سير عمل') }}</option>@for (workflow of catalog(); track workflow.id) {<option [value]="workflow.id">{{ workflow.name }} · {{ t('Version','الإصدار') }} {{ workflow.versionNumber }}</option>}</select></label>
      <label class="block">{{ t('Reference (optional)','المرجع (اختياري)') }}<input class="wf-input" name="reference" [(ngModel)]="reference" maxlength="200"></label>
      @if (administrator()) {<label class="flex gap-2 items-center"><input type="checkbox" name="demo" [(ngModel)]="isDemo">{{ t('Demo instance — allow testing as demo users','نسخة تجريبية — السماح بالتجربة كمستخدمين تجريبيين') }}</label>}
      <button class="wf-btn-primary" [disabled]="busy() || !workflowId">{{ t('Start instance','بدء النسخة') }}</button>
      @if (!loading() && !catalog().length) {<p>{{ t('Publish a main workflow before starting an instance.','انشر سير عمل رئيسي قبل بدء نسخة.') }}</p>}
    </form>
  } @else {
    <form (ngSubmit)="load()" class="flex gap-3"><input class="wf-input" name="search" [(ngModel)]="search" [placeholder]="t('Search by name or reference','البحث بالاسم أو المرجع')"><button class="wf-btn-secondary">{{ t('Search','بحث') }}</button></form>
    @if (loading()) {<p aria-live="polite">{{ t('Loading…','جارٍ التحميل…') }}</p>}
    @if (mode() === 'workflows') {
      <div class="grid gap-4 md:grid-cols-2">@for (workflow of definitions(); track workflow.id) {
        <article class="rounded-xl border p-5 bg-white dark:bg-dark-800"><h2 class="text-lg font-medium">{{ workflow.name }}</h2><p class="text-sm opacity-70 my-2">{{ workflow.description }}</p><p class="text-sm mb-4">{{ workflow.versionCount }} {{ t('versions','إصدارات') }} · {{ workflow.isActive ? t('Active','نشط') : t('Inactive','غير نشط') }}</p><a class="wf-btn-secondary inline-flex" [routerLink]="['/admin/workflow/definitions', workflow.id, 'versions']">{{ t('Open versions and designer','فتح الإصدارات والمصمم') }}</a></article>
      } @empty { @if (!loading()) {<p>{{ t('No workflows yet. Create your first workflow.','لا توجد مسارات عمل. أنشئ أول سير عمل.') }}</p>} }</div>
    } @else {
      <div class="overflow-x-auto rounded-xl border bg-white dark:bg-dark-800"><table class="w-full text-start"><thead><tr class="border-b"><th class="p-4 text-start">{{ t('Workflow','سير العمل') }}</th><th class="p-4 text-start">{{ t('Reference','المرجع') }}</th><th class="p-4 text-start">{{ t('Status','الحالة') }}</th><th class="p-4 text-start">{{ t('Started','تاريخ البدء') }}</th></tr></thead><tbody>
      @for (instance of instances(); track instance.id) {<tr class="border-b last:border-0"><td class="p-4"><a class="text-primary font-medium underline" [routerLink]="['/admin/workflow/instances',instance.id]">{{ instance.name }}</a>@if (instance.isDemo) {<span class="text-xs ms-2 rounded bg-amber-100 text-amber-900 px-2 py-1">{{ t('Demo','تجريبي') }}</span>}</td><td class="p-4">{{ instance.reference || '—' }}</td><td class="p-4">{{ instance.status }}</td><td class="p-4">{{ instance.startedAt | date:'medium' }}</td></tr>}
      </tbody></table>@if (!loading() && !instances().length) {<p class="p-5">{{ t('No instances found.','لا توجد نسخ.') }}</p>}</div>
    }
  }
</main>` })
export class WorkflowWorkspaceComponent implements OnInit {
  private readonly api = inject(WorkflowWorkspaceService); private readonly definitionsApi = inject(WorkflowDefinitionsService);
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly locale = inject(LocaleService); private readonly auth = inject(AuthStore);
  readonly mode = signal('workflows'); readonly creating = signal(false); readonly loading = signal(true); readonly busy = signal(false); readonly error = signal('');
  readonly definitions = signal<WorkflowDefinitionDto[]>([]); readonly catalog = signal<WorkspaceWorkflow[]>([]); readonly instances = signal<WorkspaceInstance[]>([]);
  name = ''; nameAr = ''; search = ''; settings: WorkspaceSettings = { kind: 'Main' }; workflowId = ''; reference = ''; isDemo = false;
  private requestId = crypto.randomUUID();
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  administrator() { return this.auth.roles().includes('Administrator'); }
  title() { return this.mode() === 'workflows' ? this.t('Workflows','مسارات العمل') : this.mode() === 'start' ? this.t('New instance','نسخة جديدة') : this.t('Instances','النسخ'); }
  subtitle() { return this.mode() === 'workflows' ? this.t('Design the activities, responsibilities and steps for your use case.','صمم الأنشطة والمسؤوليات والخطوات لحالة الاستخدام.') : this.mode() === 'start' ? this.t('Start a published workflow and follow its activities.','ابدأ سير عمل منشوراً وتابع أنشطته.') : this.t('Open an instance to work through its activities and child workflows.','افتح نسخة لتنفيذ أنشطتها ومساراتها الفرعية.'); }
  ngOnInit() { this.route.data.subscribe(data => {this.mode.set(data['workspaceView'] || 'workflows');this.creating.set(false);this.load();}); }
  load() { this.loading.set(true);this.error.set(''); const failure = (e: {error?:{message?:string}}) => {this.error.set(e.error?.message || this.t('Could not load this page.','تعذر تحميل الصفحة.'));this.loading.set(false);};
    if (this.mode() === 'workflows') this.definitionsApi.getPaged(1,200,this.search || undefined).subscribe({next:r=>{this.definitions.set(r.items || []);this.loading.set(false);},error:failure});
    else if (this.mode() === 'start') this.api.catalog().subscribe({next:r=>{this.catalog.set(r);this.loading.set(false);},error:failure});
    else this.api.instances(this.search).subscribe({next:r=>{this.instances.set(r);this.loading.set(false);},error:failure});
  }
  validCreate() { return !!this.name.trim() && (this.settings.kind === 'Child' || !!(this.settings.clusterCode && this.settings.regionCode && this.settings.cityCode)); }
  create() { if (!this.validCreate() || this.busy()) return; this.busy.set(true); this.error.set(''); this.api.create(this.name,this.nameAr,this.settings).subscribe({next:r=>{this.busy.set(false);this.router.navigate(['/admin/workflow/definitions',r.definitionId,'versions',r.versionId,'designer']);},error:e=>{this.busy.set(false);this.error.set(e.error?.message || 'Could not create workflow.');}}); }
  start() { if (!this.workflowId || this.busy()) return;this.busy.set(true);this.error.set('');this.api.start(this.workflowId,this.requestId,this.reference,this.isDemo).subscribe({next:r=>{this.busy.set(false);this.requestId=crypto.randomUUID();this.router.navigate(['/admin/workflow/instances',r.instanceId]);},error:e=>{this.busy.set(false);this.error.set(e.error?.message || 'Could not start instance.');}}); }
}
