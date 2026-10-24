using System;
using System.Collections.Generic;
namespace PigPortraitArt
{
    // Modern manifests use native ScriptableObject names, because two recolors
    // can share InternalName. InternalName fallback is only for legacy rows.
    internal sealed class HeroSkinIndex<T> where T : class
    {
        private readonly Dictionary<string, T> assets = new Dictionary<string, T>(StringComparer.Ordinal);
        private readonly Dictionary<string, T> legacy = new Dictionary<string, T>(StringComparer.Ordinal);
        internal void Add(string assetName, string internalName, T entry)
        {
            if (!string.IsNullOrEmpty(assetName)) assets.Add(assetName, entry);
            else if (!string.IsNullOrEmpty(internalName)) legacy.Add(internalName, entry);
            else throw new ArgumentException("Skin identity is required.");
        }
        internal bool TryGet(string assetName, string internalName, out T entry)
        {
            string name = assetName ?? "";
            if (assets.TryGetValue(name, out entry)) return entry != null;
            const string clone = "(Clone)";
            if (name.EndsWith(clone, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - clone.Length).TrimEnd();
                if (assets.TryGetValue(name, out entry)) return entry != null;
            }
            return legacy.TryGetValue(internalName ?? "", out entry) && entry != null;
        }
    }
}
