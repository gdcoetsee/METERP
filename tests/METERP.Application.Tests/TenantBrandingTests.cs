using METERP.Application.Models;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class TenantBrandingTests
{
    [Fact]
    public void OfficeLabel_MetTenant_IgnoresAcmePlaceholder()
    {
        var tenant = new Tenant
        {
            Name = "MET Electrical",
            Subdomain = "acme",
            BrandDisplayName = "Acme Electrical"
        };

        Assert.Equal("MET Electrical", TenantBranding.OfficeLabel(tenant));
        Assert.Equal("MET Electrical", TenantBranding.From(tenant).DisplayName);
    }

    [Fact]
    public void From_CopiesVatNumber_AndDropsBlank()
    {
        var set = new Tenant { Name = "MET Electrical", VatNumber = " 4012345678 " };
        Assert.Equal("4012345678", TenantBranding.From(set).VatNumber);

        var blank = new Tenant { Name = "MET Electrical", VatNumber = "  " };
        Assert.Null(TenantBranding.From(blank).VatNumber);
        Assert.Null(TenantBranding.From(null).VatNumber);
    }

    [Fact]
    public void OfficeLabel_MetTenant_KeepsExistingMetBrand()
    {
        var tenant = new Tenant
        {
            Name = "MET Electrical",
            BrandDisplayName = "MET Electrical"
        };

        Assert.Equal("MET Electrical", TenantBranding.OfficeLabel(tenant));
        Assert.Equal("MET Electrical", TenantBranding.ResolveSeedBrandDisplayName(tenant));
    }

    [Fact]
    public void ResolveSeedBrandDisplayName_DoesNotStampAcmeOntoMetTenant()
    {
        var tenant = new Tenant
        {
            Name = "MET Electrical",
            Subdomain = "acme",
            BrandDisplayName = null
        };

        Assert.Null(TenantBranding.ResolveSeedBrandDisplayName(tenant));
        Assert.Equal("MET Electrical", TenantBranding.OfficeLabel(tenant));
    }

    [Fact]
    public void ResolveSeedBrandDisplayName_CorrectsAcmePlaceholderOnMetTenant()
    {
        var tenant = new Tenant
        {
            Name = "MET Electrical",
            Subdomain = "acme",
            BrandDisplayName = "Acme Corp"
        };

        Assert.Equal("MET Electrical", TenantBranding.ResolveSeedBrandDisplayName(tenant));
    }

    [Fact]
    public void ResolveSeedBrandDisplayName_SetsAcmeOnlyForTrueAcmeTenant()
    {
        var acme = new Tenant
        {
            Name = "Acme Electrical (Demo)",
            Subdomain = "acme",
            BrandDisplayName = null
        };

        Assert.Equal("Acme Electrical", TenantBranding.ResolveSeedBrandDisplayName(acme));
        Assert.False(TenantBranding.IsMetOfficeTenant(acme));

        acme.BrandDisplayName = "Acme Electrical";
        Assert.Equal("Acme Electrical", TenantBranding.ResolveSeedBrandDisplayName(acme));
        Assert.Equal("Acme Electrical", TenantBranding.OfficeLabel(acme));
    }
}
