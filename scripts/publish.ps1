$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    # Always publish into a fresh directory; never zip an existing user-modified folder.
    $publishDirectory = Join-Path $projectRoot ('artifacts/publish/' + [Guid]::NewGuid().ToString('N'))
    dotnet publish src/UnityLingo/UnityLingo.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $publishDirectory 'README.md')
    $unexpected = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
        $_.Name -match '(?i)(^settings.*\.json$|^appsettings\.local\.json$|\.db($|-)|^\.env($|\.)|\.log$|\.pdb$|credential|secret)'
    })
    if ($unexpected.Count -gt 0) { throw 'Unexpected user data or debug files in publish directory. Release aborted.' }
    $zipPath = Join-Path $projectRoot 'artifacts/UnityLingo-win-x64.zip'
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -Force
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  UnityLingo-win-x64.zip" | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/SHA256SUMS.txt') -Encoding ascii
    Write-Output "Published: $zipPath"
    Write-Output "SHA256: $hash"
} finally {
    Pop-Location
}
