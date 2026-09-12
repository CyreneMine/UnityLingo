$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet publish src/UnityLingo/UnityLingo.csproj -c Release -r win-x64 --self-contained true -o artifacts/UnityLingo-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination artifacts/UnityLingo-win-x64/README.md
    Compress-Archive -Path artifacts/UnityLingo-win-x64/* -DestinationPath artifacts/UnityLingo-win-x64.zip -Force
    Get-FileHash artifacts/UnityLingo-win-x64.zip -Algorithm SHA256
} finally {
    Pop-Location
}
