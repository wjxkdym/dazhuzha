$ErrorActionPreference='Stop'
$suiteRoot=Split-Path -Parent $PSScriptRoot
$suiteSource=Get-Content -LiteralPath (Join-Path $suiteRoot 'src/shared/SuiteSettings.cs') -Raw
$suiteWrapper=@'
namespace PigModSuite {
 public static class GateProbe {
  public static bool Module(string dir,string module,bool fallback){return SuiteSettings.Load(dir,delegate(string x){}).ModuleEnabled(module,fallback);}
  public static bool Item(string dir,string module,string id){return SuiteSettings.Load(dir,delegate(string x){}).ItemEnabled(module,id);}
  public static string Group(string name){return SuiteSettings.MenuGroup(name);}
 }
}
'@
Add-Type -TypeDefinition ($suiteSource+$suiteWrapper) -ReferencedAssemblies (Join-Path $PSHOME 'Newtonsoft.Json.dll') -IgnoreWarnings
$suiteFixture=Join-Path $PSScriptRoot ('gate-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $suiteFixture 'PigModSuite') -Force | Out-Null
$suiteSettingsFile=Join-Path $suiteFixture 'PigModSuite/settings.json'
$suiteChecks=[Collections.Generic.List[string]]::new()
function Assert-Gate([bool]$Value,[string]$Name){if(-not $Value){throw $Name};$suiteChecks.Add($Name)}
Assert-Gate ([PigModSuite.GateProbe]::Module($suiteFixture,'cards',$false) -eq $false) 'Missing suite uses legacy disabled setting'
Assert-Gate ([PigModSuite.GateProbe]::Module($suiteFixture,'cards',$true)) 'Missing suite uses legacy enabled setting'
$suiteSettings=@{schemaVersion=1;modules=@{cards=$true;heroes=$true;events=$false;monsters=$true;storage=$true;mainmenu=$true};items=@{'cards:abc'=$false;'heroes:Skin_DOO_01a'=$false;'mainmenu:blue-merchant'=$false;'events:Event_Test'=$false;'monsters:Monster_Test'=$false;'storage:chest'=$false}}
$suiteSettings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $suiteSettingsFile -Encoding utf8
Assert-Gate ([PigModSuite.GateProbe]::Module($suiteFixture,'cards',$false)) 'Unified setting overrides legacy module'
Assert-Gate (-not [PigModSuite.GateProbe]::Module($suiteFixture,'events',$true)) 'Events disabled independently'
Assert-Gate ([PigModSuite.GateProbe]::Module($suiteFixture,'monsters',$false)) 'Monsters remain enabled independently'
foreach($suitePair in @(@('cards','abc'),@('heroes','Skin_DOO_01a'),@('mainmenu','blue-merchant'),@('events','Event_Test'),@('monsters','Monster_Test'),@('storage','chest'))){Assert-Gate (-not [PigModSuite.GateProbe]::Item($suiteFixture,$suitePair[0],$suitePair[1])) ('Per-item opt-out: '+$suitePair[0])}
Assert-Gate ([PigModSuite.GateProbe]::Item($suiteFixture,'cards','different-id')) 'Sibling cards remain enabled'
$suiteLayers=Get-Content -LiteralPath (Join-Path $suiteRoot 'BepInEx/plugins/PigMainMenuArt/layers.json') -Raw | ConvertFrom-Json
$suiteCatalog=Get-Content -LiteralPath (Join-Path $suiteRoot 'Configurator/catalog.json') -Raw | ConvertFrom-Json
foreach($suiteLayer in $suiteLayers.layers){$suiteGroup=[PigModSuite.GateProbe]::Group($suiteLayer.name);Assert-Gate ($suiteCatalog.items.id -contains ('mainmenu:'+$suiteGroup)) ('Menu layer bound to catalog: '+$suiteLayer.name)}
'{broken' | Set-Content -LiteralPath $suiteSettingsFile
Assert-Gate (-not [PigModSuite.GateProbe]::Module($suiteFixture,'cards',$true)) 'Malformed config fails closed'
Assert-Gate (-not [PigModSuite.GateProbe]::Item($suiteFixture,'cards','different-id')) 'Malformed config cannot silently enable art'
$suiteSettings.schemaVersion=99
$suiteSettings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $suiteSettingsFile
Assert-Gate (-not [PigModSuite.GateProbe]::Module($suiteFixture,'cards',$true)) 'Unknown schema fails closed'
@{passed=$suiteChecks.Count;checks=$suiteChecks;scope='Compiled production SuiteSettings source executed on isolated fixtures'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'gates-report.json') -Encoding utf8
Write-Output ('Passed '+$suiteChecks.Count+' production configuration gate checks.')
