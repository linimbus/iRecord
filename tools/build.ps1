param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$project = Get-Content (Join-Path $root "iRecord.csproj")
$version = [string]($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

Write-Host "Publishing iRecord $version ($Configuration / $Runtime, framework-dependent)..." -ForegroundColor Cyan

dotnet publish .\iRecord.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o ".\$Output"

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Publishing iRecord-cli ($Configuration / $Runtime, framework-dependent)..." -ForegroundColor Cyan

dotnet publish .\tools\iRecord.Cli\iRecord.Cli.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o ".\$Output"

if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }

Remove-Item ".\$Output\iRecord.runtimeconfig.json" -ErrorAction SilentlyContinue

Copy-Item .\README.md ".\$Output\README.md" -Force
Copy-Item .\README_en.md ".\$Output\README_en.md" -Force
if (Test-Path .\docs) {
    Copy-Item .\docs ".\$Output\docs" -Recurse -Force
}

Write-Host ""
Write-Host "Done. Output folder: $root\$Output" -ForegroundColor Green
Get-ChildItem ".\$Output" | Select-Object Name, Length | Format-Table -AutoSize
