param([Parameter(Mandatory=$true)][string]$Repository,[Parameter(Mandatory=$true)][string]$Artifacts)
$ErrorActionPreference='Stop'
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Use owner/repository.'}
if(-not (Get-Command gh -ErrorAction SilentlyContinue)){throw 'Install GitHub CLI and run gh auth login first.'}
$releaseRoot=[IO.Path]::GetFullPath($Artifacts)
$report=Get-Content -LiteralPath (Join-Path $releaseRoot 'release-report.json') -Raw | ConvertFrom-Json
$files=@()
foreach($entry in $report.archives){
 $file=[IO.Path]::GetFullPath((Join-Path $releaseRoot $entry.file))
 if(-not $file.StartsWith($releaseRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid archive path'}
 if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256){throw ('Hash mismatch: '+$entry.file)}
 $files+=$file
}
$files+=Join-Path $releaseRoot 'SHA256SUMS.txt'
$files+=Join-Path $releaseRoot 'release-report.json'
$tag='v'+$report.version
& gh release create $tag @files --repo $Repository --draft --prerelease --verify-tag --title ('大猪扎 '+$tag) --notes-file (Join-Path $releaseRoot 'RELEASE_NOTES.md')
if($LASTEXITCODE -ne 0){throw 'GitHub draft creation failed.'}
