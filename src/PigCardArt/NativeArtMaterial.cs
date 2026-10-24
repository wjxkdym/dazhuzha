using System;
using UnityEngine;

namespace PigCardArt
{
    internal static class NativeArtMaterial
    {
        // Copy the complete per-card material, including every native mask,
        // keyword, UV transform, premium layer, colour and animation parameter.
        // Live enchantment application/removal remains entirely with the game.
        internal static Material Create(Material original, Texture replacement)
        {
            if (original == null || !original.HasProperty("_MainTex"))
                throw new ArgumentException("Original card material has no _MainTex", "original");
            if (replacement == null) throw new ArgumentNullException("replacement");
            Material copy = new Material(original);
            try
            {
                copy.name = "PigCardArt/" + original.name;
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.SetTexture("_MainTex", replacement);
                return copy;
            }
            catch { UnityEngine.Object.Destroy(copy); throw; }
        }
    }
}
