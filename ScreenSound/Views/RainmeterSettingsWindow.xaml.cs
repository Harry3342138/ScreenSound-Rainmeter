using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using ScreenSound.Models;
using ScreenSound.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ScreenSound.Views;

public partial class RainmeterBindingEditor : ObservableObject
{
    public List<MonitorInfo> Monitors { get; }
    public ObservableCollection<string> Measures { get; } = new();
    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private string _skinFile = "";
    [ObservableProperty] private string _config = "";
    [ObservableProperty] private string _measure = "";
    [ObservableProperty] private bool _limitRefreshRate = true;

    public RainmeterBindingEditor(List<MonitorInfo> monitors, RainmeterSyncService.Binding? binding = null)
    {
        Monitors = new List<MonitorInfo>(monitors);
        if (binding == null) { SelectedMonitor = Monitors.FirstOrDefault(); return; }
        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == binding.MonitorDeviceName);
        if (SelectedMonitor == null)
        {
            SelectedMonitor = new MonitorInfo { DeviceName = binding.MonitorDeviceName, FriendlyName = binding.MonitorDeviceName + " (disconnected)" };
            Monitors.Add(SelectedMonitor);
        }
        SkinFile = binding.SkinFile;
        Config = binding.Config;
        LimitRefreshRate = binding.LimitRefreshRate;
        try { LoadMeasures(); } catch { /* Keep disconnected or moved skins editable. */ }
        if (!Measures.Contains(binding.Measure)) Measures.Add(binding.Measure);
        Measure = binding.Measure;
    }

    public void LoadMeasures()
    {
        Measures.Clear();
        foreach (string name in RainmeterSyncService.GetParentMeasures(RainmeterSyncService.ReadSkin(SkinFile))) Measures.Add(name);
        Measure = Measures.FirstOrDefault() ?? "";
    }

    public RainmeterSyncService.Binding ToBinding() => new()
    {
        MonitorDeviceName = SelectedMonitor?.DeviceName ?? "", SkinFile = SkinFile,
        Config = Config.Trim(), Measure = Measure, LimitRefreshRate = LimitRefreshRate
    };
}

public partial class RainmeterSettingsWindow : Wpf.Ui.Controls.UiWindow
{
    private readonly List<MonitorInfo> _monitors;
    private readonly Action<RainmeterSyncService.Options> _save;
    private readonly Func<List<string>> _restore;
    public ObservableCollection<RainmeterBindingEditor> Bindings { get; } = new();

    public RainmeterSettingsWindow(List<MonitorInfo> monitors, RainmeterSyncService.Options options,
        Action<RainmeterSyncService.Options> save, Func<List<string>> restore)
    {
        InitializeComponent();
        _monitors = monitors; _save = save; _restore = restore;
        EnableSync.IsChecked = options.Enabled;
        RainmeterPath.Text = string.IsNullOrWhiteSpace(options.RainmeterExe) ? FindRainmeter() : options.RainmeterExe;
        foreach (var binding in options.Bindings) Bindings.Add(new RainmeterBindingEditor(monitors, binding));
        BindingItems.ItemsSource = Bindings;
    }

    private static string FindRainmeter()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var file = Path.Combine(Environment.GetFolderPath(folder), "Rainmeter", "Rainmeter.exe");
            if (File.Exists(file)) return file;
        }
        return "";
    }

    private static string SkinRoot()
    {
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rainmeter", "Rainmeter.ini");
        if (File.Exists(settings))
        {
            var root = RainmeterSyncService.ReadOption(RainmeterSyncService.ReadSkin(settings), "Rainmeter", "SkinPath");
            if (!string.IsNullOrWhiteSpace(root)) return Environment.ExpandEnvironmentVariables(root.Trim('"'));
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rainmeter", "Skins");
    }

    private void BrowseRainmeter_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose Rainmeter.exe", Filter = "Rainmeter application|Rainmeter.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) RainmeterPath.Text = dialog.FileName;
    }

    private void AddBinding_Click(object sender, RoutedEventArgs e) => Bindings.Add(new RainmeterBindingEditor(_monitors));
    private void RemoveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RainmeterBindingEditor row }) Bindings.Remove(row);
    }

    private void BrowseSkin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RainmeterBindingEditor row }) return;
        try
        {
            string root = SkinRoot();
            var dialog = new OpenFileDialog { Title = "Choose the visualizer's INI file", Filter = "Rainmeter skins|*.ini", CheckFileExists = true };
            if (Directory.Exists(root)) dialog.InitialDirectory = root;
            if (dialog.ShowDialog(this) != true) return;
            row.SkinFile = dialog.FileName;
            row.LoadMeasures();
            string relative = Path.GetRelativePath(root, Path.GetDirectoryName(dialog.FileName)!);
            if (!relative.StartsWith("..") && !Path.IsPathRooted(relative) && relative != ".") row.Config = relative;
            if (row.Measures.Count == 0)
                MessageBox.Show(this, "This file has no parent AudioLevel measure. Select the visualizer INI that defines Plugin=AudioLevel without Parent=.", "Choose another skin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = new RainmeterSyncService.Options { Enabled = EnableSync.IsChecked == true, RainmeterExe = RainmeterPath.Text.Trim(), Bindings = Bindings.Select(b => b.ToBinding()).ToList() };
            if (options.Enabled && options.Bindings.Count == 0) throw new InvalidDataException("Add a visualizer before enabling synchronization.");
            _save(options);
            DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restore the saved skins from their first backups and disable synchronization? This replaces the entire skin files, including any later manual edits. Unsaved changes in this dialog are not included.",
            "Restore original skins", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var errors = _restore();
            if (errors.Count > 0) { ShowError(string.Join(Environment.NewLine, errors)); EnableSync.IsChecked = false; return; }
            DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "Rainmeter settings", MessageBoxButton.OK, MessageBoxImage.Warning);
}
