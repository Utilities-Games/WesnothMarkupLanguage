namespace PackageSmoke.Modern;

using WesnothMarkupLanguage;

public static class Consumer
{
    public static string RoundTrip(string text) => WmlWriter.Write(WmlParser.Parse(text));
}
