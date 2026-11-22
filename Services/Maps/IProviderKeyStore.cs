using System.Collections.Generic;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// Third-party provider keys for the live map (POWER UP): OpenAI, Anthropic, xAI, OpenRouter,
    /// TomTom, Mapbox, HERE, Amazon Location. Values never leave this process: the map page is only
    /// told which keys exist.
    /// </summary>
    public interface IProviderKeyStore
    {
        /// <summary>The key saved under that name (e.g. <c>OPENAI_API_KEY</c>), or null.</summary>
        string? Get(string name);

        /// <summary>Which of the given names have a key, without the values.</summary>
        IDictionary<string, bool> Status(IEnumerable<string> names);

        /// <summary>Saves the non-empty values; an empty one leaves the saved key as it was.</summary>
        void Save(IDictionary<string, string?> keys);
    }
}
