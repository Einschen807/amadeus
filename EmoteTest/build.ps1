$ErrorActionPreference = 'Stop'
$taskDir = $PSScriptRoot
$lib = Join-Path $taskDir '../extracted_emote/tools/FreeMote/lib'
Copy-Item -LiteralPath (Join-Path $lib 'FreeMote.NET.dll'),(Join-Path $lib 'emotedriver.dll') -Destination $taskDir -Force
Copy-Item -LiteralPath (Join-Path $lib '../FreeMote.LICENSE.txt') -Destination $taskDir -Force
$framework = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$refs = @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path "$framework\WPF" $_) }
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/out:$taskDir\AmadeusTest.exe" "/r:$taskDir\FreeMote.NET.dll" "/r:$framework\System.Xaml.dll" $refs "$taskDir\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
