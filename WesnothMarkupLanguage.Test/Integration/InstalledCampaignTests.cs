using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace WesnothMarkupLanguage.Test.Integration
{
    public sealed class InstalledCampaignTests
    {
        [InstalledGameFact]
        [Trait("Category", "InstalledGameIntegration")]
        public void Deserializes_installed_campaign_metadata()
        {
            string installationPath = WesnothTestEnvironment.TryGetInstallationPath()!;

            string mainFile = WesnothTestEnvironment.GetCampaignMainFile(installationPath);
            string source = File.ReadAllText(mainFile);
            WmlSyntaxTree syntax = WmlParser.Parse(source, mainFile);

            Assert.False(
                syntax.HasErrors,
                string.Join(System.Environment.NewLine, syntax.Diagnostics.Select(d => d.ToString())));
            Assert.Equal(source, WmlWriter.Write(syntax, WmlWriteMode.Lossless));

            Campaign campaign = WmlSerializer.Deserialize<Campaign>(syntax.Document);

            Assert.Equal("Heir_To_The_Throne", campaign.Id);
            Assert.Equal("Heir to the Throne", campaign.Name);
            Assert.Equal("HttT", campaign.Abbreviation);
            Assert.Equal("CAMPAIGN_HEIR_TO_THE_THRONE", campaign.Define);
            Assert.Equal("01_The_Elves_Besieged", campaign.FirstScenario);
            Assert.Equal(55, campaign.Rank);
            Assert.NotEmpty(campaign.AboutSections);
            Assert.Contains(campaign.AboutSections, section => section.Title == "Campaign Design");
            Assert.Contains(campaign.AboutSections.SelectMany(section => section.Entries), entry => entry.Name == "David White (Sirp)");
        }

        [WmlTag("campaign")]
        public sealed class Campaign
        {
            [WmlAttribute("id")] public string Id { get; set; } = string.Empty;
            [WmlAttribute("name")] public string Name { get; set; } = string.Empty;
            [WmlAttribute("abbrev")] public string Abbreviation { get; set; } = string.Empty;
            [WmlAttribute("rank")] public int Rank { get; set; }
            [WmlAttribute("define")] public string Define { get; set; } = string.Empty;
            [WmlAttribute("first_scenario")] public string FirstScenario { get; set; } = string.Empty;
            [WmlChild("about")] public List<AboutSection> AboutSections { get; set; } = new List<AboutSection>();
            [WmlExtensionData] public Dictionary<string, string> AdditionalAttributes { get; set; } = new Dictionary<string, string>();
        }

        public sealed class AboutSection
        {
            [WmlAttribute("title")] public string? Title { get; set; }
            [WmlChild("entry")] public List<AboutEntry> Entries { get; set; } = new List<AboutEntry>();
        }

        public sealed class AboutEntry
        {
            [WmlAttribute("name")] public string Name { get; set; } = string.Empty;
            [WmlAttribute("comment")] public string? Comment { get; set; }
        }
    }
}
