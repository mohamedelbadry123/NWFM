import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResult } from '../api/api-result';

export interface PermissionDto {
  id: string;
  code: string;
  module: string;
  nameEn: string;
  nameAr: string;
  isActive: boolean;
}

export interface RoleDto {
  id: string;
  name: string;
  permissionCodes: string[];
}

export interface RolePermissionsDto {
  roleId: string;
  roleName: string;
  permissions: PermissionDto[];
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/admin';

  getPermissions(): Observable<ApiResult<PermissionDto[]>> {
    return this.http.get<ApiResult<PermissionDto[]>>(`${this.baseUrl}/permissions`);
  }

  getRoles(): Observable<ApiResult<RoleDto[]>> {
    return this.http.get<ApiResult<RoleDto[]>>(`${this.baseUrl}/roles`);
  }

  assignRolePermissions(roleName: string, permissionCodes: string[]): Observable<ApiResult<RolePermissionsDto>> {
    return this.http.put<ApiResult<RolePermissionsDto>>(`${this.baseUrl}/roles/${encodeURIComponent(roleName)}/permissions`, {
      roleName,
      permissionCodes,
    });
  }
}
