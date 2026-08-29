using WebLynx2.Models;
using WebLynx2.Utilities;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class ViewPropertiesYamlParserTests
{
    [Fact]
    public void ParseFile_ReadsTypedEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), "view_yaml_test_" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(path, """
            properties:
              - key: updateInterval
                type: integer
                value: 100
              - key: disable_lap_board
                type: boolean
                value: false
              - key: laneColors.1
                type: color
                value: '#ffff00'
              - key: finishedText
                type: string
                value: '-'
            """);

        try
        {
            var entries = ViewPropertiesYamlParser.ParseFile(path);
            var map = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

            Assert.Equal("100", map["updateInterval"].Value);
            Assert.Equal(ViewPropertyType.Integer, map["updateInterval"].Type);
            Assert.Equal("false", map["disable_lap_board"].Value);
            Assert.Equal(ViewPropertyType.Boolean, map["disable_lap_board"].Type);
            Assert.Equal("#ffff00", map["laneColors.1"].Value);
            Assert.Equal(ViewPropertyType.Color, map["laneColors.1"].Type);
            Assert.Equal("-", map["finishedText"].Value);
            Assert.Equal(ViewPropertyType.String, map["finishedText"].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ViewPropertiesYamlFileEditorTests
{
    [Fact]
    public void Upsert_WritesAndUpdatesTypedEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), "view_yaml_edit_" + Guid.NewGuid().ToString("N") + ".yaml");

        try
        {
            ViewPropertiesYamlFileEditor.Upsert(path, "updateInterval", "250", ViewPropertyType.Integer);
            ViewPropertiesYamlFileEditor.Upsert(path, "disable_lap_board", "true", ViewPropertyType.Boolean);

            var entries = ViewPropertiesYamlParser.ParseFile(path);
            var map = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

            Assert.Equal("250", map["updateInterval"].Value);
            Assert.Equal(ViewPropertyType.Integer, map["updateInterval"].Type);
            Assert.Equal("true", map["disable_lap_board"].Value);
            Assert.Equal(ViewPropertyType.Boolean, map["disable_lap_board"].Type);

            ViewPropertiesYamlFileEditor.Upsert(path, "updateInterval", "100", ViewPropertyType.Integer);
            entries = ViewPropertiesYamlParser.ParseFile(path);
            Assert.Equal("100", entries.Single(e => e.Key == "updateInterval").Value);

            ViewPropertiesYamlFileEditor.RemoveKey(path, "disable_lap_board");
            entries = ViewPropertiesYamlParser.ParseFile(path);
            Assert.DoesNotContain(entries, e => e.Key == "disable_lap_board");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

public class ColorContrastTests
{
    [Theory]
    [InlineData("#ffffff", "#000000")]
    [InlineData("#000000", "#ffffff")]
    [InlineData("#ffff00", "#000000")]
    public void GetReadableTextColor_PicksContrastingForeground(string background, string expectedForeground)
    {
        Assert.Equal(expectedForeground, ColorContrast.GetReadableTextColor(background));
    }
}

public class ViewDiscoveryServiceYamlTests
{
    [Fact]
    public void DiscoverViews_LoadsTypedYamlFromSharedAndViewFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "WebLynx2ViewYaml_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sample_view"));
        File.WriteAllText(Path.Combine(root, "sample_view", "template.html"), "<html></html>");
        File.WriteAllText(Path.Combine(root, ViewPropertiesFiles.FileName), """
            properties:
              - key: updateInterval
                type: integer
                value: 100
            """);
        File.WriteAllText(Path.Combine(root, "sample_view", ViewPropertiesFiles.FileName), """
            properties:
              - key: disable_lap_board
                type: boolean
                value: true
            """);

        try
        {
            var discovery = new ViewDiscoveryService(root);
            discovery.DiscoverViews();

            var catalog = discovery.LastPropertyCatalog.ToDictionary(p => p.Key, StringComparer.Ordinal);
            Assert.Equal(ViewPropertyType.Integer, catalog["updateInterval"].Type);
            Assert.Equal("100", catalog["updateInterval"].Value);
            Assert.Equal(ViewPropertyType.Boolean, catalog["disable_lap_board"].Type);
            Assert.Equal("true", catalog["disable_lap_board"].Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
