[CmdletBinding()]
param(
    [string[]]$Campaign,
    [switch]$All,
    [string]$InstallationRoot,
    [string]$OutputPath,
    [ValidateRange(1, 2147483647)][int]$MaxOutputMiB = 128,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoBuild
)

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InstallationRoot)) { $InstallationRoot = Join-Path $repoRoot 'References/Wesnoth-Installation' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot 'artifacts/validation/campaign-validation.json' }

if ($All -and $Campaign.Count -gt 0) { [Console]::Error.WriteLine('Specify either -All or -Campaign, not both.'); exit 2 }
if (-not $All -and $Campaign.Count -eq 0) {
    [Console]::Error.WriteLine('Specify -All or one or more campaign folder names with -Campaign.')
    $campaignRoot = Join-Path $InstallationRoot 'data/campaigns'
    if (Test-Path -LiteralPath $campaignRoot) {
        $available = Get-ChildItem -LiteralPath $campaignRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '_main.cfg') } | Sort-Object Name | Select-Object -ExpandProperty Name
        if ($available.Count -gt 0) { Write-Host ('Available campaigns: ' + ($available -join ', ')) }
    }
    exit 2
}

$project = Join-Path $repoRoot 'WesnothMarkupLanguage.CampaignValidator/WesnothMarkupLanguage.CampaignValidator.csproj'
$arguments = @('run', '--project', $project, '--configuration', $Configuration)
if ($NoBuild) { $arguments += '--no-build' }
$arguments += @('--', '--installation-root', $InstallationRoot, '--output', $OutputPath, '--max-output-mib', $MaxOutputMiB)
if ($All) { $arguments += '--all' } else { foreach ($name in $Campaign) { $arguments += @('--campaign', $name) } }

& dotnet @arguments
exit $LASTEXITCODE
