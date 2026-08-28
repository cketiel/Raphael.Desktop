using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Raphael.Desktop.Services.Help;

/// <summary>
/// What the shipped help bundle declares about itself.
/// </summary>
/// <remarks>
/// Written by <c>help/build/build.js</c> at release time and read here. The field that earns its
/// keep is <see cref="CoversApp"/>: it is how the application can tell that the help in front of a
/// dispatcher describes an older version of itself, and say so instead of letting them believe it.
/// </remarks>
public sealed class HelpManifest
{
    [JsonPropertyName("helpVersion")]
    public string HelpVersion { get; set; }

    /// <summary>Application name to the version this help was written against.</summary>
    [JsonPropertyName("coversApp")]
    public Dictionary<string, string> CoversApp { get; set; } = new();

    [JsonPropertyName("builtUtc")]
    public string BuiltUtc { get; set; }

    /// <summary>Short commit of the ecosystem repository the bundle was compiled from.</summary>
    [JsonPropertyName("sourceCommit")]
    public string SourceCommit { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("home")]
    public string Home { get; set; }

    /// <summary>Retired topic id to the id that replaced it. See HELP_POLICY.md section 6.</summary>
    [JsonPropertyName("redirects")]
    public Dictionary<string, string> Redirects { get; set; } = new();

    [JsonPropertyName("topics")]
    public List<HelpManifestTopic> Topics { get; set; } = new();
}

public sealed class HelpManifestTopic
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; }

    [JsonPropertyName("since")]
    public string Since { get; set; }

    [JsonPropertyName("lastReviewed")]
    public string LastReviewed { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("menu")]
    public string Menu { get; set; }
}
