using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TheBazaar;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace PigStorageArt
{
    [BepInPlugin(Id, "大猪扎 · 储存箱", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.pigstorageart";
        private const string SourceName = "ENC_Event_TreasureChest_Char";
        private static Plugin instance;
        private static readonly FieldInfo PortraitField = AccessTools.Field(typeof(StoragePortraitController), "portraitRenderer");
        private readonly Dictionary<int, Sprite> replacements = new Dictionary<int, Sprite>();
        private readonly List<StoragePortraitBinding> bindings = new List<StoragePortraitBinding>();
        private Harmony harmony;
        private Texture2D texture;

        private PigModSuite.SuiteSettings suite;
        private void Awake()
        {
            suite = PigModSuite.SuiteSettings.Load(BepInEx.Paths.ConfigPath, delegate(string message) { Logger.LogWarning(message); });
            if (!suite.ModuleEnabled("storage", Config.Bind("General", "Enabled", true, "Replace the default storage chest portrait. Restart after changes.").Value)) return;
            try
            {
                if (!suite.ItemEnabled("storage", "chest")) return;
                string folder = Path.GetDirectoryName(Info.Location);
                JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "storage.json")));
                string file = (string)manifest["file"];
                if ((int?)manifest["schemaVersion"] != 1 || (string)manifest["sourceSprite"] != SourceName || string.IsNullOrEmpty(file) || Path.GetFileName(file) != file || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid storage portrait manifest");
                string path = Path.Combine(folder, file);
                if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("PNG exceeds 16 MiB");
                byte[] bytes = File.ReadAllBytes(path);
                using (SHA256 hash = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), (string)manifest["sha256"], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Storage PNG hash mismatch");
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width > 4096 || texture.width != texture.height)
                    throw new InvalidDataException("Invalid storage PNG dimensions");
                texture.name = "PigStorageArt/Chest";
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                MethodInfo setup = AccessTools.Method(typeof(StoragePortraitController), "Setup", new Type[] { typeof(AssetReferenceSprite), typeof(AssetReferenceTexture2D) });
                if (setup == null || setup.ReturnType != typeof(void) || PortraitField == null || PortraitField.FieldType != typeof(SpriteRenderer))
                    throw new MissingMemberException("Storage portrait interface changed");
                instance = this;
                harmony = new Harmony(Id);
                harmony.Patch(setup, postfix: new HarmonyMethod(typeof(Plugin), "SetupPostfix"));
                Logger.LogInfo("Ready: default storage chest portrait; native background, frame, position and interactions retained.");
            }
            catch (Exception ex)
            {
                if (harmony != null) harmony.UnpatchSelf();
                if (texture != null) Object.Destroy(texture);
                texture = null;
                if (instance == this) instance = null;
                Logger.LogError("Original storage chest retained: " + ex);
            }
        }

        private static void SetupPostfix(StoragePortraitController __instance)
        {
            Plugin current = instance;
            if (current == null || __instance == null) return;
            try
            {
                SpriteRenderer renderer = PortraitField.GetValue(__instance) as SpriteRenderer;
                if (renderer == null) return;
                StoragePortraitBinding binding = __instance.GetComponent<StoragePortraitBinding>();
                if (binding == null)
                {
                    binding = __instance.gameObject.AddComponent<StoragePortraitBinding>();
                    current.bindings.Add(binding);
                }
                binding.Bind(current, renderer);
            }
            catch (Exception ex) { current.Logger.LogWarning("Storage portrait unchanged: " + ex.Message); }
        }

        internal Sprite Resolve(Sprite original)
        {
            if (texture == null || original == null || original.name != SourceName) return null;
            int id = original.GetInstanceID();
            Sprite result;
            if (replacements.TryGetValue(id, out result)) return result;
            Rect rect = original.rect;
            if (rect.width <= 0 || rect.height <= 0 || original.pixelsPerUnit <= 0) return null;
            if (Math.Abs(rect.width / rect.height - texture.width / (float)texture.height) > .005f) return null;
            Vector2 pivot = new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height);
            float ppu = original.pixelsPerUnit * texture.width / rect.width;
            result = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            result.name = "PigStorageArt/" + SourceName;
            result.hideFlags = HideFlags.HideAndDontSave;
            replacements.Add(id, result);
            Logger.LogInfo("Storage chest portrait prepared; native sprite geometry retained.");
            return result;
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            foreach (StoragePortraitBinding binding in bindings)
                if (binding != null) { binding.Restore(); Object.Destroy(binding); }
            foreach (Sprite sprite in replacements.Values) if (sprite != null) Object.Destroy(sprite);
            replacements.Clear();
            if (texture != null) Object.Destroy(texture);
            if (instance == this) instance = null;
        }
    }

    // Setup is async void. Its postfix runs before addressable loading finishes.
    // Observe only this controller's renderer so the replacement is applied after
    // the game assigns the sprite, including later resets. Never replace its logic.
    public sealed class StoragePortraitBinding : MonoBehaviour
    {
        private Plugin owner;
        private SpriteRenderer target;
        private Sprite original, replacement;
        internal void Bind(Plugin plugin, SpriteRenderer renderer)
        { owner = plugin; target = renderer; Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (owner == null || target == null || target.sprite == null || target.sprite == replacement) return;
            Sprite next = owner.Resolve(target.sprite);
            if (next == null) return;
            original = target.sprite;
            replacement = next;
            target.sprite = replacement;
        }
        internal void Restore()
        {
            if (target != null && target.sprite == replacement) target.sprite = original;
            owner = null;
        }
    }
}
