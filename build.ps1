[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Package,
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputPath = Join-Path $projectRoot "dist\windows-$Runtime"
$reportPath = Join-Path $projectRoot '.build\windows\self-test.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $reportPath) | Out-Null

# Reject accidental drift from the Swift scene/eye geometry before compiling.
$layout = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Windows\SceneLayout.json') | ConvertFrom-Json
$swift = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'PackedScene.swift')
$jarSwift = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Jar.swift')
if ($jarSwift -notmatch 'sceneRect = NSRect\(x: 25, y: 42, width: 210, height: 210\)' -or
    ($layout.sceneRect -join ',') -ne '25,8,210,210' -or
    $jarSwift -notmatch 'scale\(x: 0.90, y: 0.967\)') { throw 'Swift JarGeometry changed. Synchronize the Windows coordinate conversion before building.' }
$spritePlacement = [regex]::Match($jarSwift, 'claudePlacement = NSRect\(x: ([0-9.]+), y: ([0-9.]+), width: ([0-9.]+), height: ([0-9.]+)\)')
$spriteNumbers = @($spritePlacement.Groups | Select-Object -Skip 1 | ForEach-Object { [double]::Parse($_.Value,[cultureinfo]::InvariantCulture) })
if (($spriteNumbers -join ',') -ne ($layout.sprites.claude -join ',')) { throw 'Claude sprite placement drift between native platforms.' }
$claudeAtlas = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Resources\claude-eyes.json') | ConvertFrom-Json
if ($claudeAtlas.sourceImageSha256 -ne (Get-FileHash -LiteralPath (Join-Path $projectRoot 'Resources\pet-claude.png') -Algorithm SHA256).Hash) { throw 'Claude eye atlas is stale. Regenerate with PetJar.exe --export-claude-eyes <Resources directory>.' }
for ($eyeIndex=0; $eyeIndex -lt 2; $eyeIndex++) {
    for ($axis=0; $axis -lt 4; $axis++) {
        if ([math]::Abs($claudeAtlas.eyes[$eyeIndex].eye[$axis] - $layout.eyes.claude[$eyeIndex][$axis]) -gt 0.000000001) { throw 'Claude eye atlas coordinates changed. Regenerate the shared eye assets.' }
    }
}
foreach ($client in @('chatgpt','grok','muse','claude')) {
    $region = [regex]::Match($swift, "case \.$($client):\s*raw = \[(.*?)\]\s*(?:case|\})", 'Singleline')
    $numbers = [regex]::Matches($region.Groups[1].Value, '[0-9]+\.[0-9]+') | ForEach-Object { [double]::Parse($_.Value,[cultureinfo]::InvariantCulture) }
    $expected = @($layout.regions.$client | ForEach-Object { $_ })
    if (($numbers -join ',') -ne ($expected -join ',')) { throw "Character outline drift: $client" }
    $eyes = [regex]::Match($swift, "case \.$($client): raw = \[(.*?)\]", 'Singleline')
    $numbers = [regex]::Matches($eyes.Groups[1].Value, '[0-9]+\.[0-9]+') | ForEach-Object { [double]::Parse($_.Value,[cultureinfo]::InvariantCulture) }
    $expected = @($layout.eyes.$client | ForEach-Object { $_ })
    if (($numbers -join ',') -ne ($expected -join ',')) { throw "Eye patch drift: $client" }
}

$publishArgs = @('publish', (Join-Path $projectRoot 'Windows\PetJar.Windows.csproj'), '-c', 'Release', '-r', $Runtime, '-o', $outputPath, '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false')
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
$exePath = Join-Path $outputPath 'PetJar.exe'
if ($Runtime -eq 'win-x64' -and $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { Write-Warning 'Testing the x64 build under emulation.' }
$test = Start-Process -FilePath $exePath -ArgumentList @('--self-test', '--test-report', "`"$reportPath`"") -Wait -PassThru
if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath }
if ($test.ExitCode -ne 0) { throw "Windows self-test failed (exit $($test.ExitCode))." }
if ($Package) {
    $zipPath = Join-Path $projectRoot "dist\宠物罐-Windows-$Runtime.zip"
    Compress-Archive -LiteralPath @($exePath,(Join-Path $outputPath 'THIRD-PARTY-NOTICES.txt')) -DestinationPath $zipPath -Force
    Write-Output "Packaged: $zipPath"
}
Write-Output "Built: $exePath"
if ($Run) { Start-Process -FilePath $exePath -WindowStyle Hidden }
