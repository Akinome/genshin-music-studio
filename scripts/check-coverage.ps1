param(
    [double]$MinimumLineCoverage = 80.0
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $root "GenshinMusicStudio.Core.Tests\GenshinMusicStudio.Core.Tests.csproj"
$settings = Join-Path $root "coverlet.runsettings"
$results = Join-Path $root ("TestResults\" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss"))

dotnet test $testProject `
    --configuration Release `
    --settings $settings `
    --collect:"XPlat Code Coverage" `
    --results-directory $results
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE"
}

$report = Get-ChildItem -LiteralPath $results -Recurse -Filter "coverage.cobertura.xml" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $report) {
    throw "Coverage report was not generated."
}

[xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
$lineRate = [double]::Parse(
    $coverage.coverage.'line-rate',
    [Globalization.CultureInfo]::InvariantCulture)
$branchRate = [double]::Parse(
    $coverage.coverage.'branch-rate',
    [Globalization.CultureInfo]::InvariantCulture)
$linePercent = [Math]::Round($lineRate * 100, 2)
$branchPercent = [Math]::Round($branchRate * 100, 2)

Write-Host "Line coverage:   $linePercent%"
Write-Host "Branch coverage: $branchPercent%"
Write-Host "Report:          $($report.FullName)"

if ($linePercent -lt $MinimumLineCoverage) {
    throw "Line coverage $linePercent% is below the required $MinimumLineCoverage%."
}
