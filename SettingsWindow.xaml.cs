using System;
using System.Windows;

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

        // 控件都建好、值也灌完了，从这里开始用户操作才算数
        _loading = false;
        UpdateValueLabels();
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
