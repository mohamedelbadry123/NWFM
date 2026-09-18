import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowBindingsBindingIdGet } from '@shared/models/fn/workflow-bindings-catalog/api-workflow-bindings-binding-id-get';
import { apiWorkflowBindingsGet } from '@shared/models/fn/workflow-bindings-catalog/api-workflow-bindings-get';
import { apiWorkflowBindingsOrganizationsOrganizationIdGroupsGet } from '@shared/models/fn/workflow-bindings-catalog/api-workflow-bindings-organizations-organization-id-groups-get';
import { apiWorkflowDefinitionsDefinitionIdBindingsGet } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-get';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdGet } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-get';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdActivatePost } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-activate-post';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdDeactivatePost } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-deactivate-post';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsGet } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-organizations-organization-id-mappings-get';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsPost } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-organizations-organization-id-mappings-post';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsMappingIdPut } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-organizations-organization-id-mappings-mapping-id-put';
import { apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsMappingIdDelete } from '@shared/models/fn/workflow-bindings/api-workflow-definitions-definition-id-bindings-binding-id-organizations-organization-id-mappings-mapping-id-delete';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { WorkflowBindingAssignmentMappingDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-binding-assignment-mapping-dto';
import type { CreateWorkflowBindingAssignmentMappingRequest } from '@shared/models/models/Workflow/Api/Controllers/create-workflow-binding-assignment-mapping-request';
import type { UpdateWorkflowBindingAssignmentMappingRequest } from '@shared/models/models/Workflow/Api/Controllers/update-workflow-binding-assignment-mapping-request';
import type { PaginatedResultOfWorkflowBindingDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-binding-dto';
import type {
  CreateWorkflowBindingPayload,
  UpdateWorkflowBindingPayload,
  WorkflowBindingViewModel,
} from '@core/models/workflow-binding.models';
import type {
  WorkflowBindingSimulateRequest,
  WorkflowBindingSimulateResult,
} from '@core/models/workflow-ops.models';

@Injectable({ providedIn: 'root' })
export class WorkflowBindingsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getByIdForAdmin(bindingId: string): Observable<WorkflowBindingViewModel> {
    return apiWorkflowBindingsBindingIdGet(this.http, this.config.rootUrl, { bindingId }).pipe(
      map(r => r.body as WorkflowBindingViewModel)
    );
  }

  listAll(
    page: number,
    pageSize: number,
    definitionId?: string,
    organizationId?: string
  ): Observable<PaginatedResultOfWorkflowBindingDto> {
    return apiWorkflowBindingsGet(this.http, this.config.rootUrl, {
      page,
      pageSize,
      definitionId,
      organizationId,
    }).pipe(map(r => r.body as PaginatedResultOfWorkflowBindingDto));
  }

  listByDefinition(
    definitionId: string,
    page: number,
    pageSize: number
  ): Observable<PaginatedResultOfWorkflowBindingDto> {
    return apiWorkflowDefinitionsDefinitionIdBindingsGet(this.http, this.config.rootUrl, {
      definitionId,
      page,
      pageSize,
    }).pipe(map(r => r.body as PaginatedResultOfWorkflowBindingDto));
  }

  getById(definitionId: string, bindingId: string): Observable<WorkflowBindingViewModel> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdGet(this.http, this.config.rootUrl, {
      definitionId,
      bindingId,
    }).pipe(map(r => r.body as WorkflowBindingViewModel));
  }

  /** Hand-extended create — sends SaaS fields (screenKey, mappings) without regenerating OpenAPI. */
  create(definitionId: string, req: CreateWorkflowBindingPayload): Observable<WorkflowBindingViewModel> {
    const url = `${this.config.rootUrl}/api/workflow/definitions/${definitionId}/bindings`;
    return this.http.post<WorkflowBindingViewModel>(url, req);
  }

  /** Hand-extended update — sends SaaS fields (screenKey, mappings) without regenerating OpenAPI. */
  update(
    definitionId: string,
    bindingId: string,
    req: UpdateWorkflowBindingPayload
  ): Observable<WorkflowBindingViewModel> {
    const url = `${this.config.rootUrl}/api/workflow/definitions/${definitionId}/bindings/${bindingId}`;
    return this.http.put<WorkflowBindingViewModel>(url, req);
  }

  activate(definitionId: string, bindingId: string): Observable<void> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdActivatePost(this.http, this.config.rootUrl, {
      definitionId,
      bindingId,
    }).pipe(map(() => undefined));
  }

  deactivate(definitionId: string, bindingId: string): Observable<void> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdDeactivatePost(this.http, this.config.rootUrl, {
      definitionId,
      bindingId,
    }).pipe(map(() => undefined));
  }

  listOrgGroups(organizationId: string): Observable<WorkflowAssignmentGroupDto[]> {
    return apiWorkflowBindingsOrganizationsOrganizationIdGroupsGet(this.http, this.config.rootUrl, {
      organizationId,
    }).pipe(map(r => r.body as WorkflowAssignmentGroupDto[]));
  }

  listMappings(
    definitionId: string,
    bindingId: string,
    organizationId: string
  ): Observable<WorkflowBindingAssignmentMappingDto[]> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsGet(
      this.http,
      this.config.rootUrl,
      { definitionId, bindingId, organizationId }
    ).pipe(map(r => r.body as WorkflowBindingAssignmentMappingDto[]));
  }

  createMapping(
    definitionId: string,
    bindingId: string,
    organizationId: string,
    req: CreateWorkflowBindingAssignmentMappingRequest
  ): Observable<WorkflowBindingAssignmentMappingDto> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsPost(
      this.http,
      this.config.rootUrl,
      { definitionId, bindingId, organizationId, body: req }
    ).pipe(map(r => r.body as WorkflowBindingAssignmentMappingDto));
  }

  updateMapping(
    definitionId: string,
    bindingId: string,
    organizationId: string,
    mappingId: string,
    req: UpdateWorkflowBindingAssignmentMappingRequest
  ): Observable<WorkflowBindingAssignmentMappingDto> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsMappingIdPut(
      this.http,
      this.config.rootUrl,
      { definitionId, bindingId, organizationId, mappingId, body: req }
    ).pipe(map(r => r.body as WorkflowBindingAssignmentMappingDto));
  }

  deleteMapping(
    definitionId: string,
    bindingId: string,
    organizationId: string,
    mappingId: string
  ): Observable<void> {
    return apiWorkflowDefinitionsDefinitionIdBindingsBindingIdOrganizationsOrganizationIdMappingsMappingIdDelete(
      this.http,
      this.config.rootUrl,
      { definitionId, bindingId, organizationId, mappingId }
    ).pipe(map(() => undefined));
  }

  /** Hand-extended — dry-run a binding against a sample payload. */
  simulate(
    bindingId: string,
    req: WorkflowBindingSimulateRequest
  ): Observable<WorkflowBindingSimulateResult> {
    const url = `${this.config.rootUrl}/api/workflow/bindings/${bindingId}/simulate`;
    return this.http.post<WorkflowBindingSimulateResult>(url, req);
  }

  /** Hand-extended until OpenAPI regen — binding readiness (mapped assignment keys). */
  getReadiness(definitionId: string, bindingId: string): Observable<WorkflowBindingReadinessView> {
    const url = `${this.config.rootUrl}/api/workflow/definitions/${definitionId}/bindings/${bindingId}/readiness`;
    return this.http.get<WorkflowBindingReadinessView>(url);
  }
}

export interface WorkflowBindingReadinessView {
  bindingId: string;
  organizationId: string;
  resolvedVersionId?: string | null;
  isReady: boolean;
  requiredAssignmentKeys: string[];
  mappedAssignmentKeys: string[];
  unmappedAssignmentKeys: string[];
  blockingReason?: string | null;
}
