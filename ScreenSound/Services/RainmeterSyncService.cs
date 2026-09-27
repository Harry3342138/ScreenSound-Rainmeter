using ScreenSound.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenSound.Services;

// Called by the existing mapping-change path. No timer, file watcher, or worker process.
public sealed class RainmeterSyncService
{
    public sealed class Binding
    {
        public string MonitorDeviceName { get; set; } = "";
        public string Config { get; set; } = "";
        public string SkinFile { get; set; } = "";
        public string Measure { get; set; } = "MeasureAudioOutput";
        public bool LimitRefreshRate { get; set; } = true;
    }

    public sealed class Options
    {
        public bool Enabled { get; set; }
        public string RainmeterExe { get; set; } = "";
        public List<Binding> Bindings { get; set; } = new();
    }

    private readonly Options _options;
    private readonly Action<string> _refresh;
    private readonly Dictionary<string, string?> _applied = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRefresh = new(StringComparer.OrdinalIgnoreCase);
    public Options Configuration => _options;
    public string? LoadError { get; private set; }
    public static string ConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenSound", "RainmeterBindings.json");

    public RainmeterSyncService(Options options, Action<string>? refresh = null)
    {
        _options = options;
        _refresh = refresh ?? RefreshSkin;
    }

    public static RainmeterSyncService Load(string path)
    {
        try
        {
            var options = File.Exists(path)
                ? JsonSerializer.Deserialize<Options>(File.ReadAllText(path)) ?? new Options()
                : new Options();
            if (options.Bindings == null) throw new InvalidDataException("Bindings must be a list.");
            if (options.Bindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.MonitorDeviceName)
                || string.IsNullOrWhiteSpace(b.SkinFile) || string.IsNullOrWhiteSpace(b.Config)
                || string.IsNullOrWhiteSpace(b.Measure)))
                throw new InvalidDataException("Each binding needs a monitor, skin path, configuration and measure.");
            return new RainmeterSyncService(options);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Rainmeter bindings: {ex.Message}");
            return new RainmeterSyncService(new Options()) { LoadError = ex.Message };
        }
    }

    // Returns errors for diagnostics; a skin failure must not interrupt audio routing.
    public List<string> Sync(IEnumerable<MonitorAudioMapping> mappings)
    {
        if (!_options.Enabled) return new List<string>();
        var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
            if (!string.IsNullOrWhiteSpace(mapping.AudioDeviceId))
                devices[mapping.MonitorDeviceName] = mapping.AudioDeviceId;

        var errors = new List<string>();
        foreach (var binding in _options.Bindings)
        {
            devices.TryGetValue(binding.MonitorDeviceName, out var device);
            if (_applied.TryGetValue(binding.SkinFile, out var applied) && applied == device)
                continue;

            try
            {
                if (device != null && !Regex.IsMatch(device, @"^\{0\.0\.0\.00000000\}\.\{[0-9a-fA-F-]{36}\}$"))
                    throw new InvalidDataException("Invalid audio endpoint ID.");
                ValidateBinding(binding);

                byte[] original = File.ReadAllBytes(binding.SkinFile);
                using var reader = new StreamReader(new MemoryStream(original), Encoding.Latin1, true);
                string before = reader.ReadToEnd();
                var encoding = reader.CurrentEncoding;
                string after = before;
                if (!string.Equals(ReadOption(before, binding.Measure, "Plugin"), "AudioLevel", StringComparison.OrdinalIgnoreCase)
                    || ReadOption(before, binding.Measure, "Parent") != null)
                    throw new InvalidDataException("Select a parent AudioLevel measure.");
                if (device != null)
                    after = SetOption(after, binding.Measure, "ID", device);
                // Refresh clears old FFT values; an unavailable/unassigned device stays quiet.
                after = SetOption(after, binding.Measure, "Disabled", device == null ? "1" : "0");
                if (binding.LimitRefreshRate && int.TryParse(ReadOption(after, "Rainmeter", "Update"), out int interval)
                    && interval >= 0 && interval < 33)
                    after = SetOption(after, "Rainmeter", "Update", "33");

                if (before != after)
                {
                    string backup = binding.SkinFile + ".screensound.bak";
                    if (!File.Exists(backup)) File.Copy(binding.SkinFile, backup, false);
                    byte[] payload = encoding.GetBytes(after);
                    byte[] preamble = encoding.GetPreamble();
                    if (preamble.Length > 0 && original.AsSpan().StartsWith(preamble))
                        payload = preamble.Concat(payload).ToArray();
                    // Replace atomically, keeping the last good file if writing fails.
                    string temporary = binding.SkinFile + ".screensound-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, payload);
                        File.Move(temporary, binding.SkinFile, true);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                    _pendingRefresh.Add(binding.Config);
                }
                if (_pendingRefresh.Contains(binding.Config))
                {
                    _refresh(binding.Config);
                    _pendingRefresh.Remove(binding.Config);
                }
                _applied[binding.SkinFile] = device;
            }
            catch (Exception ex)
            {
                errors.Add($"{binding.Config}: {ex.Message}");
            }
        }
        return errors;
    }

    public static void ValidateBinding(Binding binding)
    {
        if (!Path.IsPathFullyQualified(binding.SkinFile) || !binding.SkinFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(binding.MonitorDeviceName) || string.IsNullOrWhiteSpace(binding.Measure)
            || string.IsNullOrWhiteSpace(binding.Config) || binding.Config.StartsWith('!')
            || binding.Config.IndexOfAny(new[] { '"', '[', ']', '\r', '\n' }) >= 0)
            throw new InvalidDataException("Choose a monitor, skin INI, configuration name and parent AudioLevel measure.");
        string content = ReadSkin(binding.SkinFile);
        if (!GetParentMeasures(content).Contains(binding.Measure, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The skin has no matching parent AudioLevel measure.");
    }

    public static string ReadSkin(string path)
    {
        using var reader = new StreamReader(path, Encoding.Latin1, true);
        return reader.ReadToEnd();
    }

    public static List<string> GetParentMeasures(string content) => Regex.Matches(content, @"^[ \t]*\[([^\r\n\]]+)\][ \t]*\r?$", RegexOptions.Multiline)
        .Select(m => m.Groups[1].Value)
        .Where(section => string.Equals(ReadOption(content, section, "Plugin"), "AudioLevel", StringComparison.OrdinalIgnoreCase)
            && ReadOption(content, section, "Parent") == null).ToList();

    public static string? ReadOption(string text, string section, string key)
    {
        var header = Regex.Match(text, @"^[ \t]*\[" + Regex.Escape(section) + @"\][ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!header.Success) return null;
        int start = header.Index + header.Length;
        var next = Regex.Match(text[start..], @"^[ \t]*\[[^\r\n]+\]", RegexOptions.Multiline);
        string body = text[start..(next.Success ? start + next.Index : text.Length)];
        var value = Regex.Match(body, @"^[ \t]*" + Regex.Escape(key) + @"[ \t]*=([^\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return value.Success ? value.Groups[1].Value.Trim() : null;
    }

    public static void Save(string path, Options options)
    {
        if (options.Enabled && !File.Exists(options.RainmeterExe))
            throw new InvalidDataException("Choose Rainmeter.exe before enabling synchronization.");
        var duplicate = options.Bindings.Where(b => !string.IsNullOrWhiteSpace(b.SkinFile))
            .GroupBy(b => Path.GetFullPath(b.SkinFile), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidDataException("Each skin file can be linked to only one monitor.");
        if (options.Enabled) foreach (var binding in options.Bindings) ValidateBinding(binding);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    public List<string> RestoreBackups()
    {
        var errors = new List<string>();
        foreach (var binding in _options.Bindings)
        {
            try
            {
                string backup = binding.SkinFile + ".screensound.bak";
                if (!File.Exists(backup)) continue;
                File.Copy(backup, binding.SkinFile, true);
                _refresh(binding.Config);
            }
            catch (Exception ex) { errors.Add($"{binding.Config}: {ex.Message}"); }
        }
        _applied.Clear();
        return errors;
    }

    private static string SetOption(string text, string section, string key, string value)
    {
        var headers = Regex.Matches(text, @"^[ \t]*\[" + Regex.Escape(section) + @"\][ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (headers.Count != 1) throw new InvalidDataException($"Expected one [{section}] section.");
        int start = headers[0].Index + headers[0].Length;
        var next = Regex.Match(text[start..], @"^[ \t]*\[[^\r\n]+\]", RegexOptions.Multiline);
        int end = next.Success ? start + next.Index : text.Length;
        string body = text[start..end];
        var option = new Regex(@"^([ \t]*" + Regex.Escape(key) + @"[ \t]*=)[^\r\n]*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (option.Matches(body).Count > 1) throw new InvalidDataException($"Duplicate {key} option.");
        body = option.IsMatch(body)
            ? option.Replace(body, m => m.Groups[1].Value + value)
            : key + "=" + value + (text.Contains("\r\n") ? "\r\n" : "\n") + body;
        return text[..start] + body + text[end..];
    }

    private void RefreshSkin(string config)
    {
        var running = Process.GetProcessesByName("Rainmeter");
        bool isRunning = running.Length > 0;
        foreach (var process in running) process.Dispose();
        if (!isRunning) return; // The persisted ID is picked up on Rainmeter's next start.
        var start = new ProcessStartInfo(_options.RainmeterExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("!Refresh");
        start.ArgumentList.Add(config);
        using var command = Process.Start(start);
    }
}
