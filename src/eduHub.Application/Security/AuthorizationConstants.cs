namespace eduHub.Application.Security;

public static class AuthorizationConstants
{
    public static class Policies
    {
        public const string PlatformAdmin = "PlatformAdmin";
        public const string OrgAdmin = "OrgAdmin";
        public const string Approver = "Approver";
        public const string OrgUser = "OrgUser";
    }
}
