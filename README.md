# WesnothMarkupLanguage

A `.NET Standard 2.0` library for reading, writing, preprocessing, and mapping [Battle for Wesnoth](https://www.wesnoth.org/) WML configuration files.

> Version 2 is a breaking redesign targeting Wesnoth 1.18.7 behavior.

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

Version 2 includes the lossless parser, semantic DOM, canonical writer, POCO serializer, source diagnostics, root-sandboxed resolver, macro expansion, includes, common directives, conditionals, version checks, cycle detection, and resource limits.

As of `2.0.5`, the local campaign validator has been run against a Wesnoth 1.18.7 installation copied under `References/Wesnoth-Installation`. With `NORMAL` difficulty and full `{core/}` preprocessing, 18 of the 19 discovered `_main.cfg` folders validate successfully through preprocessing, parsing, and DOM counting:

- `Dead_Water`
- `Delfadors_Memoirs`
- `Descent_Into_Darkness`
- `Eastern_Invasion`
- `Heir_To_The_Throne`
- `Legend_of_Wesmere`
- `Liberty`
- `Northern_Rebirth`
- `Sceptre_of_Fire`
- `Secrets_of_the_Ancients`
- `Son_Of_The_Black_Eye`
- `The_Hammer_of_Thursagan`
- `The_Rise_Of_Wesnoth`
- `The_South_Guard`
- `tutorial`
- `Two_Brothers`
- `Under_the_Burning_Suns`
- `Winds_of_Fate`

Purposeful gaps:

- `World_Conquest` is discovered by `-All`, but it is multiplayer/resource-oriented and has no ordinary `[campaign]` metadata/define for this validator workflow. It is reported as `VAL1001` rather than silently skipped.
- The validator checks WML preprocessing and parsing; it does not execute Lua, evaluate Wesnoth Formula Language, render maps, load images/sounds, or simulate game runtime behavior.
- The default local survey defines `NORMAL`. Other difficulty symbols can still expose useful compatibility cases, but they are not part of the default coverage claim above.

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

## Validate local campaigns

The repository includes a .NET 8 campaign validator for repeatable local compatibility surveys. Place a local installation beneath `References/Wesnoth-Installation` (or pass another installation root), then select one or more exact campaign directory names:

```powershell
# One campaign
.\scripts\Validate-WesnothCampaigns.ps1 -Campaign Heir_To_The_Throne

# Several campaigns
.\scripts\Validate-WesnothCampaigns.ps1 -Campaign tutorial,Sceptre_of_Fire

# Every campaign that contains _main.cfg
.\scripts\Validate-WesnothCampaigns.ps1 -All
```

The wrapper supports `-InstallationRoot`, `-OutputPath`, `-MaxOutputMiB`, `-Configuration`, and `-NoBuild`. The console application can also be used directly:

```powershell
dotnet run --project WesnothMarkupLanguage.CampaignValidator -- `
  --installation-root 'I:\SteamLibrary\steamapps\common\wesnoth' `
  --campaign Heir_To_The_Throne `
  --output artifacts/validation/campaign-validation.json
```

Each campaign is preprocessed independently as `{core/}` plus its campaign entry, with its declared campaign symbol and `NORMAL` defined. The default report is `artifacts/validation/campaign-validation.json`. It contains deterministic campaign results, normalized source locations, preprocessing and parser diagnostics, source-map and DOM counts, and one of the statuses `Passed`, `Failed`, or `ResourceLimit`. Expanded WML and local game data are never copied into the report.

`-All` discovers every folder under `data/campaigns` that contains `_main.cfg`. Folders that are multiplayer/resource-only rather than ordinary single-player campaigns, such as a file set with no matching `[campaign]` metadata, are retained in the report as validation failures with `VAL1001`.

Exit codes are `0` when all campaigns pass, `1` for validation failures, `2` for usage or input errors, `3` when any campaign exceeds the expanded-output limit, `4` for unexpected tool or report failures, and `130` for cancellation. Validation and resource-limit exits still produce a report. Local Wesnoth files are ignored and are not required by GitHub Actions; automated validator tests build temporary synthetic installations instead.

Licensed under the [MIT License](LICENSE).
