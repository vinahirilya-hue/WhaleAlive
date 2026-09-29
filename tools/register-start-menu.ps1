param([string]$ProjectRoot = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$executable = Join-Path $projectPath 'prototype\bin\Release\net9.0-windows\WhaleAlive.exe'
if(!(Test-Path -LiteralPath $executable)){throw 'Build the Release application first.'}
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Whale Alive.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $projectPath
$shortcut.Description = 'Whale Alive 桌宠'
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()
Write-Output "Start menu shortcut: $shortcutPath"
