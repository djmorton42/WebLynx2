using WebLynx2.Api;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class ViewConfigBuilderTests
{
    [Fact]
    public void FromFlatKeyValues_Empty_ReturnsEmptyObject()
    {
        var result = ViewConfigBuilder.FromFlatKeyValues(new Dictionary<string, string>());

        Assert.Empty(result);
    }

    [Fact]
    public void FromFlatKeyValues_NestsDotNotationKeys()
    {
        var flat = new Dictionary<string, string>
        {
            ["laneColors.1"] = "#ffff00",
            ["laneColors.2"] = "#000000",
            ["defaultLaneColor"] = "#333333",
            ["updateInterval"] = "250"
        };

        var result = ViewConfigBuilder.FromFlatKeyValues(flat);

        Assert.Equal("#333333", result["defaultLaneColor"]);
        Assert.Equal(250, result["updateInterval"]);

        var laneColors = Assert.IsType<Dictionary<string, object>>(result["laneColors"]);
        Assert.Equal("#ffff00", laneColors["1"]);
        Assert.Equal("#000000", laneColors["2"]);
    }

    [Fact]
    public void FromFlatKeyValues_PreservesHexColorsAsStrings()
    {
        var result = ViewConfigBuilder.FromFlatKeyValues(new Dictionary<string, string>
        {
            ["defaultLaneColor"] = "#333333"
        });

        Assert.Equal("#333333", result["defaultLaneColor"]);
    }

    [Fact]
    public void FromFlatKeyValues_CoercesBooleans()
    {
        var result = ViewConfigBuilder.FromFlatKeyValues(new Dictionary<string, string>
        {
            ["someFlag"] = "true"
        });

        Assert.Equal(true, result["someFlag"]);
    }

    [Fact]
    public void FromFlatKeyValues_SupportsDeeperNesting()
    {
        var result = ViewConfigBuilder.FromFlatKeyValues(new Dictionary<string, string>
        {
            ["a.b.c"] = "x"
        });

        var a = Assert.IsType<Dictionary<string, object>>(result["a"]);
        var b = Assert.IsType<Dictionary<string, object>>(a["b"]);
        Assert.Equal("x", b["c"]);
    }
}
