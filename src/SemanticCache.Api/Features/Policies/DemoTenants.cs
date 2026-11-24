namespace SemanticCache.Api.Features.Policies;

/// <summary>The demo lets you pick a tenant. In production it comes from the signed-in user.</summary>
public static class DemoTenants
{
    public const string Acme = "acme";
    public const string Globex = "globex";

    public static bool IsKnown(string tenantId)
    {
        return tenantId is Acme or Globex;
    }
}
