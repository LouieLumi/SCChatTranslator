param([string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$f="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc="$f\csc.exe";$src=Join-Path $root 'src';$build=Join-Path $root 'build';$payload=Join-Path $build 'payload';$dist=Join-Path $root 'dist'
if(!(Test-Path "$payload\runtime\python.exe")){throw 'Run python tools/prepare.py first'}
New-Item -ItemType Directory -Force $dist|Out-Null
$shared=@('/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+',"/win32manifest:$src\app.manifest","/win32icon:$src\app.ico",'/reference:System.Xaml.dll',"/reference:$f\WPF\PresentationCore.dll","/reference:$f\WPF\PresentationFramework.dll","/reference:$f\WPF\WindowsBase.dll")
& $csc @shared "/out:$payload\SCChatUI.exe" /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$src\App.cs" "$src\SettingsWindow.cs" "$src\ChildJob.cs" "$src\GameHotkeys.cs" "$src\ChatVisibilityPrompt.cs"
if($LASTEXITCODE -ne 0){throw 'UI compile failed'}
& $Python "$PSScriptRoot\package.py"
if($LASTEXITCODE -ne 0){throw 'Payload packaging failed'}
& $csc @shared "/out:$dist\SCChatTranslator.exe" /reference:System.IO.Compression.dll "/resource:$build\payload.zip,SCChat.Payload" "/resource:$build\payload.sha256,SCChat.Hash" "$src\Bootstrap.cs"
if($LASTEXITCODE -ne 0){throw 'Bootstrap compile failed'}
$hash=(Get-FileHash "$dist\SCChatTranslator.exe" -Algorithm SHA256).Hash.ToLower()
[IO.File]::WriteAllText("$dist\SHA256SUMS.txt","$hash  SCChatTranslator.exe`n")
Write-Output "Built $dist\SCChatTranslator.exe"
