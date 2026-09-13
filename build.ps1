$ErrorActionPreference='Stop'
$moxuFramework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$moxuOutput = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $moxuOutput | Out-Null
$moxuRefs=@('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$moxuArgs=@('/nologo','/target:winexe','/optimize+','/codepage:65001',('/out:'+(Join-Path $moxuOutput 'Moxu.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+(Join-Path $PSScriptRoot 'App.ico')),('/resource:'+(Join-Path $PSScriptRoot 'App.xaml')+',App.xaml'),('/resource:'+(Join-Path $PSScriptRoot 'App.ico')+',App.ico'))
foreach($moxuRef in $moxuRefs){$moxuArgs+=('/reference:'+(Join-Path $moxuFramework $moxuRef))}
$moxuArgs+=(Join-Path $PSScriptRoot 'Moxu.cs')
& (Join-Path $moxuFramework 'csc.exe') @moxuArgs
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}

