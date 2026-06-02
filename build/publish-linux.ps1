param(
    [string[]]$Runtimes = @("linux-x64", "linux-arm64", "linux-musl-x64"),
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
$releaseRoot = Join-Path $repoRoot "build/releases/linux"

foreach ($runtime in $Runtimes) {
    $publishRoot = Join-Path $releaseRoot $runtime
    $publishDir = Join-Path $publishRoot "publish"
    $archivePath = Join-Path $publishRoot "HISA-$runtime-v$version.tar.gz"

    Write-Host "Publishing HISA for Linux ($runtime)..."

    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    if (Test-Path $archivePath) {
        Remove-Item -Force $archivePath
    }

    dotnet publish $projectPath `
        -c $Configuration `
        -f $Framework `
        -r $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $publishDir

    if (-not (Test-Path $publishDir)) {
        throw "Publish output folder was not created: $publishDir"
    }

    if (-not $IncludeSymbols) {
        Get-ChildItem -Path $publishDir -Filter *.pdb -File | Remove-Item -Force
    }

    tar -czf $archivePath -C $publishDir .
}

Write-Host ""
Write-Host "Linux publish completed:"
foreach ($runtime in $Runtimes) {
    $publishRoot = Join-Path $releaseRoot $runtime
    $publishDir = Join-Path $publishRoot "publish"
    $archivePath = Join-Path $publishRoot "HISA-$runtime-v$version.tar.gz"
    Write-Host "  [$runtime]"
    Write-Host "    Folder:  $publishDir"
    Write-Host "    Archive: $archivePath"
}
