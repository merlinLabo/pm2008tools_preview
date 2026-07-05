using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PM2008Tuner
{
    public sealed class KeyBindingConfig
    {
        public string Action { get; set; }
        public int SourceKey { get; set; }
        public int SourceKey2 { get; set; }
        public int TargetKey { get; set; }
        public int DelayMs { get; set; }

        [ScriptIgnore]
        public string SourceKeyName
        {
            get { return KeyNames.Get(SourceKey); }
        }

        [ScriptIgnore]
        public string SourceKey2Name
        {
            get { return KeyNames.Get(SourceKey2); }
        }

        [ScriptIgnore]
        public string TargetKeyName
        {
            get { return KeyNames.Get(TargetKey); }
        }
    }

    public sealed class AppConfig
    {
        public int Version { get; set; }
        public string GameDirectory { get; set; }
        public bool EnableKeyboardRemap { get; set; }
        public int GlobalInputDelayMs { get; set; }
        public int AudioGainPercent { get; set; }
        public int AudioDeviceSamples { get; set; }
        public int AudioQueueTargetMs { get; set; }
        public int AudioQueueMaxMs { get; set; }
        public string AudioQueuePolicy { get; set; }
        public int DisplayScale { get; set; }
        public string DisplayMode { get; set; }
        public string DisplayFilter { get; set; }
        public string RenderBackend { get; set; }
        public string WindowsRender { get; set; }
        public bool GuestPresentPacing { get; set; }
        public bool RealtimeBacklogDrop { get; set; }
        public long RealtimeBacklogThresholdNs { get; set; }
        public int Reset27SettleMs { get; set; }
        public string A27ClockSpeed { get; set; }
        public bool TraceInput { get; set; }
        public List<KeyBindingConfig> Bindings { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                Version = 5,
                GameDirectory = "",
                EnableKeyboardRemap = true,
                GlobalInputDelayMs = 0,
                AudioGainPercent = 70,
                AudioDeviceSamples = 256,
                AudioQueueTargetMs = 32,
                AudioQueueMaxMs = 64,
                AudioQueuePolicy = "clear",
                DisplayScale = 2,
                DisplayMode = "borderless",
                DisplayFilter = "nearest",
                RenderBackend = "sdl-native",
                WindowsRender = "auto",
                GuestPresentPacing = true,
                RealtimeBacklogDrop = true,
                RealtimeBacklogThresholdNs = 0,
                Reset27SettleMs = 0,
                A27ClockSpeed = "",
                TraceInput = false,
                Bindings = DefaultBindings()
            };
        }

        public void Normalize()
        {
            if (Version < 2)
            {
                Bindings = UpgradeV1Bindings(Bindings);
                Version = 2;
            }
            if (Version < 3)
            {
                if (Bindings != null)
                    foreach (KeyBindingConfig binding in Bindings) binding.SourceKey2 = 0;
                Version = 3;
            }
            if (Version < 4)
            {
                FixTestMenuDefault(Bindings);
                Version = 4;
            }
            if (Version < 5)
            {
                RenameP2Start(Bindings);
                Version = 5;
            }
            if (Bindings == null || Bindings.Count == 0)
                Bindings = DefaultBindings();
            if (String.IsNullOrWhiteSpace(AudioQueuePolicy)) AudioQueuePolicy = "clear";
            if (String.IsNullOrWhiteSpace(DisplayMode)) DisplayMode = "borderless";
            if (String.IsNullOrWhiteSpace(DisplayFilter)) DisplayFilter = "nearest";
            if (String.IsNullOrWhiteSpace(RenderBackend)) RenderBackend = "sdl-native";
            if (String.IsNullOrWhiteSpace(WindowsRender)) WindowsRender = "auto";
            if (AudioDeviceSamples < 64) AudioDeviceSamples = 256;
            if (AudioQueueTargetMs < 1) AudioQueueTargetMs = 32;
            if (AudioQueueMaxMs < AudioQueueTargetMs) AudioQueueMaxMs = AudioQueueTargetMs * 2;
            if (DisplayScale < 1) DisplayScale = 1;
            if (AudioGainPercent < 0) AudioGainPercent = 0;
            if (AudioGainPercent > 200) AudioGainPercent = 200;
        }

        private static void FixTestMenuDefault(List<KeyBindingConfig> bindings)
        {
            if (bindings == null) return;
            KeyBindingConfig testMenu = bindings.Find(delegate(KeyBindingConfig binding)
            {
                return String.Equals(binding.Action, "Test Menu", StringComparison.OrdinalIgnoreCase);
            });
            if (testMenu == null) return;
            int previousTarget = testMenu.TargetKey;
            testMenu.TargetKey = (int)Keys.T;
            if (testMenu.SourceKey == previousTarget) testMenu.SourceKey = (int)Keys.T;
        }

        private static void RenameP2Start(List<KeyBindingConfig> bindings)
        {
            if (bindings == null) return;
            foreach (KeyBindingConfig binding in bindings)
                if (String.Equals(binding.Action, "P2 Start / Test 返回", StringComparison.OrdinalIgnoreCase))
                    binding.Action = "P2 Start";
        }

        private static List<KeyBindingConfig> DefaultBindings()
        {
            return new List<KeyBindingConfig>
            {
                Bind("1P Drum Blue", Keys.S),
                Bind("1P Drum Red", Keys.L),
                Bind("1P Drum Rim Left", Keys.D),
                Bind("1P Drum Rim Right", Keys.K),
                Bind("1P Drum Left", Keys.F),
                Bind("1P Drum Right", Keys.J),
                Bind("Insert Coin", Keys.P),
                Bind("Service", Keys.R),
                Bind("Test Menu", Keys.T),
                Bind("P1 Start", Keys.Enter),
                Bind("P2 Start", Keys.Back)
            };
        }

        private static List<KeyBindingConfig> UpgradeV1Bindings(List<KeyBindingConfig> oldBindings)
        {
            List<KeyBindingConfig> result = DefaultBindings();
            if (oldBindings == null) return result;

            MigrateByOldTarget(result, "1P Drum Blue", Keys.S, oldBindings);
            MigrateByOldTarget(result, "1P Drum Red", Keys.L, oldBindings);
            MigrateByOldTarget(result, "1P Drum Rim Left", Keys.D, oldBindings);
            MigrateByOldTarget(result, "1P Drum Rim Right", Keys.K, oldBindings);
            MigrateByOldTarget(result, "1P Drum Left", Keys.F, oldBindings);
            MigrateByOldTarget(result, "1P Drum Right", Keys.J, oldBindings);
            MigrateByOldAction(result, "Insert Coin", "投币", oldBindings);
            MigrateByOldAction(result, "Service", "服务键", oldBindings);
            MigrateByOldAction(result, "Test Menu", "功能键 F1", oldBindings);
            MigrateByOldAction(result, "P1 Start", "1P 开始", oldBindings);
            MigrateByOldAction(result, "P2 Start", "2P 开始", oldBindings);
            return result;
        }

        private static void MigrateByOldTarget(List<KeyBindingConfig> result, string newAction, Keys oldTarget,
            List<KeyBindingConfig> oldBindings)
        {
            KeyBindingConfig old = oldBindings.Find(delegate(KeyBindingConfig b)
            {
                return b.TargetKey == (int)oldTarget;
            });
            PreserveCustomizedSource(result, newAction, old);
        }

        private static void MigrateByOldAction(List<KeyBindingConfig> result, string newAction, string oldAction,
            List<KeyBindingConfig> oldBindings)
        {
            KeyBindingConfig old = oldBindings.Find(delegate(KeyBindingConfig b)
            {
                return String.Equals(b.Action, oldAction, StringComparison.OrdinalIgnoreCase);
            });
            PreserveCustomizedSource(result, newAction, old);
        }

        private static void PreserveCustomizedSource(List<KeyBindingConfig> result, string newAction, KeyBindingConfig old)
        {
            if (old == null) return;
            KeyBindingConfig current = result.Find(delegate(KeyBindingConfig b)
            {
                return String.Equals(b.Action, newAction, StringComparison.Ordinal);
            });
            if (current == null) return;
            // 旧版来源键等于旧目标键，说明用户没有改过，采用已校正的新默认键。
            if (old.SourceKey != old.TargetKey) current.SourceKey = old.SourceKey;
            current.DelayMs = old.DelayMs;
        }

        private static KeyBindingConfig Bind(string action, Keys key)
        {
            return new KeyBindingConfig
            {
                Action = action,
                SourceKey = (int)key,
                SourceKey2 = 0,
                TargetKey = (int)key,
                DelayMs = 0
            };
        }
    }

    internal static class ConfigStore
    {
        internal static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config.json");
        internal static readonly string LegacyConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "pm2008_tuner.json");

        internal static AppConfig Load()
        {
            string loadPath = File.Exists(ConfigPath) ? ConfigPath : LegacyConfigPath;
            if (!File.Exists(loadPath)) return AppConfig.CreateDefault();
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppConfig config = serializer.Deserialize<AppConfig>(File.ReadAllText(loadPath, Encoding.UTF8));
                if (config == null) return AppConfig.CreateDefault();
                config.Normalize();
                return config;
            }
            catch
            {
                return AppConfig.CreateDefault();
            }
        }

        internal static void Save(AppConfig config)
        {
            config.Normalize();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(config);
            File.WriteAllText(ConfigPath, PrettyJson.Format(json), new UTF8Encoding(false));
        }
    }

    internal static class KeyNames
    {
        internal static string Get(int value)
        {
            if (value == 0) return "未设置";
            Keys key = (Keys)value;
            if (key == Keys.Enter) return "Enter";
            if (key == Keys.Back) return "Backspace";
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((char)('0' + (key - Keys.D0))).ToString();
            return key.ToString();
        }
    }

    internal static class PrettyJson
    {
        internal static string Format(string json)
        {
            StringBuilder output = new StringBuilder();
            bool quoted = false;
            bool escaped = false;
            int depth = 0;
            foreach (char c in json)
            {
                if (c == '"' && !escaped) quoted = !quoted;
                if (!quoted)
                {
                    if (c == '{' || c == '[')
                    {
                        output.Append(c).AppendLine();
                        depth++;
                        output.Append(new string(' ', depth * 2));
                        escaped = false;
                        continue;
                    }
                    if (c == '}' || c == ']')
                    {
                        output.AppendLine();
                        depth--;
                        output.Append(new string(' ', depth * 2)).Append(c);
                        escaped = false;
                        continue;
                    }
                    if (c == ',')
                    {
                        output.Append(c).AppendLine().Append(new string(' ', depth * 2));
                        escaped = false;
                        continue;
                    }
                    if (c == ':')
                    {
                        output.Append(": ");
                        escaped = false;
                        continue;
                    }
                }
                output.Append(c);
                escaped = c == '\\' && !escaped;
                if (c != '\\') escaped = false;
            }
            return output.ToString();
        }
    }
}
