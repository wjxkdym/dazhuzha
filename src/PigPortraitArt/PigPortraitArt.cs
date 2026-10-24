using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Spine.Unity;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PigPortraitArt
{
    [BepInPlugin(Id, "大猪扎 · 英雄皮肤", "1.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.pigportraitart";
        private static Plugin instance;
        private ConfigEntry<bool> enabledSetting;
        private bool suiteEnabled;
        private Harmony harmony;
        private readonly HeroSkinIndex<Entry> heroes = new HeroSkinIndex<Entry>();
        private readonly List<Entry> entries = new List<Entry>();

        internal sealed class Entry
        {
            internal string Key, SourceSprite, PortraitPath, FullPath, PortraitHash, FullHash;
            internal float PortraitPpu;
            internal Vector2 PortraitPivot;
            internal Vector4 FullContent;
            internal bool Creature, Failed;
            internal Texture2D PortraitTexture, FullTexture;
            internal Sprite PortraitSprite, FullSprite;
        }

        private PigModSuite.SuiteSettings suite;
        private void Awake()
        {
            suite = PigModSuite.SuiteSettings.Load(BepInEx.Paths.ConfigPath, delegate(string message) { Logger.LogWarning(message); });
            instance = this;
            enabledSetting = Config.Bind("General", "HeroBaseSkins", true, "Replace listed completed hero skins with local pig artwork. Restart after changing.");
            suiteEnabled = suite.ModuleEnabled("heroes", enabledSetting.Value);
            if (!suiteEnabled) return;
            try
            {
                LoadManifest();
                harmony = new Harmony(Id);
                Patch("LoadPortraitSpriteAsync", new Type[] { typeof(CancellationToken) }, "LoadSpritePrefix", null);
                Patch("LoadPortrait", new Type[] { typeof(bool) }, "LoadTexturePrefix", null);
                Patch("GenerateEncounterData", Type.EmptyTypes, null, "GeneratedDataPostfix");
                Patch("LoadSkinEditSkinAsync", new Type[] { typeof(SkinEdit.ESkinPlacement) }, null, "SkinModelPostfix");
                const string iface = "TheBazaar.Assets.Scripts.ScriptableObjectsScripts.IPortraitAssetData.";
                Patch(iface + "get_UseAnimatedPortrait", Type.EmptyTypes, "UseAnimatedPrefix", null);
                Patch(iface + "get_HasConfiguredPortraitSprite", Type.EmptyTypes, "HasSpritePrefix", null);
                Patch(iface + "get_PortraitPositionOffset", Type.EmptyTypes, "PositionPrefix", null);
                Patch(iface + "get_PortraitScaleOffset", Type.EmptyTypes, "ScalePrefix", null);
                Logger.LogInfo("Ready: " + entries.Count + " hero skins, portraits and full character artwork. Presentation hooks only.");
            }
            catch (Exception e)
            {
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Hero replacements disabled; original skins remain available. " + e);
            }
        }

        private void Patch(string name, Type[] args, string prefix, string postfix)
        {
            MethodInfo method = AccessTools.Method(typeof(SkinAssetDataSO), name, args);
            if (method == null) throw new MissingMethodException(typeof(SkinAssetDataSO).FullName, name);
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
        }

        private void LoadManifest()
        {
            string folder = Path.GetDirectoryName(Info.Location);
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "portraits.json")));
            if ((int?)manifest["schemaVersion"] != 1) throw new InvalidDataException("Unsupported portrait manifest.");
            foreach (JObject row in (JArray)manifest["heroes"])
            {
                string configId = (string)row["configId"] ?? (string)row["internalName"];
                if (!suite.ItemEnabled("heroes", configId)) continue;
                Entry e = new Entry();
                e.Key = configId;
                e.SourceSprite = (string)row["spriteName"];
                e.PortraitPath = LocalImage(folder, (string)row["file"]);
                e.FullPath = LocalImage(folder, (string)row["fullFile"]);
                e.PortraitHash = (string)row["sha256"];
                e.FullHash = (string)row["fullSha256"];
                e.PortraitPpu = (float)row["portraitPixelsPerUnit"];
                e.PortraitPivot = new Vector2((float)row["portraitPivot"][0], (float)row["portraitPivot"][1]);
                e.FullContent = new Vector4((float)row["fullContent"][0], (float)row["fullContent"][1], (float)row["fullContent"][2], (float)row["fullContent"][3]);
                e.Creature = (bool?)row["isCreatureForm"] == true;
                if (string.IsNullOrEmpty(e.Key) || e.PortraitPpu <= 0f || e.FullContent.z <= 0f || e.FullContent.w <= 0f)
                    throw new InvalidDataException("Invalid portrait geometry.");
                heroes.Add((string)row["assetName"], (string)row["internalName"], e);
                entries.Add(e);
            }
        }

        private static string LocalImage(string folder, string file)
        {
            if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Use a direct PNG file name.");
            return Path.Combine(Path.Combine(folder, "art"), file);
        }

        private static Texture2D LoadTexture(string path, string expectedHash)
        {
            FileInfo f = new FileInfo(path);
            if (!f.Exists || f.Length > 16 * 1024 * 1024) throw new InvalidDataException("Missing or oversized portrait: " + f.Name);
            byte[] bytes = File.ReadAllBytes(path);
            using (SHA256 hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Portrait hash mismatch: " + f.Name);
            }
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width > 4096 || texture.height > 4096)
                    throw new InvalidDataException("Invalid portrait image: " + f.Name);
                texture.name = "PigPortraitArt/" + f.Name;
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            }
            catch { Object.Destroy(texture); throw; }
        }

        private static bool TryEntry(SkinAssetDataSO skin, out Entry e)
        {
            e = null;
            Plugin p = instance;
            if (p == null || !p.suiteEnabled || skin == null || !p.heroes.TryGet(skin.name, skin.InternalName, out e) || e.Failed) return false;
            try
            {
                if (e.PortraitSprite == null)
                {
                    e.PortraitTexture = LoadTexture(e.PortraitPath, e.PortraitHash);
                    e.PortraitSprite = Sprite.Create(e.PortraitTexture, new Rect(0, 0, e.PortraitTexture.width, e.PortraitTexture.height), e.PortraitPivot, e.PortraitPpu, 0, SpriteMeshType.FullRect);
                    e.PortraitSprite.name = "PigPortraitArt/" + e.Key + "/Portrait";
                    e.PortraitSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                return true;
            }
            catch (Exception ex)
            {
                e.Failed = true;
                p.Logger.LogWarning("Original hero art retained for " + e.Key + ": " + ex.Message);
                return false;
            }
        }

        private static Sprite FullSprite(Entry e)
        {
            if (e.FullSprite != null) return e.FullSprite;
            e.FullTexture = LoadTexture(e.FullPath, e.FullHash);
            e.FullSprite = Sprite.Create(e.FullTexture, new Rect(0, 0, e.FullTexture.width, e.FullTexture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            e.FullSprite.name = "PigPortraitArt/" + e.Key + "/Full";
            e.FullSprite.hideFlags = HideFlags.HideAndDontSave;
            return e.FullSprite;
        }

        private static bool LoadSpritePrefix(SkinAssetDataSO __instance, CancellationToken __0, ref Task<Sprite> __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e)) return true;
            __result = __0.IsCancellationRequested ? Task.FromCanceled<Sprite>(__0) : Task.FromResult(e.PortraitSprite);
            return false;
        }
        private static bool LoadTexturePrefix(SkinAssetDataSO __instance, ref Task<Texture2D> __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e)) return true;
            __result = Task.FromResult(e.PortraitTexture);
            return false;
        }
        private static bool UseAnimatedPrefix(SkinAssetDataSO __instance, ref bool __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e)) return true;
            __result = false;
            return false;
        }
        private static bool HasSpritePrefix(SkinAssetDataSO __instance, ref bool __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e)) return true;
            __result = true;
            return false;
        }
        private static bool PositionPrefix(SkinAssetDataSO __instance, ref Vector3 __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e) || !e.Creature) return true;
            __result = new Vector3(.005f, .015f, -.22f);
            return false;
        }
        private static bool ScalePrefix(SkinAssetDataSO __instance, ref Vector3 __result)
        {
            Entry e;
            if (!TryEntry(__instance, out e) || !e.Creature) return true;
            __result = Vector3.one * .685f;
            return false;
        }

        private static void GeneratedDataPostfix(SkinAssetDataSO __instance, ref Task<EncounterAssetDataSO> __result)
        {
            Entry e;
            if (TryEntry(__instance, out e)) __result = ReplaceGeneratedData(__result, e);
        }
        private static async Task<EncounterAssetDataSO> ReplaceGeneratedData(Task<EncounterAssetDataSO> original, Entry e)
        {
            EncounterAssetDataSO data = await original;
            if (data == null || instance == null) return data;
            // GenerateEncounterData creates this instance for the caller. Its original
            // cleanup action continues to own and release the original asset handles.
            data.portraitTextureReference = e.PortraitSprite;
            data.animatedPortraitPrefabReference = null;
            if (e.Creature)
            {
                data.portraitPositionOffset = new Vector3(.005f, .015f, -.22f);
                data.portraitScaleOffset = Vector3.one * .685f;
            }
            return data;
        }

        private static void SkinModelPostfix(SkinAssetDataSO __instance, ref Task<Object> __result)
        {
            Entry e;
            if (TryEntry(__instance, out e)) __result = ReplaceSkinModel(__result, e);
        }
        private static async Task<Object> ReplaceSkinModel(Task<Object> original, Entry e)
        {
            Object result = await original;
            GameObject root = result as GameObject;
            if (root == null || instance == null) return result;
            try
            {
                Sprite full = FullSprite(e);
                int replaced = 0;
                // These are newly instantiated presentation objects, not shared prefabs.
                foreach (SkeletonGraphic graphic in root.GetComponentsInChildren<SkeletonGraphic>(true))
                {
                    graphic.Initialize(false);
                    graphic.Update(0f);
                    graphic.UpdateMesh();
                    Bounds bounds = new Bounds();
                    bool found = false;
                    Mesh mesh = graphic.GetLastMesh();
                    IncludeMesh(mesh, ref bounds, ref found);
                    var meshes = graphic.MeshesMultipleCanvasRenderers;
                    if (meshes != null) for (int i = 0; i < meshes.Count; i++) IncludeMesh(meshes.Items[i], ref bounds, ref found);
                    if (!found || bounds.size.x <= 0f || bounds.size.y <= 0f) continue;
                    Vector2 size, offset;
                    FitContent(e, bounds, out size, out offset);
                    GameObject child = new GameObject("PigPortraitArt/Full", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FadeFollower));
                    child.layer = graphic.gameObject.layer;
                    RectTransform rect = (RectTransform)child.transform;
                    rect.SetParent(graphic.transform, false);
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.pivot = new Vector2(.5f, .5f);
                    rect.sizeDelta = size;
                    rect.localPosition = new Vector3(offset.x, offset.y, 0f);
                    Image image = child.GetComponent<Image>();
                    image.sprite = full;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    child.GetComponent<FadeFollower>().SetGraphic(graphic, image);
                    graphic.enabled = false;
                    replaced++;
                }
                foreach (SkeletonRenderer skeleton in root.GetComponentsInChildren<SkeletonRenderer>(true))
                {
                    skeleton.Initialize(false, false);
                    SkeletonAnimation animation = skeleton as SkeletonAnimation;
                    if (animation != null) animation.Update(0f);
                    skeleton.LateUpdateMesh();
                    MeshFilter filter = skeleton.GetComponent<MeshFilter>();
                    MeshRenderer renderer = skeleton.GetComponent<MeshRenderer>();
                    if (filter == null || filter.sharedMesh == null || renderer == null) continue;
                    Bounds bounds = filter.sharedMesh.bounds;
                    if (bounds.size.x <= 0f || bounds.size.y <= 0f) continue;
                    Vector2 size, offset;
                    FitContent(e, bounds, out size, out offset);
                    GameObject child = new GameObject("PigPortraitArt/Full", typeof(SpriteRenderer), typeof(FadeFollower));
                    child.layer = skeleton.gameObject.layer;
                    child.transform.SetParent(skeleton.transform, false);
                    child.transform.localPosition = new Vector3(offset.x, offset.y, bounds.center.z);
                    child.transform.localScale = Vector3.one * (size.x / full.bounds.size.x);
                    SpriteRenderer sprite = child.GetComponent<SpriteRenderer>();
                    sprite.sprite = full;
                    sprite.sortingLayerID = renderer.sortingLayerID;
                    sprite.sortingOrder = renderer.sortingOrder;
                    child.GetComponent<FadeFollower>().SetSkeleton(skeleton, sprite);
                    skeleton.enabled = false;
                    renderer.enabled = false;
                    replaced++;
                }
                foreach (SpriteRenderer sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sprite.sprite != null && sprite.sprite.name == e.SourceSprite) sprite.sprite = e.PortraitSprite;
                instance.Logger.LogInfo("Hero presentation prepared: " + e.Key + "; full-character renderers: " + replaced);
            }
            catch (Exception ex) { instance.Logger.LogWarning("Some original hero presentation retained for " + e.Key + ": " + ex.Message); }
            return result;
        }

        private static void IncludeMesh(Mesh mesh, ref Bounds bounds, ref bool found)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            if (!found) { bounds = mesh.bounds; found = true; }
            else bounds.Encapsulate(mesh.bounds);
        }
        private static void FitContent(Entry e, Bounds bounds, out Vector2 size, out Vector2 offset)
        {
            float width = e.FullTexture.width, height = e.FullTexture.height;
            float scale = Mathf.Min(bounds.size.x / (width * e.FullContent.z), bounds.size.y / (height * e.FullContent.w));
            size = new Vector2(width * scale, height * scale);
            offset = new Vector2(bounds.center.x - (e.FullContent.x + e.FullContent.z * .5f - .5f) * size.x,
                bounds.center.y - (e.FullContent.y + e.FullContent.w * .5f - .5f) * size.y);
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            foreach (Entry e in entries)
            {
                if (e.PortraitSprite != null) Object.Destroy(e.PortraitSprite);
                if (e.FullSprite != null) Object.Destroy(e.FullSprite);
                if (e.PortraitTexture != null) Object.Destroy(e.PortraitTexture);
                if (e.FullTexture != null) Object.Destroy(e.FullTexture);
            }
            if (instance == this) instance = null;
        }
    }

    public sealed class FadeFollower : MonoBehaviour
    {
        private Graphic sourceGraphic, targetGraphic;
        private SkeletonRenderer sourceSkeleton;
        private SpriteRenderer targetSprite;
        public void SetGraphic(Graphic source, Graphic target) { sourceGraphic = source; targetGraphic = target; }
        public void SetSkeleton(SkeletonRenderer source, SpriteRenderer target) { sourceSkeleton = source; targetSprite = target; }
        private void LateUpdate()
        {
            if (targetGraphic != null && sourceGraphic != null) targetGraphic.color = sourceGraphic.color;
            if (targetSprite != null && sourceSkeleton != null && sourceSkeleton.Skeleton != null)
                targetSprite.color = new Color(1f, 1f, 1f, sourceSkeleton.Skeleton.A);
        }
    }
}
