import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowDefinitionsDefinitionIdVersionsGet } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-get';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdGet } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-get';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdXmlGet } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-xml-get';
import { apiWorkflowDefinitionsDefinitionIdVersionsPost } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-post';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdXmlPut } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-xml-put';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdValidatePost } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-validate-post';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdPublishPost } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-publish-post';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdClonePost } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-clone-post';
import { apiWorkflowDefinitionsDefinitionIdVersionsVersionIdRetirePost } from '@shared/models/fn/workflow-versions/api-workflow-definitions-definition-id-versions-version-id-retire-post';
import type { WorkflowVersionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-dto';
import type { WorkflowVersionDetailDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-detail-dto';
import type { WorkflowValidationResultDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-validation-result-dto';
import type { CreateWorkflowDraftRequest } from '@shared/models/models/Workflow/Api/Controllers/create-workflow-draft-request';
import type { CloneWorkflowVersionRequest } from '@shared/models/models/Workflow/Api/Controllers/clone-workflow-version-request';
import type { SaveWorkflowDraftXmlRequest } from '@shared/models/models/Workflow/Api/Controllers/save-workflow-draft-xml-request';
import type { PaginatedResultOfWorkflowVersionDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-version-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowVersionsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getPaged(definitionId: string, page: number, pageSize: number): Observable<PaginatedResultOfWorkflowVersionDto> {
    return apiWorkflowDefinitionsDefinitionIdVersionsGet(this.http, this.config.rootUrl, {
      definitionId,
      page,
      pageSize,
    }).pipe(map(r => r.body as PaginatedResultOfWorkflowVersionDto));
  }

  getById(definitionId: string, versionId: string): Observable<WorkflowVersionDetailDto> {
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdGet(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
    }).pipe(map(r => r.body as WorkflowVersionDetailDto));
  }

  getXml(definitionId: string, versionId: string): Observable<string> {
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdXmlGet(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
    }).pipe(map(r => r.body as string));
  }

  createDraft(definitionId: string, changeSummary?: string): Observable<WorkflowVersionDto> {
    const body: CreateWorkflowDraftRequest = { changeSummary };
    return apiWorkflowDefinitionsDefinitionIdVersionsPost(this.http, this.config.rootUrl, {
      definitionId,
      body,
    }).pipe(map(r => r.body as WorkflowVersionDto));
  }

  saveXml(
    definitionId: string,
    versionId: string,
    xmlContent: string,
    designerJson?: string
  ): Observable<WorkflowVersionDto> {
    const body: SaveWorkflowDraftXmlRequest = { xmlContent, designerJson };
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdXmlPut(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
      body,
    }).pipe(map(r => r.body as WorkflowVersionDto));
  }

  validate(definitionId: string, versionId: string): Observable<WorkflowValidationResultDto> {
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdValidatePost(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
    }).pipe(map(r => r.body as WorkflowValidationResultDto));
  }

  publish(definitionId: string, versionId: string): Observable<WorkflowVersionDto> {
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdPublishPost(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
    }).pipe(map(r => r.body as WorkflowVersionDto));
  }

  clone(definitionId: string, versionId: string, changeSummary?: string): Observable<WorkflowVersionDto> {
    const body: CloneWorkflowVersionRequest = { changeSummary };
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdClonePost(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
      body,
    }).pipe(map(r => r.body as WorkflowVersionDto));
  }

  retire(definitionId: string, versionId: string): Observable<WorkflowVersionDto> {
    return apiWorkflowDefinitionsDefinitionIdVersionsVersionIdRetirePost(this.http, this.config.rootUrl, {
      definitionId,
      versionId,
    }).pipe(map(r => r.body as WorkflowVersionDto));
  }

  /** Hand-extended until OpenAPI regen — publish preview / DoD checklist. */
  getPublishPreview(definitionId: string, versionId: string): Observable<WorkflowPublishPreviewView> {
    const url = `${this.config.rootUrl}/api/workflow/definitions/${definitionId}/versions/${versionId}/publish-preview`;
    return this.http.get<WorkflowPublishPreviewView>(url);
  }
}

export interface WorkflowPublishPreviewView {
  versionId: string;
  workflowDefinitionId: string;
  versionNumber: number;
  status: string;
  validationStatus: string;
  canPublish: boolean;
  blockingReasons: string[];
  warnings: string[];
  activityCount: number;
  transitionCount: number;
  variableCount: number;
  requiredAssignmentKeys: string[];
  latestPublishedVersionId?: string | null;
  latestPublishedVersionNumber?: number | null;
  changeSummary?: string | null;
}
