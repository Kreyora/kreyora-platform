using Kreyora.Domain.Storefront;

namespace Kreyora.UnitTests.Domain;

public class StorePaymentConfigurationTests
{
    private const string TenantId = "01J00000000000000000000001";
    private const string StoreId = "01J00000000000000000000002";

    [Fact]
    public void Create_ValidConfiguration_SetsProperties()
    {
        var config = StorePaymentConfiguration.Create(
            TenantId,
            StoreId,
            codEnabled: true,
            merchantQrEnabled: true,
            merchantQrInstructions: "Scan QR code via eSewa or Fonepay to 9841234567",
            merchantQrMediaAssetId: "01J00000000000000000000003");

        Assert.Equal(TenantId, config.TenantId);
        Assert.Equal(StoreId, config.StoreId);
        Assert.True(config.CodEnabled);
        Assert.True(config.MerchantQrEnabled);
        Assert.Equal("Scan QR code via eSewa or Fonepay to 9841234567", config.MerchantQrInstructions);
        Assert.Equal("01J00000000000000000000003", config.MerchantQrMediaAssetId);
    }

    [Fact]
    public void Create_ThrowsIfBothCodAndMerchantQrAreDisabled()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StorePaymentConfiguration.Create(TenantId, StoreId, codEnabled: false, merchantQrEnabled: false));
    }

    [Fact]
    public void Update_ThrowsIfBothCodAndMerchantQrAreDisabled()
    {
        var config = StorePaymentConfiguration.Create(TenantId, StoreId, codEnabled: true, merchantQrEnabled: true);

        Assert.Throws<InvalidOperationException>(() =>
            config.Update(codEnabled: false, merchantQrEnabled: false, null, null));
    }

    [Fact]
    public void Update_ThrowsIfInstructionsContainHtml()
    {
        var config = StorePaymentConfiguration.Create(TenantId, StoreId, codEnabled: true, merchantQrEnabled: true);

        Assert.Throws<ArgumentException>(() =>
            config.Update(codEnabled: true, merchantQrEnabled: true, "<script>alert(1)</script>", null));
    }
}

