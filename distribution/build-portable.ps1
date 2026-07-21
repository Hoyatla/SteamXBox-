param(
    [string]$PublishedExePath = "artifacts\SteamXBox-portable-win-x64\SteamXBox.exe",
    [string]$OutputRoot = "dist\SteamXBox-portable-win-x64"
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$publishedExe = Join-Path $workspaceRoot $PublishedExePath
$portableTemplateRoot = Join-Path $PSScriptRoot "portable"
$outputRoot = Join-Path $workspaceRoot $OutputRoot
$zipPath = "$outputRoot.zip"

if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published executable not found: $publishedExe"
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot | Out-Null

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $outputRoot "SteamXBox.exe")
Copy-Item -LiteralPath (Join-Path $workspaceRoot "USAGE.txt") -Destination (Join-Path $outputRoot "USAGE.txt")
Copy-Item -LiteralPath (Join-Path $workspaceRoot "LICENSE") -Destination (Join-Path $outputRoot "LICENSE-LGPL-3.0.txt")
Copy-Item -LiteralPath (Join-Path $workspaceRoot "COPYING") -Destination (Join-Path $outputRoot "COPYING-GPL-3.0.txt")
Copy-Item -Path (Join-Path $portableTemplateRoot "*.cmd") -Destination $outputRoot

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $outputRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Portable distribution created:"
Write-Host "  Folder: $outputRoot"
Write-Host "  Zip:    $zipPath"
