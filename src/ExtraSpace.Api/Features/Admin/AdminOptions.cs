namespace ExtraSpace.Api.Features.Admin;

internal sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>First staff emails (S0 spec §6.2); used only while no active Owner exists. Remove once one Owner exists.</summary>
    public string[] BootstrapEmails { get; set; } = [];

    public EntraOptions Entra { get; set; } = new();
}

internal sealed class EntraOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => TenantId.Length > 0 && ClientId.Length > 0 && ClientSecret.Length > 0;
}
