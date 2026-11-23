param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "publish",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$project = Get-Content (Join-Path $root "iRecord.csproj")
$version = [string]($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }

$outputPath = Join-Path $root $Output

if (-not $SkipBuild) {
    Write-Host "Building and publishing iRecord v$version ($Configuration / $Runtime)..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build.ps1") -Runtime $Runtime -Configuration $Configuration -Output $Output
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$requiredFiles = @("iRecord.exe", "iRecord-cli.exe")
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Join-Path $outputPath $file))) {
        throw "Missing '$file' in '$outputPath'. Run without -SkipBuild first."
    }
}

$stage = Join-Path $outputPath "_stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

try {
    foreach ($file in $requiredFiles) {
        Copy-Item (Join-Path $outputPath $file) $stage
    }

    foreach ($doc in @("README.md", "README_en.md")) {
        $source = Join-Path $root $doc
        if (Test-Path $source) { Copy-Item $source $stage }
    }

    $skills = Join-Path $root "skills"
    if (Test-Path $skills) {
        $target = Join-Path $stage "skills"
        New-Item -ItemType Directory -Path $target | Out-Null
        Copy-Item (Join-Path $skills "*") $target -Recurse
    }

    $zipName = "iRecord-v$version-$Runtime.zip"
    $zipPath = Join-Path $outputPath $zipName
    Get-ChildItem $outputPath -Filter "iRecord-v*.zip" -File | Remove-Item -Force -ErrorAction SilentlyContinue

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

    Write-Host ""
    Write-Host "Package created: $zipPath" -ForegroundColor Green
    Get-Item $zipPath |
        Select-Object Name, @{ Name = "SizeMB"; Expression = { [math]::Round($_.Length / 1MB, 2) } } |
        Format-Table -AutoSize
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
}
