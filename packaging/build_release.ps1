[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$ReleaseName,
    [string]$ReleaseNotes,
    [string]$ReleaseNotesFile,

    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [switch]$SkipRelease,
    [switch]$SkipTagCreation,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Native commands are checked through $LASTEXITCODE, so a non-zero exit code
# must not be converted into a terminating error before we can inspect it.
$global:PSNativeCommandUseErrorActionPreference = $false

$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $root "release"
$outputName = "GenshinMusicStudio-win-x64"
$zipPath = Join-Path $releaseRoot ($outputName + ".zip")
$hashPath = Join-Path $releaseRoot ($outputName + ".zip.sha256")
$changelogPath = Join-Path $root "CHANGELOG.md"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    if ($DryRun) {
        Write-Host "[dry-run] $Command $($Arguments -join ' ')"
        return
    }

    Write-Host "> $Command $($Arguments -join ' ')"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Write-NativePlan {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    Write-Host "[dry-run] $Command $($Arguments -join ' ')"
}

function Get-ChangelogNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    if (-not (Test-Path -LiteralPath $changelogPath)) {
        return $null
    }

    $escapedTag = [Regex]::Escape($Tag)
    $headingPattern = "^##\s+\[?$escapedTag\]?(?:\s+[-:].*)?\s*$"
    $lines = Get-Content -LiteralPath $changelogPath
    $start = -1

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $headingPattern) {
            $start = $i + 1
            break
        }
    }

    if ($start -lt 0) {
        return $null
    }

    $notes = [Collections.Generic.List[string]]::new()
    for ($i = $start; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s+') {
            break
        }
        $notes.Add($lines[$i])
    }

    $text = ($notes -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }
    return $text
}

function Get-ReleaseNotesText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesFile)) {
        $resolvedNotesFile = [IO.Path]::GetFullPath($ReleaseNotesFile)
        if (-not (Test-Path -LiteralPath $resolvedNotesFile -PathType Leaf)) {
            throw "Release notes file was not found: $resolvedNotesFile"
        }
        return (Get-Content -LiteralPath $resolvedNotesFile -Raw).Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        return $ReleaseNotes.Trim()
    }

    $changelogNotes = Get-ChangelogNotes -Tag $Tag
    if (-not [string]::IsNullOrWhiteSpace($changelogNotes)) {
        return $changelogNotes
    }

    throw "Release notes were not provided and CHANGELOG.md has no '$Tag' section."
}

function Test-RemoteTag {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    & git ls-remote --exit-code --tags origin "refs/tags/$Tag" 1>$null 2>$null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }
    if ($LASTEXITCODE -eq 2) {
        return $false
    }
    throw "Unable to check the remote tag '$Tag' (git exit code $LASTEXITCODE)."
}

function Test-ExistingRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    & gh release view $Tag --json tagName 1>$null 2>$null
    return ($LASTEXITCODE -eq 0)
}

Push-Location $root
try {
    if ([string]::IsNullOrWhiteSpace($ReleaseName)) {
        $ReleaseName = "$Version - Genshin Music Studio"
    }

    Write-Host "=== Genshin Music Studio release builder ==="
    Write-Host "Version:      $Version"
    Write-Host "Release name: $ReleaseName"
    Write-Host "Repository:   $root"

    if (-not $SkipBuild) {
        if (-not $SkipTests) {
            Write-Host "`n=== Running core tests ==="
            Invoke-Native -Command "dotnet" -Arguments @(
                "test",
                "GenshinMusicStudio.Core.Tests\GenshinMusicStudio.Core.Tests.csproj",
                "-c",
                "Release",
                "--nologo"
            )
        }

        Write-Host "`n=== Building Windows x64 package ==="
        & (Join-Path $PSScriptRoot "package_release.ps1") -OutputName $outputName
    }
    else {
        Write-Host "`n=== Skipping build; using existing release artifacts ==="
    }

    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Release archive was not created: $zipPath"
    }
    if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
        throw "Release checksum was not created: $hashPath"
    }

    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $expectedHash = ((Get-Content -LiteralPath $hashPath -Raw).Trim() -split '\s+')[0]
    if ($actualHash -ne $expectedHash) {
        throw "Release checksum verification failed. Expected $expectedHash, got $actualHash."
    }
    Write-Host "Verified SHA256: $actualHash"

    if ($SkipRelease) {
        Write-Host "`nRelease publishing was skipped."
        Write-Host "Archive: $zipPath"
        Write-Host "Checksum: $hashPath"
        return
    }

    if (-not $DryRun -and -not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI ('gh') is required to publish a release."
    }
    Invoke-Native -Command "gh" -Arguments @("auth", "status")

    Write-Host "`n=== Preparing Git tag ==="
    $tagExists = $false
    if (-not $DryRun) {
        $tagExists = Test-RemoteTag -Tag $Version
    }
    if (-not $tagExists) {
        if ($SkipTagCreation -and -not $DryRun) {
            throw "Remote tag '$Version' does not exist and tag creation was disabled."
        }

        if (-not $DryRun) {
            $localTagCommit = (& git rev-list -n 1 $Version 2>$null)
            $localTagExists = $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($localTagCommit -join ""))
            if ($localTagExists) {
                $localTagCommit = ($localTagCommit -join "").Trim()
                $headCommit = (& git rev-parse HEAD).Trim()
                if ($localTagCommit -ne $headCommit) {
                    throw "Local tag '$Version' points to $localTagCommit, but the checked-out commit is $headCommit."
                }
            }
            else {
                Invoke-Native -Command "git" -Arguments @("tag", "-a", $Version, "-m", "Release $Version")
            }
        }
        else {
            Write-NativePlan -Command "git" -Arguments @("tag", "-a", $Version, "-m", "Release $Version")
        }
        Invoke-Native -Command "git" -Arguments @("push", "origin", "refs/tags/$Version")
    }
    else {
        Invoke-Native -Command "git" -Arguments @("fetch", "origin", "tag", $Version)
    }

    if (-not $DryRun) {
        $tagCommit = (& git rev-list -n 1 $Version).Trim()
        $headCommit = (& git rev-parse HEAD).Trim()
        if ($tagCommit -ne $headCommit) {
            throw "Tag '$Version' points to $tagCommit, but the checked-out commit is $headCommit."
        }
    }

    $notesText = Get-ReleaseNotesText -Tag $Version
    $notesTemp = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($notesTemp, $notesText, [Text.UTF8Encoding]::new($false))

        Write-Host "`n=== Publishing GitHub Release ==="
        $releaseExists = $false
        if (-not $DryRun) {
            $releaseExists = Test-ExistingRelease -Tag $Version
        }

        if ($releaseExists) {
            Invoke-Native -Command "gh" -Arguments @(
                "release", "upload", $Version, $zipPath, $hashPath, "--clobber"
            )
            Invoke-Native -Command "gh" -Arguments @(
                "release", "edit", $Version,
                "--title", $ReleaseName,
                "--notes-file", $notesTemp,
                "--draft=$($Draft.IsPresent.ToString().ToLowerInvariant())",
                "--prerelease=$($Prerelease.IsPresent.ToString().ToLowerInvariant())"
            )
        }
        else {
            $createArguments = @(
                "release", "create", $Version,
                $zipPath,
                $hashPath,
                "--verify-tag",
                "--title", $ReleaseName,
                "--notes-file", $notesTemp
            )
            if ($Draft) {
                $createArguments += "--draft"
            }
            if ($Prerelease) {
                $createArguments += "--prerelease"
            }
            Invoke-Native -Command "gh" -Arguments $createArguments
        }
    }
    finally {
        Remove-Item -LiteralPath $notesTemp -Force -ErrorAction SilentlyContinue
    }

    Write-Host "`nRelease published successfully: $Version"
}
finally {
    Pop-Location
}
