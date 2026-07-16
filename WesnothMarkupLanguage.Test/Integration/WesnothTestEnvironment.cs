using System;
using System.IO;
using Xunit;

namespace WesnothMarkupLanguage.Test.Integration
{
    internal static class WesnothTestEnvironment
    {
        internal const string InstallationPathVariable = "WESNOTH_INSTALLATION_PATH";
        internal const string DefaultCampaign = "Heir_To_The_Throne";

        internal static string? TryGetInstallationPath()
        {
            string? configured = Environment.GetEnvironmentVariable(InstallationPathVariable);
            if (string.IsNullOrWhiteSpace(configured)) return null;

            string installationPath = Path.GetFullPath(configured);
            string dataPath = Path.Combine(installationPath, "data");
            if (!Directory.Exists(dataPath))
                throw new InvalidOperationException(
                    $"Environment variable {InstallationPathVariable} points to '{installationPath}', " +
                    "but that directory does not contain a data directory.");

            return installationPath;
        }

        internal static string GetCampaignMainFile(string installationPath)
        {
            string mainFile = Path.Combine(
                installationPath,
                "data",
                "campaigns",
                DefaultCampaign,
                "_main.cfg");

            if (!File.Exists(mainFile))
                throw new FileNotFoundException(
                    $"Campaign '{DefaultCampaign}' was not found beneath the configured Wesnoth installation.",
                    mainFile);

            return mainFile;
        }
    }

    internal sealed class InstalledGameFactAttribute : FactAttribute
    {
        public InstalledGameFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WesnothTestEnvironment.InstallationPathVariable)))
                Skip = $"Set {WesnothTestEnvironment.InstallationPathVariable} to run installed-game integration tests.";
        }
    }
}
