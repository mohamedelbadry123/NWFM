namespace NWFM.Shared.Constants;

public static class CacheKeys
{
    private const string Prefix = "NWFM";

    public static class Auth
    {
        public static class Permissions
        {
            public static string ForUser(string userId) => $"{Prefix}:Auth:Permissions:User:{userId}";
            public static string ForRole(string roleId) => $"{Prefix}:Auth:Permissions:Role:{roleId}";
        }

        public static class Otp
        {
            public static string ForChallenge(string challengeId) => $"{Prefix}:Auth:Otp:Challenge:{challengeId}";
        }
    }

    public static class Lookups
    {
        public const string OrgHierarchy = $"{Prefix}:Lookups:OrgHierarchy";
        public const string Departments = $"{Prefix}:Lookups:Departments";
        public const string Clusters = $"{Prefix}:Lookups:Clusters";
        public const string Cbus = $"{Prefix}:Lookups:Cbus";
        public const string Branches = $"{Prefix}:Lookups:Branches";
        public const string OperationAreas = $"{Prefix}:Lookups:OperationAreas";
    }

    public static class FormEngine
    {
        public const string FieldCatalog = $"{Prefix}:FormEngine:FieldCatalog";
    }

    public static class Tasks
    {
        public const string C2mActionMappings = $"{Prefix}:Tasks:C2mActionMappings";
    }
}
