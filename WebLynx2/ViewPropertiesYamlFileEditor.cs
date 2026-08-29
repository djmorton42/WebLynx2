using WebLynx2.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WebLynx2;

/// <summary>
/// Reads and writes typed <c>view.yaml</c> files on disk (UTF-8).
/// </summary>
public static class ViewPropertiesYamlFileEditor
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static void Upsert(string filePath, string key, string value, ViewPropertyType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var document = LoadDocument(filePath);
        var properties = document.Properties ?? new List<ViewPropertyYamlItem>();

        var existing = properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.Ordinal));
        if (existing is null)
        {
            properties.Add(new ViewPropertyYamlItem
            {
                Key = key,
                Type = ToYamlType(type),
                Value = ToYamlValue(value, type)
            });
        }
        else
        {
            existing.Type = ToYamlType(type);
            existing.Value = ToYamlValue(value, type);
        }

        document.Properties = properties;
        WriteDocument(filePath, document);
    }

    public static void RemoveKey(string filePath, string key)
    {
        if (!File.Exists(filePath))
            return;

        var document = LoadDocument(filePath);
        if (document.Properties is null || document.Properties.Count == 0)
            return;

        document.Properties = document.Properties
            .Where(p => !string.Equals(p.Key, key, StringComparison.Ordinal))
            .ToList();

        WriteDocument(filePath, document);
    }

    private static ViewPropertiesYamlDocument LoadDocument(string filePath)
    {
        if (!File.Exists(filePath))
            return new ViewPropertiesYamlDocument { Properties = new List<ViewPropertyYamlItem>() };

        var yaml = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(yaml))
            return new ViewPropertiesYamlDocument { Properties = new List<ViewPropertyYamlItem>() };

        var document = Deserializer.Deserialize<ViewPropertiesYamlDocument>(yaml);
        document ??= new ViewPropertiesYamlDocument();
        document.Properties ??= new List<ViewPropertyYamlItem>();
        return document;
    }

    private static void WriteDocument(string filePath, ViewPropertiesYamlDocument document)
    {
        var yaml = Serializer.Serialize(document);
        File.WriteAllText(filePath, yaml);
    }

    private static string ToYamlType(ViewPropertyType type) =>
        type switch
        {
            ViewPropertyType.Integer => "integer",
            ViewPropertyType.Boolean => "boolean",
            ViewPropertyType.Color => "color",
            _ => "string"
        };

    private static object ToYamlValue(string value, ViewPropertyType type) =>
        type switch
        {
            ViewPropertyType.Integer when int.TryParse(value, out var i) => i,
            ViewPropertyType.Boolean => bool.TryParse(value, out var b) && b,
            _ => value
        };

    private sealed class ViewPropertiesYamlDocument
    {
        public List<ViewPropertyYamlItem>? Properties { get; set; }
    }

    private sealed class ViewPropertyYamlItem
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public object? Value { get; set; }
    }
}
