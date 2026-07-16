using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace WesnothMarkupLanguage.Test.Integration
{
    /// <summary>Loads optional, test-only configuration from WesnothMarkupLanguage.Test/.env.</summary>
    internal static class DotEnvTestConfiguration
    {
        private static readonly object Sync = new object();
        private static bool _loaded;

        [ModuleInitializer]
        internal static void Initialize() => EnsureLoaded();

        internal static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;

                string? file = FindFile();
                if (file == null) return;

                foreach (KeyValuePair<string, string> variable in Parse(File.ReadAllLines(file)))
                {
                    // Shell and CI configuration always takes precedence over local defaults.
                    if (Environment.GetEnvironmentVariable(variable.Key) == null)
                        Environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }
        }

        private static string? FindFile()
        {
            string outputFile = Path.Combine(AppContext.BaseDirectory, ".env");
            if (File.Exists(outputFile)) return outputFile;

            // This fallback also supports test runners that do not copy content files.
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string projectFile = Path.Combine(directory.FullName, "WesnothMarkupLanguage.Test", ".env");
                if (File.Exists(projectFile)) return projectFile;

                string adjacentFile = Path.Combine(directory.FullName, ".env");
                if (File.Exists(Path.Combine(directory.FullName, "WesnothMarkupLanguage.Test.csproj")) && File.Exists(adjacentFile))
                    return adjacentFile;

                directory = directory.Parent;
            }

            return null;
        }

        private static IEnumerable<KeyValuePair<string, string>> Parse(IEnumerable<string> lines)
        {
            foreach (string sourceLine in lines)
            {
                string line = sourceLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line.Substring(7).TrimStart();

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;

                string key = line.Substring(0, equals).Trim();
                if (!IsValidKey(key)) continue;

                string value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') ||
                     (value[0] == '\'' && value[value.Length - 1] == '\'')))
                    value = value.Substring(1, value.Length - 2);

                yield return new KeyValuePair<string, string>(key, value);
            }
        }

        private static bool IsValidKey(string key)
        {
            if (key.Length == 0 || (!(char.IsLetter(key[0]) || key[0] == '_'))) return false;
            for (int i = 1; i < key.Length; i++)
                if (!(char.IsLetterOrDigit(key[i]) || key[i] == '_')) return false;
            return true;
        }
    }
}
