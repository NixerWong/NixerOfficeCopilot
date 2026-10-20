using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Fonts
{
    public sealed class InstalledFontManifestEntry
    {
        [JsonProperty("family")]
        public string Family { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("fontVersion")]
        public string FontVersion { get; set; }

        [JsonProperty("registered")]
        public bool Registered { get; set; }
    }

    public sealed class InstalledFontManifest
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [JsonProperty("fonts")]
        public IList<InstalledFontManifestEntry> Fonts { get; set; } = new List<InstalledFontManifestEntry>();

        [JsonProperty("pendingDeletionPaths")]
        public IList<string> PendingDeletionPaths { get; set; } = new List<string>();

        public static InstalledFontManifest Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Manifest path is required.", nameof(path));
            if (!File.Exists(path))
            {
                return new InstalledFontManifest { UpdatedUtc = DateTime.UtcNow };
            }

            var manifest = JsonConvert.DeserializeObject<InstalledFontManifest>(File.ReadAllText(path, Encoding.UTF8));
            if (manifest == null || manifest.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException("Unsupported installed font manifest version: " + path);
            }

            if (manifest.Fonts == null) manifest.Fonts = new List<InstalledFontManifestEntry>();
            if (manifest.PendingDeletionPaths == null) manifest.PendingDeletionPaths = new List<string>();
            if (manifest.Fonts.Any(font => font == null))
            {
                throw new InvalidDataException("Installed font manifest contains an empty font entry: " + path);
            }
            return manifest;
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Manifest path is required.", nameof(path));
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Manifest path must include a directory.", nameof(path));

            Directory.CreateDirectory(directory);
            UpdatedUtc = DateTime.UtcNow;
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public void Upsert(InstalledFontManifestEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (Fonts == null) Fonts = new List<InstalledFontManifestEntry>();
            var existing = Fonts.FirstOrDefault(font => string.Equals(font.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) Fonts.Remove(existing);
            Fonts.Add(entry);
        }

        public void Remove(string path)
        {
            if (Fonts == null) return;
            foreach (var entry in Fonts.Where(font => string.Equals(font.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Fonts.Remove(entry);
            }
        }
    }
}
