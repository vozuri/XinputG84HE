using System.IO;
using System.Text.Json;
using XinputG84HE.Models;

namespace XinputG84HE.Services;

public sealed class ProfileStore
{
    public const string AutosaveName = "Autosave";
    private readonly string _presetsFolder;
    private readonly string _stateFilePath;
    private readonly string _autosaveFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStore(string? root = null)
    {
        root ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XinputG84HE");
        _presetsFolder = Path.Combine(root, "Presets");
        _stateFilePath = Path.Combine(root, "state.json");
        _autosaveFilePath = Path.Combine(root, "autosave.json");
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
        if (profile.IsAutosave)
        {
            // Replace in one step so an interrupted write cannot truncate the slot.
            string temporary = _autosaveFilePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _autosaveFilePath, overwrite: true);
        }
        else File.WriteAllText(PathFor(profile.Name), json);
    }

    public Profile LoadAutosave()
    {
        ProfileDto? dto = null;
        if (File.Exists(_autosaveFilePath))
        {
            try { dto = JsonSerializer.Deserialize<ProfileDto>(File.ReadAllText(_autosaveFilePath), JsonOptions); }
            catch { /* A missing or unreadable working slot starts empty. */ }
        }
        var profile = FromDtoCore(dto ?? new ProfileDto { UseRadialDeadzone = false }, isAutosave: true);
        if (dto is null) Save(profile);
        return profile;
    }

    /// <summary>Only named presets participate in next/previous hotkeys.</summary>
    public string? GetAdjacentPresetName(string? currentName, int direction)
    {
        var names = ListPresetNames();
        if (names.Count == 0) return null;
        int index = names.FindIndex(name => string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return direction < 0 ? names[^1] : names[0];
        return names[(index + (direction < 0 ? -1 : 1) + names.Count) % names.Count];
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

    /// <summary>Preserves the last named preset or Autosave; new installations start in Autosave.</summary>
    public Profile LoadLastOrDefault()
    {
        var autosave = LoadAutosave();
        var state = ReadState();
        if (state?.LastActiveIsAutosave == true) return autosave;
        string? lastName = state?.LastActive;
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

        return autosave;
    }

    public void RememberLastActive(string name, bool isAutosave = false)
    {
        try
        {
            StateDto state = ReadState() ?? new StateDto();
            state.LastActive = name;
            state.LastActiveIsAutosave = isAutosave;
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
        SnapStickEnabled = profile.SnapStickEnabled,
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

    private static Profile FromDto(ProfileDto dto) => FromDtoCore(dto, false);

    private static Profile FromDtoCore(ProfileDto dto, bool isAutosave)
    {
        var profile = new Profile
        {
            Name = isAutosave ? AutosaveName : dto.Name,
            IsAutosave = isAutosave,
            Deadzone = dto.Deadzone,
            DigitalThreshold = dto.DigitalThreshold <= 0 ? 0.50 : dto.DigitalThreshold,
            BlockMappedKeys = dto.BlockMappedKeys,
            UseRadialDeadzone = dto.UseRadialDeadzone,
            SnapStickEnabled = dto.SnapStickEnabled,
            Locked = !isAutosave && dto.Locked,
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
        public bool SnapStickEnabled { get; set; }
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
        public bool LastActiveIsAutosave { get; set; }
        public Dictionary<string, int[]>? Hotkeys { get; set; }
        public bool? AntiGhostEnabled { get; set; }
        public double? AntiGhostSeconds { get; set; }
        public int? ReportRate { get; set; }
    }
}
