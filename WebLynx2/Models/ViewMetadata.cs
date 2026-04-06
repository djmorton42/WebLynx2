namespace WebLynx2.Models;

public class ViewMetadata
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplatePath { get; set; } = string.Empty;
    public string StylesPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> RequiredFiles { get; set; } = new();
    public List<string> MissingFiles { get; set; } = new();
}
