namespace Auth.Domain.Constants;

public static class SsoDefaults
{
    public const string Saml2Scheme = "Saml2";
    public const string TempCookieScheme = "SsoTempCookie";
    public const string TempCookieName = ".NWFM.SsoTemp";

    public const string ControllerPath = "/api/v1/auth/sso";
    public const string CallbackPath = "/api/v1/auth/sso/callback";
    public const string ClientCallbackPath = "/auth/saml-callback";
    public const string ClientAccessDeniedPath = "/auth/access-denied";

    public static class DenialReasons
    {
        public const string NotProvisioned = "not_provisioned";
        public const string Inactive = "inactive";
        public const string NoScope = "no_scope";
        public const string CrewAccount = "crew_account";
        public const string ExchangeFailed = "exchange_failed";
        public const string NoNameId = "no_name_id";
    }
}
