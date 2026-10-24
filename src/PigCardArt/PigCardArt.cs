using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;

namespace PigCardArt
{
    // Built against the user's installed Windows PTR assemblies; Check.ps1 verifies hooks.
    // Hooks only presentation methods; the game's data files are never written.
    [BepInPlugin(PluginId, "大猪扎 · 卡牌", "1.0.0")]
    [BepInDependency("BazaarPlusPlus", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginId = "local.pigcardart";
        private static Plugin instance;
        private ConfigEntry<bool> enabledSetting;
        private bool suiteEnabled;
        private Harmony harmony;
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Replacement> replacements = new Dictionary<string, Replacement>();
        private static readonly AsyncLocal<string> logicalCardId = new AsyncLocal<string>();
        private static FieldInfo previewCardData;
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private sealed class Replacement
        {
            internal CardAssetDataSO Original;
            internal CardAssetDataSO Asset;
            internal Material Material;
        }

        private PigModSuite.SuiteSettings suite;
        private void Awake()
        {
            suite = PigModSuite.SuiteSettings.Load(BepInEx.Paths.ConfigPath, delegate(string message) { Logger.LogWarning(message); });
            instance = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Enable local card art. Restart the game after changing this setting or images.");
            suiteEnabled = suite.ModuleEnabled("cards", enabledSetting.Value);
            if (!suiteEnabled) return;
            try
            {
                string folder = Path.GetDirectoryName(Info.Location);
                LoadManifest(folder);
                if (files.Count == 0)
                {
                    Logger.LogWarning("No images available. Add PNG/JPG files listed in cards.json to art, then restart. Original artwork remains active.");
                    return;
                }
                Assembly gameAssembly = typeof(CardAssetDataSO).Assembly;
                MethodInfo board = FindPresentationMethod(gameAssembly, "TheBazaar.Game.CardFrames.ItemVisualsController", "Setup", 5);
                MethodInfo preview = FindPresentationMethod(gameAssembly, "TheBazaar.UI.CardPreviewItem", "UpdateCardImageMaterial", 3);
                previewCardData = AccessTools.Field(gameAssembly.GetType("TheBazaar.UI.CardPreviewBase", true), "_cardData");
                if (previewCardData == null || previewCardData.FieldType != typeof(TCardBase)) throw new MissingFieldException("CardPreviewBase", "_cardData");
                harmony = new Harmony(PluginId);
                HarmonyMethod prefix = new HarmonyMethod(typeof(Plugin).GetMethod("ReplaceArt", BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(board, prefix: prefix);
                harmony.Patch(preview, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("ReplacePreviewArt", BindingFlags.NonPublic | BindingFlags.Static)));
                PatchLogicalCardContexts(gameAssembly);
                PatchCollectionCache();
                Logger.LogInfo("Ready: " + files.Count + " redrawn cards. Each card retains its native enchantment material, masks, colours, animation and size handling. No gameplay data is changed.");
            }
            catch (Exception error)
            {
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Disabled after initialization error; original artwork remains active. " + error);
            }
        }

        private void PatchLogicalCardContexts(Assembly gameAssembly)
        {
            Type visuals = gameAssembly.GetType("TheBazaar.Game.CardFrames.ItemVisualsController", true);
            Type clientCard = Type.GetType("BazaarGameClient.Domain.Models.Cards.Card, BazaarGameClient", true);
            int count = 0;
            foreach (MethodInfo method in visuals.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] p = method.GetParameters();
                if (method.Name != "Setup" || method.ReturnType != typeof(Task) || p.Length == 0) continue;
                if (!(p.Length == 1 && p[0].ParameterType == typeof(ITCard)) && !(p.Length == 2 && p[0].ParameterType == clientCard)) continue;
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(Plugin).GetMethod("EnterCardContext", BindingFlags.NonPublic | BindingFlags.Static)),
                    finalizer: new HarmonyMethod(typeof(Plugin).GetMethod("LeaveCardContext", BindingFlags.NonPublic | BindingFlags.Static)));
                count++;
            }
            if (count != 2) throw new MissingMethodException("Expected both logical ItemVisualsController.Setup overloads.");
            MethodInfo construct = AccessTools.Method(gameAssembly.GetType("AssetLoader", true), "ConstructAndInstantiateCardVisuals", new Type[] { typeof(ITCard), typeof(GameObject), typeof(CardAssetDataSO), typeof(bool) });
            if (construct == null || construct.ReturnType != typeof(Task<GameObject>)) throw new MissingMethodException("AssetLoader", "ConstructAndInstantiateCardVisuals");
            harmony.Patch(construct, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("ReplaceConstructedArt", BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // AsyncLocal flows into each async Setup continuation, while the finalizer restores
        // its caller immediately. Concurrent card loads cannot overwrite each other's IDs.
        private static void EnterCardContext(object __0, out string __state)
        {
            __state = logicalCardId.Value;
            ITCard template = __0 as ITCard;
            if (template != null) logicalCardId.Value = template.Id.ToString("D");
            else
            {
                PropertyInfo property = __0 == null ? null : __0.GetType().GetProperty("TemplateId");
                object id = property == null ? null : property.GetValue(__0, null);
                logicalCardId.Value = id is Guid ? ((Guid)id).ToString("D") : null;
            }
        }

        private static void LeaveCardContext(string __state) { logicalCardId.Value = __state; }

        private static void ReplaceConstructedArt(ITCard __0, ref CardAssetDataSO __2)
        {
            ReplaceWithId(ref __2, __0 == null ? null : __0.Id.ToString("D"));
        }

        private static string PreviewId(object preview)
        {
            ITCard data = preview == null || previewCardData == null ? null : previewCardData.GetValue(preview) as ITCard;
            return data == null ? null : data.Id.ToString("D");
        }

        private static void ReplacePreviewArt(object __instance, ref CardAssetDataSO __0)
        {
            ReplaceWithId(ref __0, PreviewId(__instance));
        }

        private void PatchCollectionCache()
        {
            // BazaarPlusPlus 5.7 uses its own material cache in the encyclopedia.
            // It is optional; failure here must not disable the native display hooks.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "BazaarPlusPlus") continue;
                try
                {
                    Type type = assembly.GetType("BazaarPlusPlus.Patches.CollectionPanel.CollectionItemLoadArtPatch", true);
                    MethodInfo method = type.GetMethod("Prefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (method == null || method.ReturnType != typeof(bool) || method.GetParameters().Length != 3) throw new MissingMethodException(type.FullName, "Prefix");
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("UseNativeCollectionArt", BindingFlags.NonPublic | BindingFlags.Static)));
                    Logger.LogInfo("BazaarPlusPlus: mapped cards use native preview loading to preserve per-card identity.");
                }
                catch (Exception error) { Logger.LogWarning("Encyclopedia compatibility unavailable: " + error.Message); }
                break;
            }
        }

        private static bool UseNativeCollectionArt(object __0, ref bool __result)
        {
            Plugin current = instance;
            string id = PreviewId(__0);
            if (current == null || !current.suiteEnabled || id == null || !current.files.ContainsKey(id)) return true;
            // BPP's cache is keyed by shared ArtKey, not logical card ID. Returning true
            // from its prefix lets the game's normal loader reach ReplacePreviewArt.
            __result = true;
            return false;
        }

        private static MethodInfo FindPresentationMethod(Assembly assembly, string typeName, string methodName, int count)
        {
            Type type = assembly.GetType(typeName, true);
            MethodInfo found = null;
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != methodName || method.ReturnType != typeof(Task) || parameters.Length != count || parameters[0].ParameterType != typeof(CardAssetDataSO)) continue;
                if (found != null) throw new InvalidOperationException("Ambiguous presentation method: " + typeName + "." + methodName);
                found = method;
            }
            if (found == null) throw new MissingMethodException(typeName, methodName);
            return found;
        }

        private void LoadManifest(string folder)
        {
            string manifestPath = Path.Combine(folder, "cards.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("cards.json must be next to PigCardArt.dll", manifestPath);
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if ((int?)manifest["schemaVersion"] != 1) throw new InvalidDataException("Unsupported cards.json schemaVersion.");
            JArray entries = manifest["cards"] as JArray;
            if (entries == null) throw new InvalidDataException("cards must be an array.");
            string artFolder = Path.Combine(folder, "art");
            foreach (JToken entry in entries)
            {
                if ((bool?)entry["enabled"] == false || !suite.ItemEnabled("cards", (string)entry["cardId"])) continue;
                Guid id;
                string key = (string)entry["cardId"];
                string fileName = (string)entry["file"];
                if (!Guid.TryParse(key, out id) || id == Guid.Empty || string.IsNullOrEmpty(fileName))
                    throw new InvalidDataException("Each enabled entry requires a cardId and image file name.");
                // Only direct files inside the art directory are allowed.
                if (fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("Use an image file name without directory components: " + fileName);
                string extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                    throw new InvalidDataException("Only PNG and JPG images are supported: " + fileName);
                string path = Path.Combine(artFolder, fileName);
                if (!File.Exists(path)) { Logger.LogWarning("Missing image: " + fileName); continue; }
                string canonicalId = id.ToString("D");
                if (files.ContainsKey(canonicalId)) throw new InvalidDataException("Duplicate cardId: " + canonicalId);
                files.Add(canonicalId, path);
            }
        }

        // Positional Harmony argument works for both independently named asset parameters.
        // Swap a cloned ScriptableObject so shared game assets remain untouched.
        private static void ReplaceArt(ref CardAssetDataSO __0)
        {
            ReplaceWithId(ref __0, logicalCardId.Value);
        }

        private static void ReplaceWithId(ref CardAssetDataSO __0, string cardId)
        {
            Plugin current = instance;
            if (current == null || current.enabledSetting == null || !current.suiteEnabled || __0 == null) return;
            CardAssetDataSO original = __0;
            try { __0 = current.GetReplacement(original, cardId); }
            catch (Exception error)
            {
                __0 = original;
                string key = cardId ?? original.cardGUID ?? original.name;
                if (current.failed.Add(key)) current.Logger.LogError("Using original art for " + key + ": " + error.Message);
            }
        }

        private CardAssetDataSO GetReplacement(CardAssetDataSO original, string cardId)
        {
            if (original.name.StartsWith("PigCardArt/", StringComparison.Ordinal)) return original;
            Guid id;
            if (!Guid.TryParse(cardId ?? original.cardGUID, out id)) return original;
            string key = id.ToString("D");
            string path;
            if (!files.TryGetValue(key, out path) || failed.Contains(key)) return original;
            string instanceId = key + "/" + original.GetInstanceID();
            Replacement cached;
            if (replacements.TryGetValue(instanceId, out cached) && cached.Original == original && cached.Asset != null) return cached.Asset;
            Material originalMaterial = original.cardMaterial;
            if (originalMaterial == null || !originalMaterial.HasProperty("_MainTex"))
                throw new InvalidOperationException("The current card shader has no _MainTex; this game version needs adaptation.");
            Texture2D texture;
            if (!textures.TryGetValue(path, out texture))
            {
                FileInfo file = new FileInfo(path);
                if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("Image exceeds 16 MiB: " + file.Name);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true)) throw new InvalidDataException("Unable to decode " + file.Name);
                    if (texture.width > 4096 || texture.height > 4096) throw new InvalidDataException("Image dimensions exceed 4096 pixels: " + file.Name);
                    texture.name = "PigCardArt/" + file.Name;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    textures.Add(path, texture);
                }
                catch { UnityEngine.Object.Destroy(texture); throw; }
            }
            Material material = null;
            CardAssetDataSO asset = null;
            try
            {
                material = NativeArtMaterial.Create(originalMaterial, texture);
                asset = UnityEngine.Object.Instantiate(original);
                asset.name = "PigCardArt/" + original.name;
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.cardMaterial = material;
                asset.cardGUID = key;
                if (cached != null) { UnityEngine.Object.Destroy(cached.Asset); UnityEngine.Object.Destroy(cached.Material); }
                replacements[instanceId] = new Replacement { Original = original, Asset = asset, Material = material };
                Logger.LogInfo("Art prepared: " + key + " -> " + Path.GetFileName(path));
                return asset;
            }
            catch
            {
                if (asset != null) UnityEngine.Object.Destroy(asset);
                if (material != null) UnityEngine.Object.Destroy(material);
                throw;
            }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            foreach (Replacement replacement in replacements.Values)
            {
                if (replacement.Asset != null) UnityEngine.Object.Destroy(replacement.Asset);
                if (replacement.Material != null) UnityEngine.Object.Destroy(replacement.Material);
            }
            foreach (Texture2D texture in textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            replacements.Clear();
            textures.Clear();
            if (instance == this) instance = null;
        }
    }
}
