# WesnothMarkupLanguage

A `.NET Standard 2.0` library for reading, writing, preprocessing, and mapping [Battle for Wesnoth](https://www.wesnoth.org/) WML configuration files.

> Version 2 is a breaking redesign. The first package is `2.0.0-preview.1` and targets Wesnoth 1.18.7 behavior.

## Parse and edit WML

```csharp
var tree = WmlParser.Parse(File.ReadAllText("unit.cfg"), "unit.cfg");
var units = tree.Document.FindTags("unit_type");

// An unchanged syntax tree round-trips byte-for-byte.
string unchanged = WmlWriter.Write(tree, WmlWriteMode.Lossless);

// Semantic documents can be emitted in a stable canonical style.
string canonical = WmlWriter.Write(tree.Document);
```

The parser understands nested and amended tags, top-level and multiple attributes, quoted/translatable/raw multiline values, concatenation, variables, formulas, comments, directives, and macro calls. Diagnostics carry source spans and parsing recovers where possible.

## Map C# objects

```csharp
[WmlTag("unit_type")]
public sealed class UnitType
{
    [WmlAttribute("id")] public string Id { get; set; }
    [WmlAttribute("cost")] public int Cost { get; set; }
    [WmlChild("attack")] public List<Attack> Attacks { get; set; }
}

UnitType unit = WmlSerializer.Deserialize<UnitType>(tree.Document);
WmlDocument document = WmlSerializer.Serialize(unit);
```

`[WmlExtensionData]` can retain attributes that are not represented by declared properties.

## Preprocess safely

```csharp
var options = new WmlPreprocessorOptions
{
    SourceResolver = new FileSystemWmlSourceResolver(addOnRoot, wesnothCoreRoot)
};
options.Defines["HARD"] = "";

WmlPreprocessorResult result = await WmlPreprocessor.ProcessAsync(source, options, fileName);
```

Filesystem includes are restricted to explicit roots and reparse points are rejected by default. Wesnoth core data is not bundled; add its `data/core` directory as an allowed root when core macro definitions are required.

## Status

The preview includes the lossless parser, semantic DOM, canonical writer, POCO serializer, source diagnostics, root-sandboxed resolver, macro expansion, includes, common directives, conditionals, version checks, cycle detection, and resource limits. Compatibility discoveries from real 1.18.7 add-ons will be addressed before stable `2.0.0`.

## Test an installed Wesnoth campaign

The integration tests can deserialize campaign metadata directly from an installed copy of Wesnoth. Set `WESNOTH_INSTALLATION_PATH` to the game directory; the current fixture validates the main `Heir_To_The_Throne` campaign.

```powershell
$env:WESNOTH_INSTALLATION_PATH = 'I:\SteamLibrary\steamapps\common\wesnoth'
dotnet test --filter 'Category=InstalledGameIntegration'
```

For persistent local configuration, create the ignored file `WesnothMarkupLanguage.Test/.env`:

```dotenv
WESNOTH_INSTALLATION_PATH=I:\SteamLibrary\steamapps\common\wesnoth
WESNOTH_1_18_7_EXECUTABLE=I:\SteamLibrary\steamapps\common\wesnoth\wesnoth.exe
```

Values already supplied by the shell or CI take precedence over `.env` values.

When the installation variable is absent, installed-game integration tests are inert so ordinary builds and pull requests do not depend on a local game installation.

Licensed under the [MIT License](LICENSE).
