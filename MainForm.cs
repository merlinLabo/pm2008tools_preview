using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PM2008Tuner
{
    public sealed class MainForm : Form
    {
        private AppConfig config;
        private Process gameProcess;

        private TextBox gameDirBox;
        private CheckBox remapBox;
        private NumericUpDown globalDelayBox;
        private DataGridView bindingsGrid;
        private NumericUpDown gainBox;
        private NumericUpDown samplesBox;
        private NumericUpDown queueTargetBox;
        private NumericUpDown queueMaxBox;
        private ComboBox queuePolicyBox;
        private NumericUpDown displayScaleBox;
        private ComboBox displayModeBox;
        private ComboBox displayFilterBox;
        private ComboBox renderBackendBox;
        private ComboBox windowsRenderBox;
        private CheckBox pacingBox;
        private CheckBox backlogBox;
        private NumericUpDown backlogThresholdBox;
        private NumericUpDown resetSettleBox;
        private TextBox a27ClockBox;
        private CheckBox traceBox;
        private Label statusLabel;
        private Button saveButton;
        private Button runButton;
        private Button stopButton;

        public MainForm()
        {
            Text = "PM2008 Configuration";
            Icon = SystemIcons.Question;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            Size = new Size(352, 445);
            MinimumSize = Size;
            MaximumSize = Size;
            Font = new Font("Microsoft YaHei UI", 9F);

            config = ConfigStore.Load();
            BuildUi();
            LoadControls();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }

        private void BuildUi()
        {
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(62, 24);

            TabPage inputPage = new TabPage("输入映射");
            TabPage audioPage = new TabPage("音频设置");
            TabPage emulationPage = new TabPage("仿真设置");
            TabPage displayPage = new TabPage("显示设置");
            TabPage otherPage = new TabPage("其他设置");
            tabs.TabPages.Add(inputPage);
            tabs.TabPages.Add(audioPage);
            tabs.TabPages.Add(emulationPage);
            tabs.TabPages.Add(displayPage);
            tabs.TabPages.Add(otherPage);

            BuildInputPage(inputPage);
            BuildAudioPage(audioPage);
            BuildEmulationPage(emulationPage);
            BuildDisplayPage(displayPage);
            BuildOtherPage(otherPage);

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6, 7, 6, 7) };
            saveButton = new Button { Text = "保存", Width = 58, Height = 32, Dock = DockStyle.Right };
            runButton = new Button { Text = "运行", Width = 58, Height = 32, Dock = DockStyle.Right };
            stopButton = new Button { Text = "停止", Width = 58, Height = 32, Dock = DockStyle.Right, Enabled = false };
            statusLabel = new Label { Text = "", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            saveButton.Click += delegate { SaveConfiguration(); };
            runButton.Click += delegate { LaunchGame(); };
            stopButton.Click += delegate { StopGame(); };
            bottom.Controls.Add(statusLabel);
            bottom.Controls.Add(saveButton);
            bottom.Controls.Add(runButton);
            bottom.Controls.Add(stopButton);

            Controls.Add(tabs);
            Controls.Add(bottom);
        }

        private void BuildInputPage(TabPage page)
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(7) };
            remapBox = new CheckBox { Text = "启动键位修改注入", AutoSize = true, Location = new Point(7, 7) };
            Label delayLabel = new Label { Text = "全局输入延迟 (ms)", AutoSize = true, Location = new Point(7, 36) };
            globalDelayBox = MakeNumber(0, 500, 0, 8);
            globalDelayBox.Size = new Size(78, 23);
            globalDelayBox.Location = new Point(142, 33);
            top.Controls.Add(remapBox);
            top.Controls.Add(delayLabel);
            top.Controls.Add(globalDelayBox);

            bindingsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeight = 38,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 22 },
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            bindingsGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            bindingsGrid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8F);
            bindingsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", ReadOnly = true, Width = 96 });
            bindingsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mapping1", HeaderText = "映射1", ReadOnly = true, Width = 45 });
            bindingsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mapping2", HeaderText = "映射2", ReadOnly = true, Width = 45 });
            bindingsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DefaultKey", HeaderText = "默认键位", ReadOnly = true, Width = 54 });
            bindingsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delay", HeaderText = "输入延迟 (ms)", Width = 61 });
            bindingsGrid.CellDoubleClick += BindingsGridCellDoubleClick;
            bindingsGrid.CellValidating += BindingsGridCellValidating;

            Panel tools = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(7, 4, 7, 4) };
            Button resetButton = new Button { Text = "重置所有映射", Width = 116, Dock = DockStyle.Left };
            ToolTip mappingTip = new ToolTip();
            mappingTip.SetToolTip(bindingsGrid, "双击映射1或映射2捕获按键；按 Backspace 清空。 ");
            resetButton.Click += delegate { ResetAllMappings(); };
            tools.Controls.Add(resetButton);

            page.Controls.Add(bindingsGrid);
            page.Controls.Add(tools);
            page.Controls.Add(top);
        }

        private void BuildAudioPage(TabPage page)
        {
            TableLayoutPanel table = MakeTable();
            gainBox = MakeNumber(0, 200, 70, 8);
            samplesBox = MakeNumber(64, 4096, 256, 64);
            queueTargetBox = MakeNumber(1, 250, 32, 1);
            queueMaxBox = MakeNumber(2, 500, 64, 1);
            queuePolicyBox = MakeCombo("clear", "block");
            AddRow(table, "音量增益 (%)", gainBox, "");
            AddRow(table, "音频缓冲大小", samplesBox, "越小延迟越低，但可能出现爆音，调整范围建议 128-512");
            AddRow(table, "音频队列目标延迟值 (ms)", queueTargetBox, "默认为 32ms");
            AddRow(table, "音频队列上限延迟值 (ms)", queueMaxBox, "不得小于目标值");
            AddRow(table, "音频队列溢出策略", queuePolicyBox, "默认为 clear，block 可能造成画面卡顿");

            FinishTable(table);
            page.Controls.Add(table);
            page.Controls.Add(BuildPresetPanel());
        }

        private void BuildEmulationPage(TabPage page)
        {
            TableLayoutPanel table = MakeTable();
            pacingBox = new CheckBox { Text = "开启", AutoSize = true };
            backlogBox = new CheckBox { Text = "开启过期丢弃", AutoSize = true };
            backlogThresholdBox = MakeNumber(0, 1000000000M, 0, 1000000);
            resetSettleBox = MakeNumber(0, 60000, 0, 10);
            a27ClockBox = new TextBox { Dock = DockStyle.Fill };

            AddRow(table, "游戏画面节拍同步", pacingBox, "建议保持开启，关闭后游戏逻辑可能过快，导致音画异常");
            AddRow(table, "A27 仿真积压", backlogBox, "建议保持开启");
            AddRow(table, "A27 仿真积压阈值 (ns)", backlogThresholdBox, "默认为 0");
            AddRow(table, "A27 复位等待时间 (ms)", resetSettleBox, "默认为 0；仅延长启动时间，对游戏暂无改善");
            AddRow(table, "A27 时钟倍率", a27ClockBox, "留空为默认倍率");
            FinishTable(table);
            page.Controls.Add(table);
        }

        private FlowLayoutPanel BuildPresetPanel()
        {
            FlowLayoutPanel presets = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 43, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Label label = new Label { Text = "预设", AutoSize = true, Margin = new Padding(2, 7, 4, 3) };
            Button low = new Button { Text = "低延迟", Width = 66, Height = 27, Margin = new Padding(2, 1, 2, 1) };
            Button balanced = new Button { Text = "平衡", Width = 58, Height = 27, Margin = new Padding(2, 1, 2, 1) };
            Button stable = new Button { Text = "稳定", Width = 58, Height = 27, Margin = new Padding(2, 1, 2, 1) };
            low.Click += delegate { SetAudioPreset(128, 16, 32); };
            balanced.Click += delegate { SetAudioPreset(256, 32, 64); };
            stable.Click += delegate { SetAudioPreset(512, 48, 96); };
            presets.Controls.Add(label);
            presets.Controls.Add(low);
            presets.Controls.Add(balanced);
            presets.Controls.Add(stable);
            return presets;
        }

        private void BuildDisplayPage(TabPage page)
        {
            TableLayoutPanel table = MakeTable();
            displayScaleBox = MakeNumber(1, 8, 2, 1);
            displayModeBox = MakeCombo("windowed", "borderless");
            displayFilterBox = MakeCombo("nearest", "linear");
            renderBackendBox = MakeCombo("sdl-native", "soft");
            windowsRenderBox = MakeCombo("auto", "sdl", "d3d11");
            AddRow(table, "显示倍率", displayScaleBox, "");
            AddRow(table, "显示模式", displayModeBox, "");
            AddRow(table, "缩放过滤", displayFilterBox, "");
            AddRow(table, "渲染后端", renderBackendBox, "");
            AddRow(table, "Windows 渲染", windowsRenderBox, "");

            FinishTable(table);
            page.Controls.Add(table);
        }

        private void BuildOtherPage(TabPage page)
        {
            TableLayoutPanel table = MakeTable();
            Panel pathPanel = new Panel { Dock = DockStyle.Fill, Height = 28 };
            gameDirBox = new TextBox { Dock = DockStyle.Fill };
            Button browse = new Button { Text = "浏览…", Dock = DockStyle.Right, Width = 58 };
            browse.Click += delegate { BrowseGameDirectory(); };
            pathPanel.Controls.Add(gameDirBox);
            pathPanel.Controls.Add(browse);
            traceBox = new CheckBox { Text = "输出 SDL 输入跟踪", AutoSize = true };

            AddRow(table, "游戏目录", pathPanel, "即包含 Launcher 的目录");
            AddRow(table, "诊断", traceBox, "");
            FinishTable(table);
            page.Controls.Add(table);
        }

        private void LoadControls()
        {
            gameDirBox.Text = config.GameDirectory;
            remapBox.Checked = config.EnableKeyboardRemap;
            globalDelayBox.Value = Clamp(config.GlobalInputDelayMs, globalDelayBox);
            gainBox.Value = Clamp(config.AudioGainPercent, gainBox);
            samplesBox.Value = Clamp(config.AudioDeviceSamples, samplesBox);
            queueTargetBox.Value = Clamp(config.AudioQueueTargetMs, queueTargetBox);
            queueMaxBox.Value = Clamp(config.AudioQueueMaxMs, queueMaxBox);
            SelectCombo(queuePolicyBox, config.AudioQueuePolicy);
            displayScaleBox.Value = Clamp(config.DisplayScale, displayScaleBox);
            SelectCombo(displayModeBox, config.DisplayMode);
            SelectCombo(displayFilterBox, config.DisplayFilter);
            SelectCombo(renderBackendBox, config.RenderBackend);
            SelectCombo(windowsRenderBox, config.WindowsRender);
            pacingBox.Checked = config.GuestPresentPacing;
            backlogBox.Checked = config.RealtimeBacklogDrop;
            backlogThresholdBox.Value = Clamp(config.RealtimeBacklogThresholdNs, backlogThresholdBox);
            resetSettleBox.Value = Clamp(config.Reset27SettleMs, resetSettleBox);
            a27ClockBox.Text = config.A27ClockSpeed ?? "";
            traceBox.Checked = config.TraceInput;
            PopulateBindings();
        }

        private void SaveControls()
        {
            bindingsGrid.EndEdit();
            config.GameDirectory = gameDirBox.Text.Trim();
            config.EnableKeyboardRemap = remapBox.Checked;
            config.GlobalInputDelayMs = (int)globalDelayBox.Value;
            config.AudioGainPercent = (int)gainBox.Value;
            config.AudioDeviceSamples = (int)samplesBox.Value;
            config.AudioQueueTargetMs = (int)queueTargetBox.Value;
            config.AudioQueueMaxMs = (int)queueMaxBox.Value;
            config.AudioQueuePolicy = ComboValue(queuePolicyBox);
            config.DisplayScale = (int)displayScaleBox.Value;
            config.DisplayMode = ComboValue(displayModeBox);
            config.DisplayFilter = ComboValue(displayFilterBox);
            config.RenderBackend = ComboValue(renderBackendBox);
            config.WindowsRender = ComboValue(windowsRenderBox);
            config.GuestPresentPacing = pacingBox.Checked;
            config.RealtimeBacklogDrop = backlogBox.Checked;
            config.RealtimeBacklogThresholdNs = (long)backlogThresholdBox.Value;
            config.Reset27SettleMs = (int)resetSettleBox.Value;
            config.A27ClockSpeed = a27ClockBox.Text.Trim();
            config.TraceInput = traceBox.Checked;

            List<KeyBindingConfig> bindings = new List<KeyBindingConfig>();
            foreach (DataGridViewRow row in bindingsGrid.Rows)
            {
                KeyBindingConfig item = row.Tag as KeyBindingConfig;
                if (item == null) continue;
                int delay;
                if (!Int32.TryParse(Convert.ToString(row.Cells[4].Value), out delay)) delay = 0;
                item.DelayMs = Math.Max(0, Math.Min(500, delay));
                bindings.Add(item);
            }
            config.Bindings = bindings;
            config.Normalize();
        }

        private void PopulateBindings()
        {
            bindingsGrid.Rows.Clear();
            foreach (KeyBindingConfig item in config.Bindings)
            {
                int index = bindingsGrid.Rows.Add(item.Action, item.SourceKeyName, item.SourceKey2Name,
                    item.TargetKeyName, item.DelayMs);
                bindingsGrid.Rows[index].Tag = item;
            }
        }

        private void BindingsGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex != 1 && e.ColumnIndex != 2)) return;
            CaptureKey(e.RowIndex, e.ColumnIndex);
        }

        private void BindingsGridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 4) return;
            int delay;
            if (!Int32.TryParse(Convert.ToString(e.FormattedValue), out delay) || delay < 0 || delay > 500)
            {
                e.Cancel = true;
                statusLabel.Text = "";
                MessageBox.Show(this, "输入延迟必须是 0–500 ms。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CaptureKey(int rowIndex, int column)
        {
            DataGridViewRow row = bindingsGrid.Rows[rowIndex];
            KeyBindingConfig item = row.Tag as KeyBindingConfig;
            if (item == null) return;
            using (KeyCaptureDialog dialog = new KeyCaptureDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (column == 1) item.SourceKey = (int)dialog.SelectedKey;
                else item.SourceKey2 = (int)dialog.SelectedKey;
                row.Cells[1].Value = item.SourceKeyName;
                row.Cells[2].Value = item.SourceKey2Name;
                statusLabel.Text = "";
            }
        }

        private void ResetAllMappings()
        {
            foreach (DataGridViewRow row in bindingsGrid.Rows)
            {
                KeyBindingConfig item = row.Tag as KeyBindingConfig;
                if (item == null) continue;
                item.SourceKey = item.TargetKey;
                item.SourceKey2 = 0;
                item.DelayMs = 0;
                row.Cells[1].Value = item.SourceKeyName;
                row.Cells[2].Value = item.SourceKey2Name;
                row.Cells[4].Value = 0;
            }
            globalDelayBox.Value = 0;
            statusLabel.Text = "";
        }

        private void SetAudioPreset(int samples, int target, int max)
        {
            samplesBox.Value = Clamp(samples, samplesBox);
            queueTargetBox.Value = Clamp(target, queueTargetBox);
            queueMaxBox.Value = Clamp(max, queueMaxBox);
            queuePolicyBox.SelectedItem = "clear";
            statusLabel.Text = "";
        }

        private void SaveConfiguration()
        {
            statusLabel.Text = "";
            try
            {
                SaveControls();
                if (config.AudioQueueMaxMs < config.AudioQueueTargetMs)
                {
                    MessageBox.Show(this, "音频队列上限不能小于目标值。", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string bindingError = RawInputPatcher.ValidateBindings(config);
                if (bindingError != null)
                {
                    MessageBox.Show(this, bindingError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ConfigStore.Save(config);
                statusLabel.Text = "保存成功";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseGameDirectory()
        {
            statusLabel.Text = "";
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 PM2008v2 游戏目录";
                dialog.SelectedPath = gameDirBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        gameDirBox.Text = dialog.SelectedPath;
                        config.GameDirectory = dialog.SelectedPath.Trim();
                        ConfigStore.Save(config);
                        statusLabel.Text = "保存成功";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "保存失败：" + ex.Message, Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void LaunchGame()
        {
            statusLabel.Text = "";
            try
            {
                if (gameProcess != null && !gameProcess.HasExited)
                {
                    MessageBox.Show(this, "游戏已经在运行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SaveControls();
                if (config.AudioQueueMaxMs < config.AudioQueueTargetMs)
                {
                    MessageBox.Show(this, "音频队列上限不能小于目标值。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string bindingError = RawInputPatcher.ValidateBindings(config);
                if (bindingError != null)
                {
                    MessageBox.Show(this, bindingError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                LaunchSpec spec = LaunchBuilder.Build(config);
                string error = LaunchBuilder.Validate(spec);
                if (error != null)
                {
                    MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string rawInputStatus;
                gameProcess = RawInputPatcher.Start(spec, config, out rawInputStatus);
                gameProcess.EnableRaisingEvents = true;
                gameProcess.Exited += GameExited;
                runButton.Enabled = false;
                stopButton.Enabled = true;
            }
            catch (Exception ex)
            {
                if (RawInputPatcher.NeedsPatch(config) &&
                    !ex.Message.StartsWith("CreateProcess 失败", StringComparison.Ordinal))
                    statusLabel.Text = "注入错误代码：" + GetErrorCode(ex);
                MessageBox.Show(this, "启动失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GameExited(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                runButton.Enabled = true;
                stopButton.Enabled = false;
                statusLabel.Text = "";
            });
        }

        private void StopGame()
        {
            statusLabel.Text = "";
            try
            {
                if (gameProcess != null && !gameProcess.HasExited) gameProcess.Kill();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "停止失败：" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static string GetErrorCode(Exception exception)
        {
            const string marker = "Windows 错误：";
            string message = exception == null ? "" : exception.Message;
            int start = message.LastIndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                int end = start;
                while (end < message.Length && Char.IsDigit(message[end])) end++;
                if (end > start) return message.Substring(start, end - start);
            }
            return "0x" + (exception == null ? 0 : exception.HResult).ToString("X8");
        }

        private static TableLayoutPanel MakeTable()
        {
            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(7),
                ColumnCount = 2,
                RowCount = 0
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control, string help)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Label left = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            control.Margin = new Padding(3, 4, 3, 4);
            if (!(control is CheckBox)) control.Dock = DockStyle.Fill;
            table.Controls.Add(left, 0, row);
            table.Controls.Add(control, 1, row);
            if (!String.IsNullOrEmpty(help))
            {
                int helpRow = table.RowCount++;
                int helpHeight = help.Length > 29 ? 37 : 23;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, helpHeight));
                Label helpLabel = new Label
                {
                    Text = help,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.TopLeft,
                    ForeColor = Color.DimGray,
                    Padding = new Padding(4, 0, 2, 0)
                };
                table.Controls.Add(helpLabel, 0, helpRow);
                table.SetColumnSpan(helpLabel, 2);
            }
        }

        private static void FinishTable(TableLayoutPanel table)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Panel spacer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            table.Controls.Add(spacer, 0, row);
            table.SetColumnSpan(spacer, 2);
        }

        private static NumericUpDown MakeNumber(decimal min, decimal max, decimal value, decimal increment)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Increment = increment,
                Width = 70
            };
        }

        private static ComboBox MakeCombo(params string[] values)
        {
            ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(values);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        private static void SelectCombo(ComboBox combo, string value)
        {
            int index = combo.Items.IndexOf(value);
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        private static string ComboValue(ComboBox combo)
        {
            return Convert.ToString(combo.SelectedItem);
        }

        private static decimal Clamp(long value, NumericUpDown control)
        {
            if (value < control.Minimum) return control.Minimum;
            if (value > control.Maximum) return control.Maximum;
            return value;
        }
    }

    internal sealed class KeyCaptureDialog : Form
    {
        internal Keys SelectedKey { get; private set; }

        internal KeyCaptureDialog()
        {
            Text = "捕获按键";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 105);
            Font = new Font("Microsoft YaHei UI", 10F);
            Controls.Add(new Label
            {
                Text = "请按下要使用的键。Backspace 清空，Esc 取消。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Back)
            {
                SelectedKey = Keys.None;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            if (key == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (key != Keys.None && key != Keys.ControlKey && key != Keys.ShiftKey && key != Keys.Menu)
            {
                SelectedKey = key;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
