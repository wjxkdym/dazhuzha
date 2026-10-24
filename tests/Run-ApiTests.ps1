$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $PSScriptRoot 'game'
$exe=Join-Path $root 'Configurator/PigModConfigurator.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Build first.'}
if(-not (Test-Path -LiteralPath (Join-Path $root 'Configurator/previews'))){throw 'API tests need the complete artwork package.'}
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $fixture 'TheBazaar.exe'),[byte[]]@())
$proc=Start-Process -FilePath $exe -ArgumentList @('--game',('"'+$fixture+'"'),'--headless','--no-browser') -WindowStyle Hidden -PassThru
try {
 $session=$null
 for($i=0;$i -lt 30;$i++){
  $session=Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'PigModSuite') -Filter '*.json' | ForEach-Object {try{Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json}catch{}} | Where-Object {$_.pid -eq $proc.Id} | Select-Object -First 1
  if($session){break}
  Start-Sleep -Milliseconds 200
 }
 if(-not $session){throw 'Test configurator did not start.'}
 & python (Join-Path $PSScriptRoot 'test_api.py')
 if($LASTEXITCODE -ne 0){throw 'API tests failed.'}
} finally {
 if($session){
  $token=$session.url.TrimEnd('/').Split('/')[-1]
  Invoke-RestMethod -Uri ($session.url+'api/quit') -Method Post -Headers @{'X-Pig-Token'=$token} -ContentType 'application/json' -Body '{}' | Out-Null
 }
}
