using System.Text.Json.Serialization;

namespace MarkdownVault.Services.Plugins;

/// <summary>Modelo del archivo <c>plugin.json</c> (fuente autoritativa de metadata).</summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]          public string Id          { get; set; } = "";
    [JsonPropertyName("name")]        public string Name        { get; set; } = "";
    [JsonPropertyName("version")]     public string Version     { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("author")]      public string Author      { get; set; } = "";
    [JsonPropertyName("entry")]       public string Entry       { get; set; } = "";
    [JsonPropertyName("minSdk")]      public string MinSdk      { get; set; } = "1.0.0";

    /// <summary>Valida los campos obligatorios del manifiesto.</summary>
    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Id))      { error = "Falta 'id'.";      return false; }
        if (string.IsNullOrWhiteSpace(Name))    { error = "Falta 'name'.";    return false; }
        if (string.IsNullOrWhiteSpace(Version)) { error = "Falta 'version'."; return false; }
        if (string.IsNullOrWhiteSpace(Entry))   { error = "Falta 'entry'.";   return false; }
        error = null;
        return true;
    }
}
