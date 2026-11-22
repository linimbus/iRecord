param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "Publishing iRecord 1.0.0 ($Configuration / $Runtime, framework-dependent)..." -ForegroundColor Cyan

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

Copy-Item .\README.md ".\$Output\README.md" -Force

Write-Host ""
Write-Host "Done. Output folder: $root\$Output" -ForegroundColor Green
Get-ChildItem ".\$Output" | Select-Object Name, Length | Format-Table -AutoSize
