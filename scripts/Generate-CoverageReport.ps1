param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$TargetDirectory,

    [ValidateSet("full", "core")]
    [string]$Scope = "full",

    [double]$MinimumLineCoverage = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$reports = (Get-ChildItem $ResultsDirectory -Recurse -Filter coverage.cobertura.xml | ForEach-Object FullName) -join ";"
if ([string]::IsNullOrWhiteSpace($reports))
{
    throw "No coverage files were found under '$ResultsDirectory'."
}

$fileFilters = @("-**\tests\**")
if ($Scope -eq "core")
{
    $fileFilters += @(
        "-**\src\Cress.Companion.Windows\**",
        "-**\src\Cress.Companion.Core\ProcessCompanionTargetCatalog.cs",
        "-**\src\Cress.Companion.Core\ProcessWindowInspector.cs",
        "-**\src\Cress.Companion.Core\RecordingSessionBackend.cs",
        "-**\src\Cress.Companion.Core\ScreenPreviewProvider.cs",
        "-**\src\Cress.Companion.Core\SystemCompanionClock.cs",
        "-**\src\Cress.Studio\**",
        "-**\src\Cress.Studio.Launcher\**",
        "-**\src\Cress.Studio.Tool\**",
        "-**\src\Cress.Studio.Web\**",
        "-**\src\Cress.Studio.Windows\**",
        "-**\src\Cress.Studio.Core\**",
        "-**\src\Cress.Execution.Flawright\**",
        "-**\src\Cress.Recorder\**",
        "-**\src\Cress.LivingDocs\**",
        "-**\src\Cress.Validation\**",
        "-**\src\Cress.ProjectSystem\**",
        "-**\src\Cress.Specs\**",
        "-**\src\Cress.Exporters\Cypress\**",
        "-**\src\Cress.Exporters\SeleniumIde\**",
        "-**\src\Cress.Execution\PluginHost.cs",
        "-**\src\Cress.Execution\Drivers\HttpRuntimeDriver.cs",
        "-**\src\Cress.Execution\NodeProcessJsonRpcClient.cs",
        "-**\src\Cress.Execution\ProjectCatalogService.cs",
        "-**\src\Cress.Execution\StepRegistry.cs"
    )
}

if (Test-Path $TargetDirectory)
{
    Remove-Item $TargetDirectory -Recurse -Force
}

$reportGenerator = Join-Path $env:USERPROFILE ".dotnet\tools\reportgenerator.exe"
& $reportGenerator `
    "-reports:$reports" `
    "-targetdir:$TargetDirectory" `
    "-reporttypes:HtmlInline;Cobertura;TextSummary;Badges" `
    "-assemblyfilters:+Cress*;-*Tests*" `
    "-filefilters:$($fileFilters -join ';')"

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$summaryPath = Join-Path $TargetDirectory "Summary.txt"
$summary = Get-Content $summaryPath -Raw

if ($MinimumLineCoverage -gt 0)
{
    $match = [regex]::Match($summary, "Line coverage:\s+([0-9]+(?:\.[0-9]+)?)%")
    if (-not $match.Success)
    {
        throw "Could not determine line coverage from '$summaryPath'."
    }

    $lineCoverage = [double]$match.Groups[1].Value
    if ($lineCoverage -lt $MinimumLineCoverage)
    {
        throw "Coverage scope '$Scope' is $lineCoverage%, below required $MinimumLineCoverage%."
    }
}

$summary
