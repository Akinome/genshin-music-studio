$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $root "release"
$appDir = Join-Path $releaseRoot "GenshinMusicStudio-win-x64"
$zipPath = Join-Path $releaseRoot "GenshinMusicStudio-win-x64.zip"

$resolvedRoot = [IO.Path]::GetFullPath($root)
$resolvedRelease = [IO.Path]::GetFullPath($releaseRoot)
if (-not $resolvedRelease.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release path is outside the repository: $resolvedRelease"
}

# Preserve an AI environment installed inside the packaged app across repackaging.
$venvInApp = Join-Path $appDir ".venv-ai"
$venvStash = Join-Path $releaseRoot ".venv-ai-stash"
if (Test-Path -LiteralPath $venvInApp) {
    if (Test-Path -LiteralPath $venvStash) {
        Remove-Item -LiteralPath $venvStash -Recurse -Force
    }
    Move-Item -LiteralPath $venvInApp -Destination $venvStash
}
if (Test-Path -LiteralPath $appDir) {
    Remove-Item -LiteralPath $appDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$project = Join-Path $root "GenshinMusicStudio.WinUI\GenshinMusicStudio.WinUI.csproj"
dotnet build $project -c Release -p:Platform=x64 -p:PublishTrimmed=false -p:PublishReadyToRun=false
$buildDir = Join-Path $root "GenshinMusicStudio.WinUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64"
if (-not (Test-Path -LiteralPath $buildDir)) {
    throw "Release build output was not found: $buildDir"
}
Copy-Item -Path (Join-Path $buildDir "*") -Destination $appDir -Recurse -Force
Get-ChildItem -LiteralPath $appDir -Recurse -Filter "*.pdb" -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "launcher.bat") -Destination (Join-Path $appDir "Start.bat") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README-RELEASE.txt") -Destination (Join-Path $appDir "README.txt") -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\install_ai_env_uv.bat") -Destination $appDir -Force
$backendFiles = @(
    "backend\studio_backend.py",
    "backend\studio_pipeline.py",
    "backend\media_tools.py",
    "backend\midi_to_genshin.py",
    "backend\duration_preserving.py",
    "backend\melody_extract.py",
    "backend\symbolic_optimizer.py",
    "backend\conversion_modes.py",
    "backend\model_evaluator.py",
    "config.json",
    "backend\requirements-ai.txt",
    "README.md",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md"
)
foreach ($file in $backendFiles) {
    $source = Join-Path $root $file
    $destination = Join-Path $appDir $file
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

if (Test-Path -LiteralPath $venvStash) {
    Move-Item -LiteralPath $venvStash -Destination (Join-Path $appDir ".venv-ai")
}

Write-Host "Release directory: $appDir"
Write-Host "Release archive:   $zipPath"
