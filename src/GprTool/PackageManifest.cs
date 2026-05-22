using System;
using System.Text.RegularExpressions;

namespace GprTool
{
    public sealed class PackageVersion : IEquatable<PackageVersion>
    {
        static readonly Regex ValidVersionPattern = new Regex(
            @"^\d+(\.\d+){0,3}([\-+][0-9A-Za-z\-\.]+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        PackageVersion(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static PackageVersion Parse(string value)
        {
            if (!TryParse(value, out var version))
            {
                throw new FormatException($"Invalid package version '{value}'.");
            }

            return version;
        }

        public static bool TryParse(string value, out PackageVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (!ValidVersionPattern.IsMatch(trimmed))
            {
                return false;
            }

            version = new PackageVersion(trimmed);
            return true;
        }

        public bool Equals(PackageVersion other) =>
            other != null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => Equals(obj as PackageVersion);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

        public override string ToString() => Value;
    }

    public sealed class RepositoryMetadata
    {
        public string Url { get; set; }
        public string Type { get; set; }
    }

    public sealed class ManifestMetadata
    {
        public string Id { get; set; }
        public PackageVersion Version { get; set; }
        public RepositoryMetadata Repository { get; set; }
    }

    public sealed class PackageManifest
    {
        public PackageManifest(ManifestMetadata metadata)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public ManifestMetadata Metadata { get; }
    }
}
