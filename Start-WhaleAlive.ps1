$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$executable = Join-Path $projectRoot 'prototype\bin\Release\net9.0-windows\WhaleAlive.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    dotnet build (Join-Path $projectRoot 'prototype\WhaleAlive.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Whale Alive build failed.' }
}
$running = Get-Process -Name 'WhaleAlive' -ErrorAction SilentlyContinue | Where-Object {
    try { [System.IO.Path]::GetFullPath($_.Path) -eq [System.IO.Path]::GetFullPath($executable) } catch { $false }
}
if (-not $running) {
    Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Hidden
}
