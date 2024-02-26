using BepInEx;
using BepInEx.Unity.IL2CPP;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using VRoid.UI.Messages;
using System.Text;
using HarmonyLib;
using System;
using Newtonsoft.Json;

namespace vroid_i18n
{
    class PluginMeta
    {
        public const string PLUGIN_GUID = "VRoid.i18n";
        public const string PLUGIN_NAME = "VRoid Studio i18n plugin";
        public const string PLUGIN_VERSION = "0.0.1";
        public const string GAME_NAME = "VRoidStudio.exe";
        public static readonly DirectoryInfo I18nFilePath = new DirectoryInfo($"{Paths.GameRootPath}\\locates\\zh_CN\\");
    }

    [BepInPlugin(PluginMeta.PLUGIN_GUID, PluginMeta.PLUGIN_NAME, PluginMeta.PLUGIN_VERSION)]
    [BepInProcess(PluginMeta.GAME_NAME)]
    public class Plugin : BasePlugin
    {
        public ConfigEntry<bool> OnStartDump;
        public ConfigEntry<bool> OnHasNullValueDump;
        public ConfigEntry<bool> DebugMode;
        public ConfigEntry<BepInEx.Unity.IL2CPP.UnityEngine.KeyCode> UiRefreshShortcut;
        public ConfigEntry<BepInEx.Unity.IL2CPP.UnityEngine.KeyCode> LanguageSwitchShortcut;
        public bool HasNullValue;
        public string RawMessage;
        public string RawString;
        public Dictionary<string, string> RawStringDict = new Dictionary<string, string>();
        public string MergeMessage;
        public string MergeString;
        public bool ShowUpdateTip;
        public bool IsFallback;
        public bool IsLanguageChanged;

        public override void Load()
        {
            Log.LogMessage($"Plugin {PluginMeta.PLUGIN_GUID} {PluginMeta.PLUGIN_VERSION} is loaded for {PluginMeta.GAME_NAME}!");
            try
            {
                InitPluginConfig();
                InitI18n();
            }
            catch (Exception e)
            {
                Log.LogError(e);
                ShowUpdateTip = true;
            }
        }

        private void InitI18n()
        {
            System.Diagnostics.Stopwatch sw = new();
            sw.Start();

            if (!PluginMeta.I18nFilePath.Exists)
            {
                PluginMeta.I18nFilePath.Create();
            }

            SwitchToChinese();

            Harmony.CreateAndPatchAll(typeof(Plugin));
            VRoid.UI.EditorOption.EditorOptionManager.Instance.EditorOption.Preference.languageMode = VRoid.UI.EditorOption.LanguageMode.En;
            Messages.CurrentCrowdinLanguageCode = "en";
            sw.Stop();

            Log.LogDebug($"i18n complete in {sw.ElapsedMilliseconds}ms");
        }

        private void InitPluginConfig()
        {
            OnStartDump = Config.Bind<bool>("config", "OnStartDump", false, "当启动时进行转储 (原词条)");
            OnHasNullValueDump = Config.Bind<bool>("config", "OnHasNullValueDump", false, "当缺失词条时进行转储 (合并后词条)");
            DebugMode = Config.Bind<bool>("config", "DebugMode", false, "调试模式");
            UiRefreshShortcut = Config.Bind("config", "UiRefreshShortcut", BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.F10, "[仅限开发模式] 刷新语言快捷键");
            LanguageSwitchShortcut = Config.Bind("config", "LanguageSwitchShortcut", BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.F11, "[仅限开发模式] 切换语言快捷键");

            CreateBackup();
            if (OnStartDump.Value)
            {
                DumpRaw();
            }
        }

        public void SwitchToChinese()
        {
            HasNullValue = false;
            I18nStrings();
            I18nMessages();
            try
            {
                Messages.OnMessagesLanguageChange?.Invoke();
                IsLanguageChanged = true;
            }
            catch (System.Exception e)
            {
                Log.LogError($"Error on refresh UI: {e.Message}\n{e.StackTrace}");
                IsFallback = true;
                SwitchToEnglish();
            }
        }

        public void SwitchToEnglish()
        {
            Messages.s_localeDictionary["en"] = JsonConvert.DeserializeObject<Messages>(RawMessage);
            Messages.OnMessagesLanguageChange?.Invoke();
            foreach (var kv in RawStringDict)
            {
                Messages.s_localeStringDictionary["en"][kv.Key] = kv.Value;
            }
            IsLanguageChanged = false;
        }

        public void I18nMessages()
        {
            Log.LogDebug("bug on InitPluginConfig");

            string messageFilePath = $"{PluginMeta.I18nFilePath}\\messages.json";
            if (!File.Exists(messageFilePath))
            {
                Log.LogError($"No {messageFilePath} founded");
                return;
            }
            try
            {
                string json = File.ReadAllText(messageFilePath);
                JSONObject ori = new JSONObject(RawMessage);
                JSONObject cnJson = new JSONObject(json);

                MergeJson(ori, cnJson);

                JSONObject sortJson = JsonSorter(ori);
                MergeMessage = sortJson.ToString();
                Messages cn = JsonConvert.DeserializeObject<Messages>(MergeMessage);

                if (HasNullValue)
                {
                    if (OnHasNullValueDump.Value)
                    {
                        DumpMerge();
                    }
                }
                Messages.s_localeDictionary["en"] = cn;
            }
            catch (System.Exception e)
            {
                Log.LogError($"Error on load i18n: {e.Message}\n{e.StackTrace}");
            }
        }

        public void I18nStrings()
        {
            string stringFilePath = $"{PluginMeta.I18nFilePath}\\string";
            if (!File.Exists(stringFilePath))
            {
                Log.LogError($"No {stringFilePath} founded");
                return;
            }
            try
            {
                string[] lines = File.ReadAllLines(stringFilePath);
                var strDict = Messages.s_localeStringDictionary["en"];
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var kv = line.Split(new char[] { '=' }, 2);
                        if (kv.Length == 2)
                        {
                            strDict[kv[0]] = kv[1].Replace("\\r\\n", "\r\n");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.LogError($"Unable to decode: {e.Message}\n{e.StackTrace}");
            }
        }

        public void DumpMerge()
        {
            Messages messages = JsonConvert.DeserializeObject<Messages>(MergeMessage);
            string messagesStr = JsonConvert.SerializeObject(messages);
            File.WriteAllText($"{PluginMeta.I18nFilePath.FullName}\\DumpMergeMessages.json", messagesStr);
            var strDict = Messages.s_localeStringDictionary["en"];
            StringBuilder sb = new StringBuilder();
            foreach (var kv in strDict)
            {
                string value = kv.Value.Replace("\r\n", "\\r\\n");
                sb.AppendLine($"{kv.Key}={value}");
            }
            File.WriteAllText($"{PluginMeta.I18nFilePath.FullName}\\DumpMergeString.txt", sb.ToString());
        }

        public void CreateBackup()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            RawMessage = JsonConvert.SerializeObject(Messages.All["en"]);
            var enDict = Messages.s_localeStringDictionary["en"];
            StringBuilder sb = new StringBuilder();
            foreach (var kv in enDict)
            {
                RawStringDict.Add(kv.Key, kv.Value);
                string value = kv.Value.Replace("\r\n", "\\r\\n");
                sb.AppendLine($"{kv.Key}={value}");
            }
            RawString = sb.ToString();
            sw.Stop();
        }

        public void DumpRaw()
        {
            File.WriteAllText($"{PluginMeta.I18nFilePath.FullName}\\DumpMessages_en_{PluginMeta.PLUGIN_VERSION}.json", RawMessage);
            File.WriteAllText($"{PluginMeta.I18nFilePath.FullName}\\DumpString_en_{PluginMeta.PLUGIN_VERSION}.txt", RawString);
        }

        public JSONObject JsonSorter(JSONObject baseJson)
        {
            if (baseJson.type == JSONObject.Type.OBJECT)
            {
                List<string> keys = new List<string>(baseJson.keys);
                keys.Sort();
                JSONObject obj = new JSONObject(JSONObject.Type.OBJECT);
                foreach (var key in keys)
                {
                    obj.SetField(key, baseJson[key]);
                }
                return obj;
            }
            else
            {
                return baseJson;
            }
        }

        public void MergeJson(JSONObject baseJson, JSONObject modJson)
        {
            List<string> baseKeys = new List<string>(baseJson.keys);
            foreach (var key in baseKeys)
            {
                if (modJson.HasField(key))
                {
                    if (baseJson[key].IsString)
                    {
                        baseJson.SetField(key, modJson[key]);
                    }
                    else if (baseJson[key].IsObject)
                    {
                        MergeJson(baseJson[key], modJson[key]);
                    }
                }
                else
                {
                    HasNullValue = true;
                    Log.LogWarning($"Detected missing value: {key}:{baseJson[key]}");
                }
            }
        }
    }
}
