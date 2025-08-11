using System.ComponentModel.DataAnnotations;

namespace LLMHoney.Host;

public sealed class ConfigurationOptions
{
    public const string SectionName = "Configuration";

    [Required]
    public string ConfigDirectory { get; init; } = "./configs";
}
