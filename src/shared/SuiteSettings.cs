using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace PigModSuite
{
    // One immutable snapshot per plugin at startup. A corrupt shared file fails
    // closed so an unreadable user opt-out can never silently enable artwork.
    internal sealed class SuiteSettings
    {
        private JObject settings;
        private bool invalid;
        internal static SuiteSettings Load(string configDirectory, Action<string> warn)
        {
            SuiteSettings result = new SuiteSettings();
            string file = Path.Combine(configDirectory, "PigModSuite", "settings.json");
            if (!File.Exists(file)) return result;
            try
            {
                if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Settings exceed 2 MiB.");
                JObject data = JObject.Parse(File.ReadAllText(file));
                if ((int?)data["schemaVersion"] != 1) throw new InvalidDataException("Unsupported settings schema.");
                JObject modules = data["modules"] as JObject, items = data["items"] as JObject;
                if (modules == null || items == null) throw new InvalidDataException("Missing modules/items.");
                foreach (string name in new[] { "cards", "heroes", "events", "monsters", "storage", "mainmenu" })
                    if (modules[name] == null || modules[name].Type != JTokenType.Boolean) throw new InvalidDataException("Invalid module: " + name);
                foreach (JProperty item in items.Properties())
                    if (item.Value.Type != JTokenType.Boolean) throw new InvalidDataException("Invalid item: " + item.Name);
                result.settings = data;
            }
            catch (Exception ex)
            {
                result.invalid = true;
                warn("Pig Mod Suite settings unreadable; original artwork retained. " + file + ": " + ex.Message);
            }
            return result;
        }
        internal bool ModuleEnabled(string module, bool legacyValue)
        {
            if (invalid) return false;
            return settings == null ? legacyValue : (bool)settings["modules"][module];
        }
        internal bool ItemEnabled(string module, string id)
        {
            if (invalid || string.IsNullOrEmpty(id)) return false;
            if (settings == null) return true;
            JToken value = settings["items"][module + ":" + id];
            return value == null || (bool)value;
        }
        internal static string MenuGroup(string name)
        {
            if (name == null) return "";
            if (name.StartsWith("MainMenu_Anim_BG_SM_Blue_", StringComparison.Ordinal)) return "blue-merchant";
            if (name.StartsWith("MainMenu_Anim_BG_SM_Green_", StringComparison.Ordinal)) return "green-musician";
            return name;
        }
    }
}
