using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PigEncounterArt
{
    [BepInPlugin(Id, "大猪扎 · 事件与野怪", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.pigencounterart";
        private static Plugin instance;
        private ConfigEntry<bool> enabledSetting;
        private bool suiteEnabled;
        private Harmony harmony;
        private readonly Dictionary<string, List<Entry>> names = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Entry>> keys = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, Original> originals = new Dictionary<string, Original>();
        private readonly Dictionary<int, PreviewPlacement> previewPlacements = new Dictionary<int, PreviewPlacement>();
        private static readonly FieldInfo CardField = AccessTools.Field(typeof(CardPreviewBase), "_cardData");
        private static readonly FieldInfo BackgroundField = AccessTools.Field(typeof(CardPreviewBase), "_cardImage");
        private static readonly FieldInfo PortraitField = AccessTools.Field(typeof(CardPreviewEncounter), "_portraitImage");
        private static readonly FieldInfo HasPortraitField = AccessTools.Field(typeof(CardPreviewEncounter), "_hasPortrait");
        private static readonly MethodInfo ArtKeyGetter = AccessTools.Method(typeof(TCardBase), "get_ArtKey");
        private sealed class Entry
        {
            internal string Name, File, Hash, SourceTexture, SourceSprite;
            internal bool Background, Failed;
            internal float Ppu;
            internal Vector2 Pivot;
            internal float PreviewScale;
            internal Vector2 PreviewOrigin;
            internal Texture2D Texture;
            internal Sprite Sprite;
        }
        private sealed class Original
        {
            internal EncounterAssetDataSO Data;
            internal Sprite Portrait;
            internal Texture2D Background;
            internal Entry Entry;
        }
        private sealed class PreviewPlacement
        {
            internal RectTransform Transform;
            internal Vector3 Position, Scale;
            internal bool Applied;
            internal void Restore()
            {
                if (!Applied || Transform == null) return;
                Transform.localPosition = Position;
                Transform.localScale = Scale;
                Applied = false;
            }
        }
        private PigModSuite.SuiteSettings suite;
        private void Awake()
        {
            suite = PigModSuite.SuiteSettings.Load(BepInEx.Paths.ConfigPath, delegate(string message) { Logger.LogWarning(message); });
            instance = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Local event and monster artwork; restart after changing.");
            suiteEnabled = (suite.ModuleEnabled("events", enabledSetting.Value) || suite.ModuleEnabled("monsters", enabledSetting.Value));
            if (!suiteEnabled) return;
            try
            {
                LoadManifest();
                if (CardField == null || BackgroundField == null || PortraitField == null || HasPortraitField == null || ArtKeyGetter == null)
                    throw new MissingFieldException("Encounter preview layout changed.");
                harmony = new Harmony(Id);
                Patch(typeof(EncounterController), "SetupEncounterController", new Type[] { typeof(EncounterAssetDataSO) }, "SetupPrefix", null);
                Patch(typeof(EncounterAssetDataSO), "LoadPortraitSpriteAsync", new Type[] { typeof(CancellationToken) }, "PortraitPrefix", null);
                Patch(typeof(CardPreviewEncounter), "LoadArt", new Type[] { typeof(bool), typeof(CancellationToken) }, "PreviewPrefix", "PreviewPostfix");
                Logger.LogInfo("Ready: " + entries.Count + " distinct event/monster artworks; exact asset and ArtKey matches.");
            }
            catch (Exception ex)
            {
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Encounter artwork disabled; original presentation retained. " + ex);
            }
        }
        private void Patch(Type type, string name, Type[] args, string prefix, string postfix)
        {
            MethodInfo target = AccessTools.Method(type, name, args);
            if (target == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
        }
        private void LoadManifest()
        {
            string folder = Path.GetDirectoryName(Info.Location);
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "encounters.json")));
            if ((int?)manifest["schemaVersion"] != 1) throw new InvalidDataException("Unsupported manifest.");
            foreach (JObject row in (JArray)manifest["artworks"])
            {
                string module = (string)row["category"] == "monster" ? "monsters" : "events";
                if (!suite.ModuleEnabled(module, enabledSetting.Value) || !suite.ItemEnabled(module, (string)row["ownerAssetName"] ?? (string)row["assetName"])) continue;
                string file = (string)row["file"];
                if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid local image path.");
                Entry e = new Entry();
                e.Name = (string)row["assetName"];
                e.File = Path.Combine(Path.Combine(folder, "art"), file);
                e.Hash = (string)row["sha256"];
                e.SourceTexture = (string)row["sourceTexture"];
                e.SourceSprite = (string)row["sourceSprite"];
                e.Background = (string)row["role"] == "background";
                e.Ppu = (float?)row["pixelsPerUnit"] ?? 256f;
                e.Pivot = row["pivot"] is JArray ? new Vector2((float)row["pivot"][0], (float)row["pivot"][1]) : new Vector2(.5f, .5f);
                e.PreviewScale = (float?)row["previewScale"] ?? 1f;
                e.PreviewOrigin = row["previewOrigin"] is JArray ? new Vector2((float)row["previewOrigin"][0], (float)row["previewOrigin"][1]) : Vector2.zero;
                if (string.IsNullOrEmpty(e.Name) || string.IsNullOrEmpty(e.SourceTexture) || e.Ppu <= 0f)
                    throw new InvalidDataException("Missing artwork identity or geometry.");
                AddLayer(names, (string)row["ownerAssetName"] ?? e.Name, e);
                foreach (JToken key in (JArray)row["artKeys"]) AddLayer(keys, (string)key, e);
                entries.Add(e);
            }
        }
        private static void AddLayer(Dictionary<string, List<Entry>> map, string key, Entry entry)
        {
            List<Entry> layers;
            if (!map.TryGetValue(key, out layers)) { layers = new List<Entry>(); map.Add(key, layers); }
            foreach (Entry other in layers)
                if (other.Background == entry.Background) throw new InvalidDataException("Duplicate artwork layer: " + key);
            layers.Add(entry);
        }
        private bool Ensure(Entry e)
        {
            if (e.Failed) return false;
            if (e.Texture != null) return true;
            Texture2D texture = null;
            try
            {
                FileInfo file = new FileInfo(e.File);
                if (!file.Exists || file.Length > 16 * 1024 * 1024) throw new InvalidDataException("Missing or oversized PNG.");
                byte[] bytes = File.ReadAllBytes(e.File);
                using (SHA256 hash = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), e.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PNG hash mismatch.");
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width > 4096 || texture.height > 4096)
                    throw new InvalidDataException("Invalid PNG dimensions.");
                texture.name = "PigEncounterArt/" + e.Name;
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                if (!e.Background)
                {
                    e.Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), e.Pivot, e.Ppu, 0, SpriteMeshType.FullRect);
                    e.Sprite.name = texture.name;
                    e.Sprite.hideFlags = HideFlags.HideAndDontSave;
                }
                e.Texture = texture;
                return true;
            }
            catch (Exception ex)
            {
                if (texture != null) Object.Destroy(texture);
                e.Failed = true;
                Logger.LogWarning("Original artwork retained for " + e.Name + ": " + ex.Message);
                return false;
            }
        }
        private void Apply(EncounterAssetDataSO data)
        {
            if (data == null || !suiteEnabled) return;
            List<Entry> layers;
            if (!names.TryGetValue(data.name, out layers)) return;
            foreach (Entry e in layers) ApplyLayer(data, e);
        }
        private void ApplyLayer(EncounterAssetDataSO data, Entry e)
        {
            Original original;
            string id = data.GetInstanceID().ToString() + (e.Background ? "/background" : "/portrait");
            if (originals.TryGetValue(id, out original) && original.Data == data) return;
            if (e.Background)
            {
                if (data.backgroundTextureReference == null || data.backgroundTextureReference.name != e.SourceTexture) return;
            }
            else if (data.portraitTextureReference == null || data.portraitTextureReference.name != e.SourceSprite) return;
            if (!Ensure(e)) return;
            original = new Original { Data = data, Portrait = data.portraitTextureReference, Background = data.backgroundTextureReference, Entry = e };
            originals[id] = original;
            // Only these presentation references change. Keep encounters, rewards,
            // difficulty, background placement, click handlers and asset ownership intact.
            if (e.Background) data.backgroundTextureReference = e.Texture;
            else data.portraitTextureReference = e.Sprite;
        }
        private static void SetupPrefix(EncounterAssetDataSO __0)
        {
            if (instance != null) instance.Apply(__0);
        }
        private static void PortraitPrefix(EncounterAssetDataSO __instance)
        {
            if (instance != null) instance.Apply(__instance);
        }
        private static string PreviewKey(CardPreviewEncounter preview)
        {
            TCardBase card = CardField.GetValue(preview) as TCardBase;
            return card == null ? null : (string)ArtKeyGetter.Invoke(card, null);
        }
        private static void PreviewPrefix(CardPreviewEncounter __instance, out string __state)
        {
            if (instance != null) instance.RestorePreview(__instance);
            __state = instance == null ? null : PreviewKey(__instance);
        }
        private void RestorePreview(CardPreviewEncounter preview)
        {
            Image portrait = PortraitField.GetValue(preview) as Image;
            PreviewPlacement placement;
            if (portrait != null && previewPlacements.TryGetValue(portrait.GetInstanceID(), out placement)) placement.Restore();
        }
        private void PlacePreview(Image portrait, Entry e)
        {
            RectTransform rect = portrait.rectTransform;
            int id = portrait.GetInstanceID();
            PreviewPlacement placement;
            if (!previewPlacements.TryGetValue(id, out placement) || placement.Transform != rect)
            {
                placement = new PreviewPlacement { Transform = rect };
                previewPlacements[id] = placement;
            }
            placement.Restore();
            placement.Position = rect.localPosition;
            placement.Scale = rect.localScale;
            Vector2 origin = e.PreviewOrigin + (e.PreviewScale - 1f) * rect.pivot;
            Vector3 offset = new Vector3(origin.x * rect.rect.width, origin.y * rect.rect.height, 0f);
            rect.localPosition = placement.Position + rect.localRotation * Vector3.Scale(offset, placement.Scale);
            rect.localScale = Vector3.Scale(placement.Scale, new Vector3(e.PreviewScale, e.PreviewScale, 1f));
            placement.Applied = true;
        }
        private static void PreviewPostfix(CardPreviewEncounter __instance, CancellationToken __1, string __state, ref Task __result)
        {
            List<Entry> layers;
            if (instance != null && instance.suiteEnabled && __state != null && instance.keys.TryGetValue(__state, out layers))
                foreach (Entry e in layers) __result = UpdatePreview(__result, __instance, __state, __1, e);
        }
        private static async Task UpdatePreview(Task original, CardPreviewEncounter preview, string key, CancellationToken token, Entry e)
        {
            await original;
            if (instance == null || preview == null || token.IsCancellationRequested || PreviewKey(preview) != key) return;
            try
            {
                if (!instance.Ensure(e)) return;
                if (e.Background)
                {
                    RawImage background = BackgroundField.GetValue(preview) as RawImage;
                    if (background != null) background.texture = e.Texture;
                }
                else
                {
                    Image portrait = PortraitField.GetValue(preview) as Image;
                    if (portrait != null)
                    {
                        portrait.sprite = e.Sprite;
                        instance.PlacePreview(portrait, e);
                        portrait.enabled = true;
                        HasPortraitField.SetValue(preview, true);
                    }
                }
            }
            catch (Exception ex) { instance.Logger.LogWarning("Encounter preview unchanged: " + ex.Message); }
        }
        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            foreach (PreviewPlacement placement in previewPlacements.Values) placement.Restore();
            foreach (Original o in originals.Values)
            {
                if (o.Data == null) continue;
                if (o.Data.portraitTextureReference == o.Entry.Sprite && !o.Entry.Background) o.Data.portraitTextureReference = o.Portrait;
                if (o.Data.backgroundTextureReference == o.Entry.Texture && o.Entry.Background) o.Data.backgroundTextureReference = o.Background;
            }
            foreach (Entry e in entries)
            {
                if (e.Sprite != null) Object.Destroy(e.Sprite);
                if (e.Texture != null) Object.Destroy(e.Texture);
            }
            if (instance == this) instance = null;
        }
    }
}
