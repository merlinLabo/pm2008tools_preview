using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PM2008Tuner
{
    internal sealed class LaunchSpec
    {
        internal string LauncherPath;
        internal string WorkingDirectory;
        internal string Arguments;
        internal Dictionary<string, string> Environment = new Dictionary<string, string>();
    }

    internal static class LaunchBuilder
    {
        internal static LaunchSpec Build(AppConfig c)
        {
            string root = (c.GameDirectory ?? "").Trim().TrimEnd(Path.DirectorySeparatorChar);
            LaunchSpec spec = new LaunchSpec();
            spec.WorkingDirectory = root;
            spec.LauncherPath = Path.Combine(root, "uclc_pm2008_launcher.exe");

            List<string> a = new List<string>();
            Add(a, "--game-dir", root + Path.DirectorySeparatorChar + ".");
            Add(a, "--game-bin", "pm2008v2");
            Add(a, "--a27-internal-rom", Path.Combine(root, "irom.bin"));
            Add(a, "--a27-external-rom", Path.Combine(root, "erom.bin"));
            Add(a, "--card1", Path.Combine(root, "iccard_p1.sle4442"));
            Add(a, "--a27-sram", Path.Combine(root, "sram.bin"));
            a.Add("--card1-present");
            a.Add("--no-card2-present");
            Add(a, "--audio-gain-percent", c.AudioGainPercent.ToString(CultureInfo.InvariantCulture));
            Add(a, "--display-scale", c.DisplayScale.ToString(CultureInfo.InvariantCulture));
            Add(a, "--display-mode", c.DisplayMode);
            Add(a, "--display-filter", c.DisplayFilter);
            Add(a, "--render-backend", c.RenderBackend);
            Add(a, "--windows-render", c.WindowsRender);

            a.Add(c.GuestPresentPacing ? "--guest-present-pacing" : "--no-guest-present-pacing");
            a.Add(c.RealtimeBacklogDrop ? "--a27-realtime-backlog-drop" : "--no-a27-realtime-backlog-drop");
            if (c.RealtimeBacklogThresholdNs > 0)
                Add(a, "--a27-realtime-backlog-drop-threshold-ns", c.RealtimeBacklogThresholdNs.ToString(CultureInfo.InvariantCulture));
            if (c.Reset27SettleMs > 0)
                Add(a, "--reset27-settle-ms", c.Reset27SettleMs.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrWhiteSpace(c.A27ClockSpeed))
                Add(a, "--a27-clock-speed", c.A27ClockSpeed.Trim());
            if (c.TraceInput) a.Add("--trace-sdl-input");

            spec.Arguments = String.Join(" ", a.ToArray());
            spec.Environment["UCLC_AUDIO_DEVICE_SAMPLES"] = c.AudioDeviceSamples.ToString(CultureInfo.InvariantCulture);
            spec.Environment["UCLC_AUDIO_QUEUE_TARGET_MS"] = c.AudioQueueTargetMs.ToString(CultureInfo.InvariantCulture);
            spec.Environment["UCLC_AUDIO_QUEUE_MAX_MS"] = c.AudioQueueMaxMs.ToString(CultureInfo.InvariantCulture);
            spec.Environment["UCLC_AUDIO_QUEUE_POLICY"] = c.AudioQueuePolicy;
            return spec;
        }

        internal static string Preview(AppConfig c)
        {
            LaunchSpec spec = Build(c);
            StringBuilder b = new StringBuilder();
            foreach (KeyValuePair<string, string> item in spec.Environment)
                b.Append("set \"").Append(item.Key).Append('=').Append(item.Value).AppendLine("\"");
            b.Append('"').Append(spec.LauncherPath).Append("\" ").Append(spec.Arguments);
            return b.ToString();
        }

        internal static string Validate(LaunchSpec spec)
        {
            if (String.IsNullOrWhiteSpace(spec.WorkingDirectory) || !Directory.Exists(spec.WorkingDirectory))
                return "游戏目录不存在。";
            string[] files =
            {
                spec.LauncherPath,
                Path.Combine(spec.WorkingDirectory, "pm2008v2"),
                Path.Combine(spec.WorkingDirectory, "irom.bin"),
                Path.Combine(spec.WorkingDirectory, "erom.bin")
            };
            foreach (string file in files)
                if (!File.Exists(file)) return "缺少文件：" + file;
            return null;
        }

        private static void Add(List<string> args, string name, string value)
        {
            args.Add(name);
            args.Add(Quote(value));
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
