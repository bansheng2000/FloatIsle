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

        // 控件都建好、值也灌完了，从这里开始用户操作才算数
        _loading = false;
        RebuildModuleRows();
        UpdateValueLabels();
        UpdateLedgerText();
        UpdateTasksStatus();
        PlacementText.Text = owner.PlacementDescription();
        AutoStartText.Text = AutoStart.IsEnabled()
            ? "开机自启：已开启（真源是注册表，在托盘菜单里切换）"
            : "开机自启：未开启（在托盘菜单里切换）";

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

    // ---------- 联动（任务面板占位） ----------

    private void TasksEndpoint_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        string text = TasksEndpointBox.Text.Trim();
        // 清空/超长算「正在输入」，先不落盘：Normalize 会把空串改成默认值，边打边写会打架
        if (text.Length is 0 or > 64 || text == _owner.Settings.TasksEndpoint)
        {
            return;
        }

        _owner.UpdateSettings(s => s.TasksEndpoint = text);
        UpdateTasksStatus();
    }

    private void UpdateTasksStatus()
    {
        TasksStatusText.Text = _owner.TasksConnectedNow
            ? $"状态：已连接（地址 {_owner.Settings.TasksEndpoint}）"
            : $"状态：未连接 · 监听端还没接上，任务面板暂时不显示（地址 {_owner.Settings.TasksEndpoint}）";
    }

    private void UpdateLedgerText()
    {
        string watching = _owner.UsageWatching ? "监视中" : "无法监视（目录不存在或无权限）";
        string snapshot = _owner.Usage is null ? "无数据" : UsageLedger.UsageLine(_owner.Usage);
        LedgerText.Text = $"账本：{_owner.UsagePath}\n{snapshot} · {watching}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
