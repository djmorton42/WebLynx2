using WebLynx2.Utilities;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class NetworkAddressHelperTests
{
    [Fact]
    public void GetHttpListenerPrefixes_SpecificAddress_ReturnsSinglePrefix()
    {
        var prefixes = NetworkAddressHelper.GetHttpListenerPrefixes(5001, "192.168.0.10");

        Assert.Equal(["http://192.168.0.10:5001/"], prefixes);
    }

    [Fact]
    public void GetHttpListenerPrefixes_Loopback_ReturnsSinglePrefix()
    {
        var prefixes = NetworkAddressHelper.GetHttpListenerPrefixes(8080, "127.0.0.1");

        Assert.Equal(["http://127.0.0.1:8080/"], prefixes);
    }

    [Fact]
    public void GetHttpListenerPrefixes_AllInterfaces_IncludesLoopbackAndLocalAddresses()
    {
        var prefixes = NetworkAddressHelper.GetHttpListenerPrefixes(5001);

        Assert.Contains("http://127.0.0.1:5001/", prefixes);
        Assert.All(prefixes, p =>
        {
            Assert.StartsWith("http://", p, StringComparison.Ordinal);
            Assert.EndsWith(":5001/", p, StringComparison.Ordinal);
            Assert.DoesNotContain('*', p);
            Assert.DoesNotContain('+', p);
        });
    }

    [Fact]
    public void GetHttpListenerPrefixes_InvalidAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NetworkAddressHelper.GetHttpListenerPrefixes(5001, "not-an-ip"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void GetHttpListenerPrefixes_InvalidPort_Throws(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetworkAddressHelper.GetHttpListenerPrefixes(port, "127.0.0.1"));
    }
}
