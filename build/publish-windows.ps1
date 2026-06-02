param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [switch]$IncludeSymbols
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/Hisa.App/Hisa.App.csproj"
[xml]$project = Get-Content -LiteralPath $projectPath
$versionNode = $project.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Project version was not found in: $projectPath"
}
$version = $versionNode.InnerText.Trim()
$publishRoot = Join-Path $repoRoot "build/releases/windows/$Runtime"
$publishDir = Join-Path $publishRoot "publish"
$zipPath = Join-Path $publishRoot "HISA-$Runtime-v$version.zip"

Write-Host "Publishing HISA for Windows ($Runtime)..."

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

dotnet publish $projectPath `
    -c $Configuration `
    -f $Framework `
    -r $Runtime `
    --self-contained true `
    -p:PublishProfile=WindowsSingleFile `
    -o $publishDir

if (-not (Test-Path $publishDir)) {
    throw "Publish output folder was not created: $publishDir"
}

if (-not $IncludeSymbols) {
    Get-ChildItem -Path $publishDir -Filter *.pdb -File | Remove-Item -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Publish completed:"
Write-Host "  Folder: $publishDir"
Write-Host "  Zip:    $zipPath"
