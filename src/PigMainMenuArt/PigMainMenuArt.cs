using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PigMainMenuArt
{
    [BepInPlugin("local.pigmainmenuart", "大猪扎 · 主界面", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
        private readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        private readonly List<Binding> bindings = new List<Binding>();
        private float nextScan;
        private bool ready;
        private sealed class Layer { internal JObject data; internal Texture2D texture; }

        private PigModSuite.SuiteSettings suite;
        private void Awake()
        {
            suite = PigModSuite.SuiteSettings.Load(BepInEx.Paths.ConfigPath, delegate(string message) { Logger.LogWarning(message); });
            if (!suite.ModuleEnabled("mainmenu", Config.Bind("General", "Enabled", true, "Pigify selected original city inhabitants and decorations. Restart after changes.").Value)) return;
            try
            {
                string folder = Path.GetDirectoryName(Info.Location);
                JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "layers.json")));
                if ((int?)manifest["schemaVersion"] != 1) throw new InvalidDataException("Unknown layer schema");
                foreach (JObject data in (JArray)manifest["layers"])
                {
                    string name = (string)data["name"], file = (string)data["file"];
                    if (!suite.ItemEnabled("mainmenu", PigModSuite.SuiteSettings.MenuGroup(name))) continue;
                    if (name == null || !name.StartsWith("MainMenu_", StringComparison.Ordinal) || string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid layer path/name");
                    string path = Path.Combine(folder, "art", file);
                    if (new FileInfo(path).Length > 24 * 1024 * 1024) throw new InvalidDataException("Oversized PNG");
                    byte[] bytes = File.ReadAllBytes(path);
                    using (SHA256 hash = SHA256.Create())
                        if (!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), (string)data["sha256"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PNG hash mismatch: " + name);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width > 8192 || texture.height > 8192) { Object.Destroy(texture); throw new InvalidDataException("Invalid PNG: " + name); }
                    texture.name = "PigMainMenuArt/" + name;
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    layers.Add(name, new Layer { data = data, texture = texture });
                }
                ready = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
                Logger.LogInfo("Ready: " + layers.Count + " city layers. Original city, UI, transforms, materials and animation controllers retained.");
            }
            catch (Exception ex) { ready = false; Logger.LogError("Original main menu retained: " + ex); Release(); }
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { nextScan = 0; }
        private void Update()
        {
            if (!ready || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 3f;
            bindings.RemoveAll(delegate(Binding b) { return b == null; });
            foreach (SpriteRenderer target in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (target == null || !target.gameObject.scene.IsValid() || target.sprite == null || !layers.ContainsKey(target.sprite.name) || target.GetComponent<Binding>() != null) continue;
                Binding binding = target.gameObject.AddComponent<Binding>();
                binding.Bind(this, target);
                bindings.Add(binding);
            }
        }

        internal Sprite Resolve(Sprite original)
        {
            if (original == null) return null;
            Layer layer;
            if (!layers.TryGetValue(original.name, out layer)) return null;
            Sprite result;
            int id = original.GetInstanceID();
            if (cache.TryGetValue(id, out result)) return result;
            result = null;
            try
            {
                JObject data = layer.data;
                JArray expected = (JArray)data["vertices"], coordinates = (JArray)data["uv"];
                Vector2[] positions = original.vertices;
                SpriteBone[] bones = original.GetBones();
                if (positions.Length != expected.Count || coordinates.Count != expected.Count || bones.Length != (int)data["bones"] || original.texture.name != (string)data["textureName"]) throw new InvalidDataException("Source geometry/version differs");
                for (int i = 0; i < positions.Length; i++)
                    if (Math.Abs(positions[i].x - (float)expected[i][0]) > .0001f || Math.Abs(positions[i].y - (float)expected[i][1]) > .0001f) throw new InvalidDataException("Source vertices changed");
                ushort[] indices = original.triangles;
                JArray triangles = (JArray)data["triangles"];
                if (indices.Length != triangles.Count * 3) throw new InvalidDataException("Source topology changed");
                for (int i = 0; i < indices.Length; i++) if (indices[i] != (ushort)triangles[i / 3][i % 3]) throw new InvalidDataException("Source indices changed");
                Rect rect = original.rect;
                result = Sprite.Create(layer.texture, new Rect(0, 0, layer.texture.width, layer.texture.height), new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height), original.pixelsPerUnit * layer.texture.width / rect.width, 0, SpriteMeshType.FullRect);
                result.name = "PigMainMenuArt/" + original.name;
                result.hideFlags = HideFlags.HideAndDontSave;
                // Preserve the native mesh, including animation joint positions. A quad
                // replacement would break the SpriteSkin animation of these layers.
                result.OverrideGeometry(positions, indices);
                CopyChannel<Vector3>(original, result, VertexAttribute.Position);
                CopyChannel<Vector3>(original, result, VertexAttribute.Normal);
                CopyChannel<Vector4>(original, result, VertexAttribute.Tangent);
                CopyChannel<Color32>(original, result, VertexAttribute.Color);
                for (int channel = (int)VertexAttribute.TexCoord1; channel <= (int)VertexAttribute.TexCoord7; channel++) CopyChannel<Vector2>(original, result, (VertexAttribute)channel);
                NativeArray<Vector2> uv = new NativeArray<Vector2>(positions.Length, Allocator.Temp);
                try
                {
                    for (int i = 0; i < positions.Length; i++) uv[i] = new Vector2((float)coordinates[i][0], (float)coordinates[i][1]);
                    result.SetVertexAttribute(VertexAttribute.TexCoord0, uv);
                }
                finally { uv.Dispose(); }
                if (bones.Length > 0)
                {
                    result.SetBones(bones);
                    // Borrowed arrays returned by Unity belong to the original Sprite.
                    // SetBindPoses copies them; do not dispose the borrowed array.
                    result.SetBindPoses(original.GetBindPoses());
                    CopyChannel<BoneWeight>(original, result, VertexAttribute.BlendWeight);
                    if (result.GetBones().Length != bones.Length || result.GetBindPoses().Length != original.GetBindPoses().Length || !result.HasVertexAttribute(VertexAttribute.BlendWeight)) throw new InvalidDataException("Incomplete copied skin data");
                }
                Logger.LogInfo("Prepared " + original.name + ": " + positions.Length + " original vertices, " + bones.Length + " original bones.");
            }
            catch (Exception ex)
            {
                if (result != null) Object.Destroy(result);
                result = null;
                Logger.LogWarning("Keeping original " + original.name + ": " + ex.Message);
            }
            cache.Add(id, result);
            return result;
        }
        private static void CopyChannel<T>(Sprite from, Sprite to, VertexAttribute channel) where T : struct
        {
            if (!from.HasVertexAttribute(channel)) return;
            NativeSlice<T> source = from.GetVertexAttribute<T>(channel);
            NativeArray<T> copy = new NativeArray<T>(source.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < source.Length; i++) copy[i] = source[i];
                to.SetVertexAttribute(channel, copy);
            }
            finally { copy.Dispose(); }
        }
        private void OnDestroy()
        {
            ready = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            foreach (Binding binding in bindings) if (binding != null) { binding.Restore(); Object.Destroy(binding); }
            Release();
        }
        private void Release()
        {
            foreach (Sprite sprite in cache.Values) if (sprite != null) Object.Destroy(sprite);
            cache.Clear();
            foreach (Layer layer in layers.Values) if (layer.texture != null) Object.Destroy(layer.texture);
            layers.Clear();
        }
    }

    public sealed class Binding : MonoBehaviour
    {
        private Plugin owner;
        private SpriteRenderer target;
        private Sprite original, replacement;
        internal void Bind(Plugin plugin, SpriteRenderer renderer) { owner = plugin; target = renderer; Refresh(); }
        private void Update() { Refresh(); }
        private void Refresh()
        {
            if (owner == null || target == null || target.sprite == null || target.sprite == replacement) return;
            Sprite next = owner.Resolve(target.sprite);
            if (next == null) return;
            original = target.sprite; replacement = next;
            target.sprite = next;
        }
        internal void Restore()
        {
            if (target != null && target.sprite == replacement) target.sprite = original;
            owner = null;
        }
    }
}
