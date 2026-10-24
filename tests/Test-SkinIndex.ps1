$ErrorActionPreference='Stop'
$skinSource=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/PigPortraitArt/HeroSkinIndex.cs') -Raw
$skinTests=@'
public static class SkinIndexTests {
 public static string[] Run() {
  var checks=new System.Collections.Generic.List<string>();
  var index=new PigPortraitArt.HeroSkinIndex<string>();string value;
  index.Add("Skin_STE_08a_SkinData","Skin_STE_08a","a");
  index.Add("Skin_STE_08b_SkinData","Skin_STE_08a","b");
  if(!index.TryGet("Skin_STE_08a_SkinData","Skin_STE_08a",out value)||value!="a")throw new System.Exception("A");checks.Add("First recolor resolves by exact native asset");
  if(!index.TryGet("Skin_STE_08b_SkinData","Skin_STE_08a",out value)||value!="b")throw new System.Exception("B");checks.Add("Second recolor with duplicate InternalName stays distinct");
  if(!index.TryGet("Skin_STE_08b_SkinData(Clone)","Skin_STE_08a",out value)||value!="b")throw new System.Exception("Clone");checks.Add("Unity clone suffix resolves");
  if(index.TryGet("OtherSkin","Skin_STE_08a",out value))throw new System.Exception("Unexpected fallback");checks.Add("Unknown skin never falls back to another modern recolor");
  var partial=new PigPortraitArt.HeroSkinIndex<string>();partial.Add("Skin_STE_08a_SkinData","Skin_STE_08a","a");
  if(partial.TryGet("Skin_STE_08b_SkinData","Skin_STE_08a",out value))throw new System.Exception("Disabled recolor replaced");checks.Add("Disabled or unfinished recolor retains original art");
  if(partial.TryGet(null,null,out value))throw new System.Exception("Null");checks.Add("Null identities safely retain original");
  partial.Add(null,"OldSkin","legacy");if(!partial.TryGet("OldSkinAsset","OldSkin",out value)||value!="legacy")throw new System.Exception("Legacy");checks.Add("Legacy manifest still resolves");
  bool rejected=false;try{index.Add("Skin_STE_08a_SkinData","Other","duplicate");}catch(System.ArgumentException){rejected=true;}
  if(!rejected)throw new System.Exception("Duplicate accepted");checks.Add("Duplicate asset identities rejected");
  return checks.ToArray();
 }
}
'@
Add-Type -TypeDefinition ($skinSource+[Environment]::NewLine+$skinTests)
$skinChecks=[SkinIndexTests]::Run()
@{passed=$skinChecks.Count;checks=$skinChecks} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'skin-index-report.json') -Encoding utf8
$skinChecks
