export interface AuthTokenResponse {
  value: {
    accessToken: string;
    refreshToken: string;
    expiresInSeconds: number;
    userName: string;
    roles: string[];
    permissions: string[];
  };
  isSuccess: boolean;
  error: { code: string; message: string };
}

export interface CurrentUser {
  userId: string;
  userName: string;
  email: string | null;
  roles: string[];
  permissions: string[];
  teamId: number | null;
  isUnrestrictedScope: boolean;
}

export interface SsoStatus {
  enabled: boolean;
  allowLocalLoginForAdministrators: boolean;
}
