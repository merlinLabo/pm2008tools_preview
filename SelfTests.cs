using System;

namespace PM2008Tuner
{
    internal static class SelfTests
    {
        internal static int Run()
        {
            try
            {
                AppConfig c = AppConfig.CreateDefault();
                Assert(c.GameDirectory == "", "default game directory is empty");
                c.GameDirectory = @"C:\Games\PM 2008";
                LaunchSpec spec = LaunchBuilder.Build(c);
                Assert(spec.Arguments.Contains("--game-bin \"pm2008v2\""), "game-bin argument");
                Assert(spec.Arguments.Contains("\"C:\\Games\\PM 2008\\irom.bin\""), "quoted ROM path");
                Assert(spec.Environment["UCLC_AUDIO_DEVICE_SAMPLES"] == "256", "audio samples");
                Assert(c.Version == 5, "config version");
                Assert(System.IO.Path.GetFileName(ConfigStore.ConfigPath) == "config.json", "config filename");
                Assert(c.Bindings.Count == 11, "default binding count");
                Assert(Find(c, "1P Drum Blue").TargetKey == (int)System.Windows.Forms.Keys.S, "blue target");
                Assert(Find(c, "1P Drum Red").TargetKey == (int)System.Windows.Forms.Keys.L, "red target");
                Assert(Find(c, "Insert Coin").TargetKey == (int)System.Windows.Forms.Keys.P, "coin target");
                Assert(Find(c, "P1 Start").TargetKey == (int)System.Windows.Forms.Keys.Enter, "start target");
                Assert(Find(c, "P2 Start").TargetKey == (int)System.Windows.Forms.Keys.Back, "back target");
                Assert(Find(c, "1P Drum Blue").SourceKey2 == 0, "second mapping empty by default");
                string launcher = @"D:\Arcade\PM2008v2\uclc_pm2008_launcher.exe";
                if (System.IO.File.Exists(launcher))
                {
                    Assert(RawInputPatcher.FindImportIatRva(launcher, "GetRawInputData") > 0, "GetRawInputData IAT");
                    Assert(RawInputPatcher.FindImportIatRva(launcher, "Sleep") > 0, "Sleep IAT");
                }
                c.AudioQueueTargetMs = 80;
                c.AudioQueueMaxMs = 20;
                c.Normalize();
                Assert(c.AudioQueueMaxMs >= c.AudioQueueTargetMs, "queue bounds");

                AppConfig legacy = AppConfig.CreateDefault();
                legacy.Version = 0;
                legacy.Bindings = new System.Collections.Generic.List<KeyBindingConfig>
                {
                    new KeyBindingConfig { Action = "1P 开始", SourceKey = (int)System.Windows.Forms.Keys.Z,
                        TargetKey = (int)System.Windows.Forms.Keys.D1, DelayMs = 7 },
                    new KeyBindingConfig { Action = "投币", SourceKey = (int)System.Windows.Forms.Keys.D5,
                        TargetKey = (int)System.Windows.Forms.Keys.D5, DelayMs = 0 }
                };
                legacy.Normalize();
                Assert(legacy.Version == 5, "legacy migrated to v5");
                Assert(Find(legacy, "P1 Start").SourceKey == (int)System.Windows.Forms.Keys.Z, "migration preserves custom source");
                Assert(Find(legacy, "P1 Start").TargetKey == (int)System.Windows.Forms.Keys.Enter, "migration fixes start target");
                Assert(Find(legacy, "Insert Coin").SourceKey == (int)System.Windows.Forms.Keys.P, "migration resets untouched source");

                AppConfig v3 = AppConfig.CreateDefault();
                v3.Version = 3;
                KeyBindingConfig oldTestMenu = Find(v3, "Test Menu");
                oldTestMenu.SourceKey = (int)System.Windows.Forms.Keys.F1;
                oldTestMenu.TargetKey = (int)System.Windows.Forms.Keys.F1;
                v3.Normalize();
                Assert(v3.Version == 5, "v3 migrated to v5");
                Assert(Find(v3, "Test Menu").TargetKey == (int)System.Windows.Forms.Keys.T, "test menu target fixed to T");
                Assert(Find(v3, "Test Menu").SourceKey == (int)System.Windows.Forms.Keys.T, "untouched test menu source fixed to T");

                AppConfig v4 = AppConfig.CreateDefault();
                v4.Version = 4;
                Find(v4, "P2 Start").Action = "P2 Start / Test 返回";
                v4.Normalize();
                Assert(v4.Version == 5, "v4 migrated to v5");
                Assert(Find(v4, "P2 Start") != null, "P2 Start action renamed");

                AppConfig dual = AppConfig.CreateDefault();
                KeyBindingConfig dualBlue = Find(dual, "1P Drum Blue");
                dualBlue.SourceKey = (int)System.Windows.Forms.Keys.Q;
                dualBlue.SourceKey2 = (int)System.Windows.Forms.Keys.W;
                Assert(RawInputPatcher.ValidateBindings(dual) == null, "two mappings accepted");
                Assert(RawInputPatcher.NeedsPatch(dual), "two mappings require patch");
                dualBlue.SourceKey = 0;
                dualBlue.SourceKey2 = 0;
                Assert(RawInputPatcher.ValidateBindings(dual) == null, "empty mappings accepted");
                Assert(RawInputPatcher.NeedsPatch(dual), "empty mappings disable default key");
                Assert(MainForm.GetErrorCode(new InvalidOperationException("注入失败，Windows 错误：87")) == "87",
                    "injection Win32 error code");
                Assert(MainForm.GetErrorCode(new InvalidOperationException("版本不兼容")).StartsWith("0x"),
                    "injection fallback error code");
                Console.WriteLine("PM2008Tuner self-test: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PM2008Tuner self-test: FAIL - " + ex.Message);
                return 1;
            }
        }

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
        }

        private static KeyBindingConfig Find(AppConfig config, string action)
        {
            return config.Bindings.Find(delegate(KeyBindingConfig b) { return b.Action == action; });
        }
    }
}
