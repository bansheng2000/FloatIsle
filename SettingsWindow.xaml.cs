using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// UseWindowsForms 的隐式 using 带进了同名的 WinForms 控件，钉死用 WPF 的
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;

namespace FloatIsle;

/// <summary>
/// 普通 WPF 设置窗口：每改一项立即生效并落盘（MainWindow.UpdateSettings 是唯一入口）。
/// 自启这里只读回显——真源是注册表，面板不写第二份。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;

    // 初始就是 true：InitializeComponent 里设置 Slider.Minimum 会强制夹一次 Value 并触发
    // ValueChanged，那一刻后面的控件还没创建（实测 NRE），所以加载期间一律不处理。
    private bool _loading = true;

    // 重建模块行时会新建 CheckBox 并赋初值，同样要挡住它们的 Checked 事件
    private bool _rebuildingRows;

    // 联动状态每秒刷新一次；只在窗口开着时跑（Closed 里停掉）
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    internal SettingsWindow(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;

        // 高缩放 + 小屏时窗口不许长到工作区外：高度封顶，超出的内容进 ScrollViewer（不裁字、不越界）
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height * 0.9);

        AppSettings s = owner.Settings;
        OpacitySlider.Value = s.Opacity;
        ClickThroughCheck.IsChecked = s.ClickThrough;
        MediaCheck.IsChecked = s.MediaEnabled;
        FullscreenCheck.IsChecked = s.FullscreenSuppress;
        HoverSlider.Value = s.HoverDelayMs;

        // 「收起时主行」下拉：value 用模块 id 存在 Tag 里，显示名走 IslandModuleIds.Label
        CollapsedLineCombo.Items.Add(new ComboBoxItem { Content = "媒体标题", Tag = IslandModuleIds.Media });
        CollapsedLineCombo.Items.Add(new ComboBoxItem { Content = "今日用量", Tag = IslandModuleIds.Usage });
        CollapsedLineCombo.Items.Add(new ComboBoxItem { Content = "任务数", Tag = IslandModuleIds.Tasks });
        CollapsedLineCombo.SelectedIndex = s.CollapsedLine switch
        {
            IslandModuleIds.Usage => 1,
            IslandModuleIds.Tasks => 2,
            _ => 0
        };
        TasksEndpointBox.Text = s.TasksEndpoint;
        TasksJumpBox.Text = s.TasksJumpEndpoint;
        GlowColorBox.Text = s.GlowColor;

        // 形态下拉：value 存 Tag（capsule/top），显示名给人看
        ModeCombo.Items.Add(new ComboBoxItem { Content = "胶囊（顶部居中，悬停展开）", Tag = AppSettings.ModeCapsule });
        ModeCombo.Items.Add(new ComboBoxItem { Content = "贴顶工作台（胶囊常驻，悬停向下展开）", Tag = AppSettings.ModeTop });
        ModeCombo.SelectedIndex = s.Mode == AppSettings.ModeTop ? 1 : 0;

        // 控件都建好、值也灌完了，从这里开始用户操作才算数
        _loading = false;
        RebuildModuleRows();
        UpdateValueLabels();
        UpdateLedgerText();
        UpdateTasksStatus();
        UpdateGlowHint();
        PlacementText.Text = owner.PlacementDescription();
        AutoStartText.Text = AutoStart.IsEnabled()
            ? "开机自启：已开启（真源是注册表，在托盘菜单里切换）"
            : "开机自启：未开启（在托盘菜单里切换）";

        // 联动状态每秒刷新（「最后心跳 N 秒前」会变）；只在设置窗口开着时跑，关掉就停，
        // 岛上的空闲 CPU 不受影响（面板不是常驻窗口）
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateTasksStatus();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();

        if (owner.SettingsBackupPath is string backup)
        {
            LoadWarning.Text = $"上次读取 settings.json 失败，已用默认值启动；坏文件备份在：{backup}";
            LoadWarning.Visibility = Visibility.Visible;
        }
    }

    private void UpdateValueLabels()
    {
        OpacityValue.Text = $"{OpacitySlider.Value:P0}";
        HoverValue.Text = HoverSlider.Value <= 0
            ? "立即展开"
            : $"{HoverSlider.Value:F0} 毫秒后展开";
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }
        UpdateValueLabels();
        double value = Math.Round(e.NewValue, 2);
        _owner.UpdateSettings(s => s.Opacity = value);
    }

    private void Hover_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }
        UpdateValueLabels();
        int ms = (int)Math.Round(e.NewValue);
        _owner.UpdateSettings(s => s.HoverDelayMs = ms);
    }

    /// <summary>形态切换立即生效（不用重启）：胶囊 ↔ 贴顶工作台。</summary>
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeCombo.SelectedItem is not ComboBoxItem { Tag: string mode })
        {
            return;
        }
        _owner.UpdateSettings(s => s.Mode = mode);
        PlacementText.Text = _owner.PlacementDescription();
    }

    private void ClickThrough_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        bool on = ClickThroughCheck.IsChecked == true;
        _owner.UpdateSettings(s => s.ClickThrough = on);
    }

    private void Media_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        bool on = MediaCheck.IsChecked == true;
        _owner.UpdateSettings(s => s.MediaEnabled = on);
    }

    private void Fullscreen_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        bool on = FullscreenCheck.IsChecked == true;
        _owner.UpdateSettings(s => s.FullscreenSuppress = on);
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        _owner.ResetPlacement();
        PlacementText.Text = _owner.PlacementDescription();
    }

    // ---------- 岛内容：每个模块一行，显示开关 + 上移 + 下移 ----------

    /// <summary>
    /// 每次顺序变化都整块重建。行很少（4 行），重建比维护控件状态简单，也不会出现
    /// 「按钮 Tag 和数组下标对不上」这类错位 bug。
    /// </summary>
    private void RebuildModuleRows()
    {
        _rebuildingRows = true;
        try
        {
            ModulesPanel.Children.Clear();
            var modules = _owner.Settings.Modules;
            for (int i = 0; i < modules.Count; i++)
            {
                IslandModule module = modules[i];

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var check = new CheckBox
                {
                    Content = IslandModuleIds.Label(module.Id),
                    IsChecked = module.Visible,
                    Tag = module.Id,
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Center
                };
                check.Checked += ModuleVisible_Changed;
                check.Unchecked += ModuleVisible_Changed;
                row.Children.Add(check);

                var up = new Button
                {
                    Content = "上移",
                    Tag = module.Id,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    IsEnabled = i > 0
                };
                up.Click += MoveUp_Click;
                row.Children.Add(up);

                var down = new Button
                {
                    Content = "下移",
                    Tag = module.Id,
                    Padding = new Thickness(8, 2, 8, 2),
                    IsEnabled = i < modules.Count - 1
                };
                down.Click += MoveDown_Click;
                row.Children.Add(down);

                ModulesPanel.Children.Add(row);
            }
        }
        finally
        {
            _rebuildingRows = false;
        }
    }

    private void ModuleVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _rebuildingRows || sender is not CheckBox { Tag: string id } check)
        {
            return;
        }
        bool visible = check.IsChecked == true;
        _owner.UpdateSettings(s =>
        {
            IslandModule? module = s.Modules.FirstOrDefault(m => m.Id == id);
            if (module is not null)
            {
                module.Visible = visible;
            }
        });
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveModule(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveModule(sender, 1);

    private void MoveModule(object sender, int delta)
    {
        if (_loading || sender is not Button { Tag: string id })
        {
            return;
        }

        _owner.UpdateSettings(s =>
        {
            int index = s.Modules.FindIndex(m => m.Id == id);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= s.Modules.Count)
            {
                return;
            }
            (s.Modules[index], s.Modules[target]) = (s.Modules[target], s.Modules[index]);
        });

        RebuildModuleRows(); // 顺序变了，首尾两行的按钮可用状态也要跟着变
    }

    private void CollapsedLine_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CollapsedLineCombo.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            return;
        }
        _owner.UpdateSettings(s => s.CollapsedLine = id);
    }

    // ---------- 呼吸辉光颜色 ----------

    private void GlowColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        string text = GlowColorBox.Text.Trim();
        if (AppSettings.TryNormalizeColor(text, out string hex))
        {
            if (hex != _owner.Settings.GlowColor)
            {
                _owner.UpdateSettings(s => s.GlowColor = hex);
            }
        }
        else if (text.Length >= 7)
        {
            // 输入到 7 位还不合法就落默认值，免得岛上一半新色一半旧色
            _owner.UpdateSettings(s => s.GlowColor = AppSettings.DefaultGlowColor);
        }
        UpdateGlowHint();
    }

    private void UpdateGlowHint()
    {
        string text = GlowColorBox.Text.Trim();
        bool ok = AppSettings.TryNormalizeColor(text, out string hex);
        GlowColorHint.Text = ok ? hex : "非法颜色，已回默认 #4DA3FF";
        GlowColorHint.Foreground = ok
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
        if (ok && System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color color)
        {
            GlowColorSwatch.Fill = new System.Windows.Media.SolidColorBrush(color);
        }
    }

    // ---------- 联动（任务面板接收端） ----------

    private void TasksEndpoint_Changed(object sender, TextChangedEventArgs e) => ApplyEndpoint(TasksEndpointBox.Text, jump: false);

    private void TasksJump_Changed(object sender, TextChangedEventArgs e) => ApplyEndpoint(TasksJumpBox.Text, jump: true);

    private void ApplyEndpoint(string text, bool jump)
    {
        if (_loading)
        {
            return;
        }

        // 清空/超长算「正在输入」，先不落盘：Normalize 会把空串改成默认值，边打边写会打架
        text = text.Trim();
        if (text.Length is 0 or > 64 || text == (jump ? _owner.Settings.TasksJumpEndpoint : _owner.Settings.TasksEndpoint))
        {
            return;
        }

        _owner.UpdateSettings(s =>
        {
            if (jump)
            {
                s.TasksJumpEndpoint = text;
            }
            else
            {
                s.TasksEndpoint = text;
            }
        });
        UpdateTasksStatus();
    }

    private void UpdateTasksStatus()
    {
        // 收到的状态文案和岛上用的是同一份（TasksHub.StatusText），不在这里另写一套判定
        TasksStatusText.Text = $"状态：{_owner.TasksStatus}\n"
                               + $"接收 {_owner.Settings.TasksEndpoint} · 跳转 {_owner.Settings.TasksJumpEndpoint}";
    }

    private void UpdateLedgerText()
    {
        string watching = _owner.UsageWatching ? "监视中" : "无法监视（目录不存在或无权限）";
        string snapshot = _owner.Usage is null ? "无数据" : UsageLedger.UsageLine(_owner.Usage);
        LedgerText.Text = $"账本：{_owner.UsagePath}\n{snapshot} · {watching}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}



