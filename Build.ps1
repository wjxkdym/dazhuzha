param([string]$GamePath,[switch]$ConfiguratorOnly)
$ErrorActionPreference='Stop'
$suiteCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $suiteCompiler)){throw '.NET Framework C# compiler not found.'}
if(-not $ConfiguratorOnly){
if(-not $GamePath){throw 'Specify -GamePath for your installed The Bazaar, or use -ConfiguratorOnly.'}
$suiteManaged=Join-Path $GamePath 'TheBazaar_Data\Managed'
$suiteCore=Join-Path $GamePath 'BepInEx\core'
$suiteRefs=@('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','TheBazaarRuntime.dll','BazaarGameShared.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','Unity.Collections.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','Unity.Addressables.dll','Unity.ResourceManager.dll','Newtonsoft.Json.dll','spine-unity.dll','spine-csharp.dll') | ForEach-Object { '/reference:'+(Join-Path $suiteManaged $_) }
$suiteRefs+=@('BepInEx.dll','0Harmony.dll') | ForEach-Object { '/reference:'+(Join-Path $suiteCore $_) }
foreach($suiteName in @('PigCardArt','PigEncounterArt','PigPortraitArt','PigStorageArt','PigMainMenuArt')){
 New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('BepInEx\plugins\'+$suiteName)) -Force | Out-Null
 $suiteSources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot ('src\'+$suiteName)) -Filter '*.cs' | ForEach-Object FullName)
 $suiteSources+=Join-Path $PSScriptRoot 'src\shared\SuiteSettings.cs'
 & $suiteCompiler /nologo /noconfig /nostdlib /optimize+ /target:library ('/out:'+(Join-Path $PSScriptRoot ('BepInEx\plugins\'+$suiteName+'\'+$suiteName+'.dll'))) $suiteRefs $suiteSources
 if($LASTEXITCODE -ne 0){throw ('Compilation failed: '+$suiteName)}
}
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'Configurator') -Force | Out-Null
& $suiteCompiler /nologo /optimize+ /target:winexe /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/win32icon:'+(Join-Path $PSScriptRoot 'Configurator\web\dazhuzha.ico')) ('/out:'+(Join-Path $PSScriptRoot 'Configurator\PigModConfigurator.exe')) (Join-Path $PSScriptRoot 'src\Configurator.cs')
if($LASTEXITCODE -ne 0){throw 'Configurator compilation failed'}
Write-Output 'Dazhuzha build completed.'
