using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XinputG84HE.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string connectionTransport = "Offline";
    [ObservableProperty] private bool isDiagnosticsExpanded;
    [ObservableProperty] private string diagnosticsText = "Waiting for the first sample…";
    [ObservableProperty] private string diagnosticLog = "Ready.";
    private readonly Queue<string> _diagnosticMessages = new();
    private long _lastDiagnosticTick = Stopwatch.GetTimestamp();
    private long _lastDiagnosticReportCount;

    private void RecordDiagnosticMessage(string message)
    {
        StatusLog = message;
        _diagnosticMessages.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (_diagnosticMessages.Count > 80) _diagnosticMessages.Dequeue();
        DiagnosticLog = string.Join(Environment.NewLine, _diagnosticMessages);
    }

    private void UpdateDiagnostics()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastDiagnosticTick) / (double)Stopwatch.Frequency;
        if (elapsed < 1) return;

        ConnectionTransport = _hid.ConnectionDescription;
        var input = _hid.GetDiagnostics();
        var output = _gamepad.Diagnostics.Sample();
        double inputRate = (input.reports - _lastDiagnosticReportCount) / elapsed;
        _lastDiagnosticTick = now;
        _lastDiagnosticReportCount = input.reports;
        string age = double.IsFinite(input.lastReportAgeSeconds) ? $"{input.lastReportAgeSeconds:0.0} s ago" : "No reports yet";

        DiagnosticsText = string.Join(Environment.NewLine,
            $"Connection: {ConnectionTransport}",
            $"Open HID interfaces: {input.interfaces}",
            $"HID input: {inputRate:N0} reports/s",
            $"Last HID report: {age}",
            $"Active keys: {input.activeKeys}  ·  Suppressed: {_hid.GetSuppressedKeyIds().Count}",
            $"Virtual controller: {(_gamepad.IsRunning ? "Connected" : "Stopped")}",
            $"Output updates: {output.UpdatesPerSecond:N0}/s",
            $"Configured rate: {EmulationReportRate:N0} Hz",
            $"Update jitter: {output.JitterMs:0.000} ms",
            $"Longest update interval: {output.LongestIntervalMs:0.000} ms",
            $"Totals: {input.reports:N0} HID / {output.TotalUpdates:N0} updates",
            $"SnapStick+: {(SnapStickEnabled ? "On" : "Off")}");
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText($"XinputG84HE 1.12\n{DiagnosticsText}\n\n{DiagnosticLog}");
        }
        catch (ExternalException)
        {
            RecordDiagnosticMessage("Couldn't copy diagnostics because the clipboard is busy. Please try again.");
        }
    }
}
