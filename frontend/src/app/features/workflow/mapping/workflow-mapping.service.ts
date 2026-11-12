import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiConfiguration } from '@shared/models/api-configuration';
import type {
  EndpointDefinition,
  HttpTestResult,
  MappingAuthoringRequest,
  MappingCatalog,
  MappingDefinitionChoice,
  MappingInvocationDto,
  MappingPreviewRequest,
  MappingPreviewResult,
  MappingSchema,
  MappingValidationResult,
  PreparedMappingInputsDto,
  PreparedMappingRequest,
  SchemaImportRequest,
} from './mapping.models';

/**
 * The authoritative mapping APIs. The browser never evaluates a mapping: catalog, validation, preview and
 * schema import/inference all run on the server. `root*` is always the pinned MAIN tree root, including when
 * the mapping belongs to a child workflow.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowMappingService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiConfiguration);

  private mapping(rootDefinitionId: string, rootVersionId: string) {
    return `${this.api.rootUrl}/api/workflow/definitions/${rootDefinitionId}/versions/${rootVersionId}/mapping`;
  }
  private get invocations() {
    return `${this.api.rootUrl}/api/workflow/mapping-invocations`;
  }

  catalog(rootDefinitionId: string, rootVersionId: string) {
    return this.http.get<MappingCatalog>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/sources`,
    );
  }
  /** Published workflows the signed-in user may read from; no values and no criteria are involved. */
  relatedDefinitions(rootDefinitionId: string, rootVersionId: string) {
    return this.http.get<MappingDefinitionChoice[]>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/related-definitions`,
    );
  }
  /** Related workflow catalogs are browsed through their own pinned version (never the current tree). */
  relatedCatalog(definitionId: string, versionId: string) {
    return this.catalog(definitionId, versionId);
  }
  validate(rootDefinitionId: string, rootVersionId: string, body: MappingAuthoringRequest) {
    return this.http.post<MappingValidationResult>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/validate`,
      body,
    );
  }
  preview(rootDefinitionId: string, rootVersionId: string, body: MappingPreviewRequest) {
    return this.http.post<MappingPreviewResult>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/preview`,
      body,
    );
  }
  importOpenApi(rootDefinitionId: string, rootVersionId: string, body: SchemaImportRequest) {
    return this.http.post<EndpointDefinition>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/import-openapi`,
      body,
    );
  }
  validateSchema(rootDefinitionId: string, rootVersionId: string, endpoint: EndpointDefinition) {
    return this.http.post<EndpointDefinition>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/validate-schema`,
      endpoint,
    );
  }
  inferResponse(rootDefinitionId: string, rootVersionId: string, sample: unknown) {
    return this.http.post<MappingSchema>(
      `${this.mapping(rootDefinitionId, rootVersionId)}/infer-response`,
      sample,
    );
  }
  /**
   * Send Test through the protected integration endpoint. The prepared request carries ONLY the evaluated
   * values; the connection, credentials and origin stay on the server. Nothing here dispatches follow-ups
   * or writes workflow data.
   */
  sendTest(configuration: unknown, preparedRequest: PreparedMappingRequest) {
    return this.http.post<HttpTestResult>(
      `${this.api.rootUrl}/api/workflow/integrations/http/test`,
      {
        configuration,
        variables: {},
        preparedRequest,
      },
    );
  }

  invocationsForRoot(rootInstanceId: string) {
    return this.http.get<MappingInvocationDto[]>(this.invocations, { params: { rootInstanceId } });
  }
  retryInvocation(id: string) {
    return this.http.post<void>(`${this.invocations}/${id}/retry`, {});
  }
  preparedInputs(id: string) {
    return this.http.get<PreparedMappingInputsDto>(`${this.invocations}/${id}/prepared-inputs`);
  }
}
