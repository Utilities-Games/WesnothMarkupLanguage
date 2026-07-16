namespace PackageSmoke.NetFramework
{
    using WesnothMarkupLanguage;
    public static class Consumer
    {
        public static string RoundTrip(string text) { return WmlWriter.Write(WmlParser.Parse(text)); }
    }
}
