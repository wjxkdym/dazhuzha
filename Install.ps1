param([string]$GamePath)
$ErrorActionPreference='Stop'
if(-not $GamePath){
 Add-Type -AssemblyName System.Windows.Forms
 $suiteDialog=New-Object System.Windows.Forms.FolderBrowserDialog
 $suiteDialog.Description='Select The Bazaar folder containing TheBazaar.exe'
 if($suiteDialog.ShowDialog() -ne 'OK'){return}
 $GamePath=$suiteDialog.SelectedPath
}
$suiteGame=[IO.Path]::GetFullPath($GamePath).TrimEnd('\','/')
function Assert-Closed {if(Get-Process -Name TheBazaar -ErrorAction SilentlyContinue){throw 'Exit The Bazaar before installing. Game files were not updated.'}}
Assert-Closed
if(-not (Test-Path -LiteralPath (Join-Path $suiteGame 'TheBazaar.exe'))){throw 'Invalid game directory'}
if(-not (Test-Path -LiteralPath (Join-Path $suiteGame 'BepInEx/core/BepInEx.dll'))){throw 'BepInEx must already be installed. This package does not replace the game loader.'}
if(Get-Process -Name PigModConfigurator -ErrorAction SilentlyContinue){throw 'Close Pig Mod Configurator before installing.'}
$suiteManifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($suiteManifest.schemaVersion -ne 1){throw 'Unsupported package manifest'}
$suiteAllowed=@('PigCardArt','PigEncounterArt','PigPortraitArt','PigStorageArt','PigMainMenuArt')
function Resolve-Target([string]$Relative){
 $resolved=[IO.Path]::GetFullPath((Join-Path $suiteGame $Relative))
 if(-not $resolved.StartsWith($suiteGame+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Path escapes game directory'}
 $normalized=$Relative.Replace('\','/')
 $allowed=$normalized.StartsWith('PigModConfigurator/') -or $normalized.StartsWith('BepInEx/config/PigModSuite/') -or $normalized -eq 'Open-PigModConfigurator.cmd'
 foreach($plugin in $suiteAllowed){if($normalized.StartsWith('BepInEx/plugins/'+$plugin+'/')){$allowed=$true}}
 if(-not $allowed){throw ('Unexpected install target: '+$Relative)}
 return $resolved
}
function Legacy-Enabled([string]$File,[string]$Key){
 $path=Join-Path $suiteGame ('BepInEx/config/'+$File)
 if(-not (Test-Path -LiteralPath $path)){return $true}
 $general=$false
 foreach($line in Get-Content -LiteralPath $path){
  $trim=$line.Trim();if($trim.StartsWith('[')){$general=$trim -eq '[General]'}
  if($general -and $trim -match ('^'+[regex]::Escape($Key)+'\s*=\s*(true|false)\s*$')){return $Matches[1] -eq 'true'}
 }
 return $true
}
$suiteStamp=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$suiteBackup=Join-Path $suiteGame ('BepInEx/PigModSuite-backups/'+$suiteStamp)
New-Item -ItemType Directory -Path $suiteBackup -Force | Out-Null
$suitePlan=[Collections.Generic.List[object]]::new()
$suiteSkipped=0
Write-Output 'Checking package and existing files...'
foreach($entry in $suiteManifest.files){
 $target=Resolve-Target $entry.target
 $source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $entry.source))
 if(-not $source.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source escapes package'}
 if(-not (Test-Path -LiteralPath $source)){throw ('Missing package file: '+$entry.source)}
 if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $entry.sha256){$suiteSkipped++;continue}
 if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256){throw ('Package hash mismatch: '+$entry.source)}
 $suitePlan.Add([pscustomobject]@{relative=$entry.target;source=$source;sha256=$entry.sha256;existed=(Test-Path -LiteralPath $target)})
}
$suiteGenerated=Join-Path $suiteBackup 'generated'
New-Item -ItemType Directory -Path $suiteGenerated -Force | Out-Null
function Add-Generated([string]$Relative,[string]$Text){
 $file=Join-Path $suiteGenerated ([Guid]::NewGuid().ToString('N')+'.json')
 [IO.File]::WriteAllText($file,$Text,(New-Object Text.UTF8Encoding($false)))
 $target=Resolve-Target $Relative
 $suitePlan.Add([pscustomobject]@{relative=$Relative;source=$file;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash;existed=(Test-Path -LiteralPath $target)})
}
$suiteConfig=Resolve-Target 'BepInEx/config/PigModSuite/settings.json'
if(-not (Test-Path -LiteralPath $suiteConfig)){
 $encounter=Legacy-Enabled 'local.pigencounterart.cfg' 'Enabled'
 $initial=[ordered]@{schemaVersion=1;modules=[ordered]@{cards=(Legacy-Enabled 'local.pigcardart.cfg' 'Enabled');heroes=(Legacy-Enabled 'local.pigportraitart.cfg' 'HeroBaseSkins');events=$encounter;monsters=$encounter;storage=(Legacy-Enabled 'local.pigstorageart.cfg' 'Enabled');mainmenu=(Legacy-Enabled 'local.pigmainmenuart.cfg' 'Enabled')};items=@{};updatedAt=(Get-Date).ToString('o')}
 Add-Generated 'BepInEx/config/PigModSuite/settings.json' ($initial|ConvertTo-Json -Depth 8)
}
$suiteInstallation=[ordered]@{version='1.1.0';gamePath=$suiteGame;installedAt=(Get-Date).ToString('o');backup=$suiteBackup}
Add-Generated 'PigModConfigurator/installation.json' ($suiteInstallation|ConvertTo-Json -Depth 5)
Add-Generated 'BepInEx/config/PigModSuite/installation.json' ($suiteInstallation|ConvertTo-Json -Depth 5)
Assert-Closed
foreach($record in $suitePlan){
 if($record.existed){$backupFile=Join-Path $suiteBackup ('files/'+$record.relative);New-Item -ItemType Directory -Path (Split-Path -Parent $backupFile) -Force | Out-Null;Copy-Item -LiteralPath (Resolve-Target $record.relative) -Destination $backupFile -Force}
}
$suitePlan | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $suiteBackup 'files.json') -Encoding UTF8
$suiteWritten=[Collections.Generic.List[object]]::new()
try{
 foreach($record in $suitePlan){
  Assert-Closed
  $target=Resolve-Target $record.relative
  New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
  $suiteWritten.Add($record)
  Copy-Item -LiteralPath $record.source -Destination $target -Force
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $record.sha256){throw ('Installed file mismatch: '+$record.relative)}
 }
}catch{
 foreach($record in $suiteWritten){
  $target=Resolve-Target $record.relative
  if($record.existed){Copy-Item -LiteralPath (Join-Path $suiteBackup ('files/'+$record.relative)) -Destination $target -Force}
  elseif(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
 }
 throw
}
# Allows the package's launch shortcut to reopen this installation too.
$suiteInstallation | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Configurator/installation.json') -Encoding UTF8
$suiteResult=[ordered]@{version='1.1.0';installedAt=(Get-Date).ToString('o');gamePath=$suiteGame;backup=$suiteBackup;changedFiles=$suitePlan.Count;unchangedVerifiedFiles=$suiteSkipped;configPreserved=$true;configurator=(Join-Path $suiteGame 'PigModConfigurator/PigModConfigurator.exe');inGameVisualVerified=$false}
$suiteResult | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot '安装结果.json') -Encoding UTF8
$suiteResult | ConvertTo-Json -Depth 5
