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

/** A workspace token minted for platform support from a redeemed handoff code. */
export interface SupportSessionToken {
  accessToken: string;
  expiresInSeconds: number;
  sessionId: string;
  tenantKey: string;
  supportScope: string;
  impersonatedUserName: string;
  actorName: string;
  roles: string[];
  permissions: string[];
}
