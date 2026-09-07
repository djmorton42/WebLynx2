using WebLynx2.Utilities;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class NetworkAddressHelperTests
{
    [Fact]
    public void GetKestrelUrl_SpecificAddress_ReturnsUrl()
    {
        Assert.Equal("http://192.168.0.10:5001", NetworkAddressHelper.GetKestrelUrl(5001, "192.168.0.10"));
    }

    [Fact]
    public void GetKestrelUrl_Loopback_ReturnsUrl()
    {
        Assert.Equal("http://127.0.0.1:8080", NetworkAddressHelper.GetKestrelUrl(8080, "127.0.0.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("+")]
    public void GetKestrelUrl_AllInterfaces_BindsAny(string? listenAddress)
    {
        Assert.Equal("http://0.0.0.0:5001", NetworkAddressHelper.GetKestrelUrl(5001, listenAddress));
    }

    [Fact]
    public void GetKestrelUrl_InvalidAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NetworkAddressHelper.GetKestrelUrl(5001, "not-an-ip"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void GetKestrelUrl_InvalidPort_Throws(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetworkAddressHelper.GetKestrelUrl(port, "127.0.0.1"));
    }
}
