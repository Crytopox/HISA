param(
    [ValidateSet("osx-x64", "osx-arm64")]
    [string[]]$Runtimes = @("osx-x64", "osx-arm64"),
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [switch]$IncludeSymbols
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$missingLibraries = foreach ($runtime in $Runtimes) {
    $library = Join-Path $repoRoot "build/xmpp-native/$runtime/libhisa-strophe.dylib"
    if (-not (Test-Path -LiteralPath $library -PathType Leaf)) {
        $library
    }
}
if ($missingLibraries) {
    $paths = $missingLibraries -join "`n"
    throw "Missing native XMPP libraries:`n$paths`nUse the Linux and macOS packages workflow in GitHub Actions, or build the missing native libraries before publishing locally."
}

$projectPath = Join-Path $repoRoot "src/Hisa.App/Hisa.App.csproj"
[xml]$project = Get-Content -LiteralPath $projectPath
$versionNode = $project.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Project version was not found in: $projectPath"
}

$version = $versionNode.InnerText.Trim()
$releaseRoot = Join-Path $repoRoot "build/releases/macos"

foreach ($runtime in $Runtimes) {
    $publishRoot = Join-Path $releaseRoot $runtime
    $publishDir = Join-Path $publishRoot "publish"
    $archivePath = Join-Path $publishRoot "HISA-$runtime-v$version.tar.gz"

    Write-Host "Publishing HISA for macOS ($runtime)..."

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
        -p:RequireXmppNative=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtime." }
    if (-not (Test-Path $publishDir)) {
        throw "Publish output folder was not created: $publishDir"
    }

    if (-not $IncludeSymbols) {
        Get-ChildItem -Path $publishDir -Filter *.pdb -File | Remove-Item -Force
    }

    tar -czf $archivePath -C $publishDir .
    if ($LASTEXITCODE -ne 0) { throw "Could not create the archive for $runtime." }
}

Write-Host ""
Write-Host "macOS publish completed:"
foreach ($runtime in $Runtimes) {
    $publishRoot = Join-Path $releaseRoot $runtime
    $publishDir = Join-Path $publishRoot "publish"
    $archivePath = Join-Path $publishRoot "HISA-$runtime-v$version.tar.gz"
    Write-Host "  [$runtime]"
    Write-Host "    Folder:  $publishDir"
    Write-Host "    Archive: $archivePath"
}
