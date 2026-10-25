param([string]$ReleaseManifest,[string]$InstallerPath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $InstallerPath){$InstallerPath=Join-Path $repoRoot 'Install.ps1'}
$fixtureRoot=Join-Path $PSScriptRoot ('installer-fixture-'+[Guid]::NewGuid().ToString('N'))
$packageRoot=Join-Path $fixtureRoot 'package'
$gameRoot=Join-Path $fixtureRoot 'game'
$utf8=New-Object Text.UTF8Encoding($false)
$checks=[Collections.Generic.List[string]]::new()
function Assert-Installer([bool]$Condition,[string]$Name){if(-not $Condition){throw $Name};$checks.Add($Name)}
function Write-Fixture([string]$Path,[string]$Text){
 New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
 [IO.File]::WriteAllText($Path,$Text,$utf8)
}
function Invoke-FixtureInstaller {
 # Only process discovery is isolated; the unmodified production installer runs
 # against the newly created dummy game directory, never the user's real game.
 function Get-Process {
  [CmdletBinding()]param([string[]]$Name)
  if(@($Name | Where-Object {$_ -notin @('TheBazaar','PigModConfigurator')}).Count){throw 'Unexpected process query'}
 }
 & (Join-Path $packageRoot 'Install.ps1') -GamePath $gameRoot | Out-Null
}
Write-Fixture (Join-Path $gameRoot 'TheBazaar.exe') 'dummy game marker'
Write-Fixture (Join-Path $gameRoot 'BepInEx/core/BepInEx.dll') 'dummy loader marker'
Write-Fixture (Join-Path $packageRoot 'Configurator/fixture.txt') 'test payload'
Copy-Item -LiteralPath $InstallerPath -Destination (Join-Path $packageRoot 'Install.ps1')
$entry=[ordered]@{source='Configurator/fixture.txt';target='PigModConfigurator/fixture.txt';sha256=(Get-FileHash -LiteralPath (Join-Path $packageRoot 'Configurator/fixture.txt') -Algorithm SHA256).Hash.ToLowerInvariant();bytes=12}
$brand=([string][char]0x5927)+[char]0x732A+[char]0x624E
$manifest=[ordered]@{schemaVersion=1;version='1.1.0';brand=$brand;files=@($entry)}
$manifestPath=Join-Path $packageRoot 'package-files.json'
Write-Fixture $manifestPath ($manifest | ConvertTo-Json -Depth 6)
Assert-Installer (-not ([IO.File]::ReadAllBytes($manifestPath)[0] -eq 239)) 'Fixture manifest is UTF-8 without BOM'
Invoke-FixtureInstaller
Assert-Installer ((Get-FileHash -LiteralPath (Join-Path $gameRoot $entry.target) -Algorithm SHA256).Hash -eq $entry.sha256) 'Real installer installs UTF-8 manifest with Chinese brand'
$settingsPath=Join-Path $gameRoot 'BepInEx/config/PigModSuite/settings.json'
Assert-Installer (Test-Path -LiteralPath $settingsPath) 'Initial settings generated'
$customSettings='{"schemaVersion":1,"modules":{"cards":false},"items":{"cards:fixture":false}}'
Write-Fixture $settingsPath $customSettings
Invoke-FixtureInstaller
Assert-Installer ((Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8) -eq $customSettings) 'Repeated install preserves user settings exactly'
Write-Fixture (Join-Path $packageRoot 'Configurator/fixture.txt') 'updated payload'
$entry.sha256=(Get-FileHash -LiteralPath (Join-Path $packageRoot 'Configurator/fixture.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
$entry.bytes=15
Write-Fixture $manifestPath ($manifest | ConvertTo-Json -Depth 6)
Invoke-FixtureInstaller
Assert-Installer ((Get-Content -LiteralPath (Join-Path $gameRoot $entry.target) -Raw -Encoding UTF8) -eq 'updated payload') 'Changed payload installed after hash verification'
$backupPayloads=@(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx/PigModSuite-backups') -Filter 'fixture.txt' -File -Recurse)
Assert-Installer (@($backupPayloads | Where-Object {(Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8) -eq 'test payload'}).Count -eq 1) 'Previous payload backed up before overwrite'
Write-Fixture (Join-Path $packageRoot 'Configurator/fixture.txt') 'corrupt payload'
Write-Fixture (Join-Path $gameRoot $entry.target) 'existing user file'
$rejected=$false
try{Invoke-FixtureInstaller}catch{if($_.Exception.Message -notlike 'Package hash mismatch:*'){throw};$rejected=$true}
Assert-Installer $rejected 'Corrupt package file rejected'
Assert-Installer ((Get-Content -LiteralPath (Join-Path $gameRoot $entry.target) -Raw -Encoding UTF8) -eq 'existing user file') 'Failed verification leaves existing game files untouched'
if($ReleaseManifest){
 $release=Get-Content -LiteralPath $ReleaseManifest -Raw -Encoding UTF8 | ConvertFrom-Json
 Assert-Installer ($release.schemaVersion -eq 1 -and $release.brand -eq $brand -and $release.files.Count -gt 6000) 'Full release manifest parses with Chinese brand and all file records'
}
$report=[ordered]@{powershell=$PSVersionTable.PSVersion.ToString();defaultEncoding=[Text.Encoding]::Default.WebName;passed=$checks.Count;checks=$checks.ToArray();scope='Production installer on disposable dummy game; process discovery isolated; no real game changes'}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-report.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 6
