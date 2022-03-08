using ConsoulLibrary;
using ConsoulLibrary.Views;
using System.IO;
using WesnothMarkupLanguage;
using WesnothMarkupLanguage.Serialization;
using WesnothMarkupLanguage.Tags;

namespace WesnothMarkupLanguage.Test.Views
{
    public class MainView : StaticView
    {
        public MainView()
        {
            Title = (new BannerEntry("WML Utilities")).Message;
        }

        [ViewOption("Read WML")]
        public void ReadWML()
        {
            string filepath = Consoul.PromptForFilepath("Input WML formatted .cfg file.", true);
            using (StreamReader sr = new StreamReader(filepath))
            {
                var config = ConfigReader.Read(sr);
                Consoul.Write($"Found {config.TopLevelTags.Count} Top-Level Tags.", System.ConsoleColor.Green);

                var searchTags = config.Find("unit_type");
                foreach (var v in searchTags)
                {
                    var t = v.ToType() as unit_type;
                    Consoul.Write($"[{t.id}] {t.name}");
                }
            }
            Consoul.Wait();
        }
    }
}
