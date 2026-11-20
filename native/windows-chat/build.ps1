param([string]$OutDir = (Join-Path $PSScriptRoot '..\..\dist\windows-chat'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$out = (New-Item -ItemType Directory -Force $OutDir).FullName
$references = @('System.Windows.Forms', 'System.Drawing',
    'WPF\UIAutomationClient', 'WPF\UIAutomationTypes', 'WPF\WindowsBase') | ForEach-Object {
        "/reference:$(Join-Path $framework ($_ + '.dll'))"
    }
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$icon = Join-Path $PSScriptRoot '..\..\resources\windows\hex.ico'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 "/win32icon:$icon" "/out:$out\HexChat.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw "Échec de compilation ($LASTEXITCODE)" }
