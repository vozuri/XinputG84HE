using System.IO;
using System.Text.Json;
using XinputG84HE.Models;

namespace XinputG84HE.Services;

public sealed class ProfileStore
{
    private readonly string _presetsFolder;
    private readonly string _stateFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XinputG84HE");
        _presetsFolder = Path.Combine(root, "Presets");
        _stateFilePath = Path.Combine(root, "state.json");
        Directory.CreateDirectory(_presetsFolder);
    }

    public List<string> ListPresetNames()
    {
        if (!Directory.Exists(_presetsFolder))
        {
            return new List<string>();
        }

        return Directory.GetFiles(_presetsFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Profile? Load(string name)
    {
        string path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            ProfileDto? dto = JsonSerializer.Deserialize<ProfileDto>(json, JsonOptions);
            return dto is null ? null : FromDto(dto);
        }
        catch
        {
            return null;
        }
    }

    public void Save(Profile profile)
    {
        ProfileDto dto = ToDto(profile);
        string json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(PathFor(profile.Name), json);
    }

    public void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Rename(string oldName, string newName)
    {
        Profile? profile = Load(oldName);
        if (profile is null)
        {
            return;
        }
        Delete(oldName);
        profile.Name = newName;
        Save(profile);
    }

    /// <summary>Loads the last-active preset, or creates a fresh "Default" preset if none exist yet.</summary>
    public Profile LoadLastOrDefault()
    {
        string? lastName = ReadState()?.LastActive;
        if (lastName is not null)
        {
            Profile? loaded = Load(lastName);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        List<string> existing = ListPresetNames();
        if (existing.Count > 0)
        {
            Profile? first = Load(existing[0]);
            if (first is not null)
            {
                return first;
            }
        }

        var fresh = new Profile { Name = "Default" };
        Save(fresh);
        return fresh;
    }

    public void RememberLastActive(string name)
    {
        try
        {
            StateDto state = ReadState() ?? new StateDto();
            state.LastActive = name;
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    public (int modifiers, int virtualKey)? LoadHotkey(string name)
    {
        StateDto? state = ReadState();
        if (state?.Hotkeys is null || !state.Hotkeys.TryGetValue(name, out int[]? pair) || pair.Length != 2)
        {
            return null;
        }
        return (pair[0], pair[1]);
    }

    public void SaveHotkey(string name, int modifiers, int virtualKey)
    {
        try
        {
            StateDto state = ReadState() ?? new StateDto();
            state.Hotkeys ??= new Dictionary<string, int[]>();
            state.Hotkeys[name] = new[] { modifiers, virtualKey };
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    public (bool enabled, double seconds) LoadAntiGhostSettings()
    {
        StateDto? state = ReadState();
        bool enabled = state?.AntiGhostEnabled ?? false;
        double seconds = state?.AntiGhostSeconds ?? 2.5;
        return (enabled, seconds);
    }

    public void SaveAntiGhostSettings(bool enabled, double seconds)
    {
        try
        {
            StateDto state = ReadState() ?? new StateDto();
            state.AntiGhostEnabled = enabled;
            state.AntiGhostSeconds = seconds;
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    public int LoadReportRate()
    {
        StateDto? state = ReadState();
        return state?.ReportRate ?? 500;
    }

    public void SaveReportRate(int reportRate)
    {
        try
        {
            StateDto state = ReadState() ?? new StateDto();
            state.ReportRate = reportRate;
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    private StateDto? ReadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<StateDto>(File.ReadAllText(_stateFilePath));
        }
        catch
        {
            return null;
        }
    }

    private string PathFor(string name)
    {
        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_presetsFolder, safe + ".json");
    }

    private static ProfileDto ToDto(Profile profile) => new()
    {
        Name = profile.Name,
        Deadzone = profile.Deadzone,
        DigitalThreshold = profile.DigitalThreshold,
        BlockMappedKeys = profile.BlockMappedKeys,
        UseRadialDeadzone = profile.UseRadialDeadzone,
        Locked = profile.Locked,
        LeftStickCurve = profile.LeftStickCurve,
        RightStickCurve = profile.RightStickCurve,
        Bindings = profile.Bindings
            .Where(kv => kv.Value is not null)
            .Select(kv => new BindingDto
            {
                Input = kv.Key.ToString(),
                RawKeyId = kv.Value!.RawKeyId,
                VirtualKey = kv.Value.VirtualKey,
                DisplayName = kv.Value.DisplayName,
            })
            .ToList(),
    };

    private static Profile FromDto(ProfileDto dto)
    {
        var profile = new Profile
        {
            Name = dto.Name,
            Deadzone = dto.Deadzone,
            DigitalThreshold = dto.DigitalThreshold <= 0 ? 0.50 : dto.DigitalThreshold,
            BlockMappedKeys = dto.BlockMappedKeys,
            UseRadialDeadzone = dto.UseRadialDeadzone,
            Locked = dto.Locked,
        };
        
        if (dto.LeftStickCurve != null && dto.LeftStickCurve.Length == 11) profile.LeftStickCurve = dto.LeftStickCurve;
        if (dto.RightStickCurve != null && dto.RightStickCurve.Length == 11) profile.RightStickCurve = dto.RightStickCurve;

        foreach (BindingDto b in dto.Bindings)
        {
            if (Enum.TryParse<ControllerInput>(b.Input, out ControllerInput input))
            {
                profile.Bindings[input] = new KeyBinding(b.RawKeyId, b.VirtualKey, b.DisplayName);
            }
        }

        return profile;
    }

    private sealed class ProfileDto
    {
        public string Name { get; set; } = "Default";
        public List<BindingDto> Bindings { get; set; } = new();
        public double Deadzone { get; set; } = 0.05;
        public double DigitalThreshold { get; set; } = 0.50;
        public bool BlockMappedKeys { get; set; } = true;
        public bool UseRadialDeadzone { get; set; } = true;
        public bool Locked { get; set; }
        public double[]? LeftStickCurve { get; set; }
        public double[]? RightStickCurve { get; set; }
    }

    private sealed class BindingDto
    {
        public string Input { get; set; } = "";
        public ushort RawKeyId { get; set; }
        public int VirtualKey { get; set; }
        public string DisplayName { get; set; } = "";
    }

    private sealed class StateDto
    {
        public string? LastActive { get; set; }
        public Dictionary<string, int[]>? Hotkeys { get; set; }
        public bool? AntiGhostEnabled { get; set; }
        public double? AntiGhostSeconds { get; set; }
        public int? ReportRate { get; set; }
    }
}
