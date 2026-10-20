using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Actions.Switches;

public static class SwitchCatalog
{
    public const string Services = @"HKLM\SYSTEM\CurrentControlSet\Services";

    public static readonly SwitchSpec TelemetryServices = new("telemetry-services",
        "Connected User Experiences and Telemetry (DiagTrack, dmwappushservice)",
        "The services that send usage data to Microsoft start with Windows.",
        new[]
        {
            new SwitchValue($@"{Services}\DiagTrack", "Start", 4, AbsentIsOff: true),
            new SwitchValue($@"{Services}\dmwappushservice", "Start", 4, AbsentIsOff: true),
        });

    public static readonly SwitchSpec ErrorReporting = new("error-reporting-off",
        "Windows Error Reporting",
        "Windows sends crash and error reports to Microsoft.",
        new[] { new SwitchValue(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1) });

    public static readonly SwitchSpec Feedback = new("feedback-off",
        "Feedback frequency",
        "Windows asks how you like it and sends the answers to Microsoft.",
        new[]
        {
            new SwitchValue(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
            new SwitchValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec Copilot = new("copilot-off",
        "Windows Copilot",
        "Copilot is allowed and its button can sit on the taskbar.",
        new[]
        {
            new SwitchValue(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
            new SwitchValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, AbsentIsOff: true),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec Recall = new("recall-off",
        "Recall",
        "Recall is allowed to save snapshots of the screen.",
        new[] { new SwitchValue(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1) },
        WriteEffect.Unread);

    public static readonly SwitchSpec DeliveryPeers = new("delivery-peers-off",
        "Delivery Optimization",
        "Windows can upload updates to other PCs on the internet.",
        new[]
        {
            new SwitchValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 1,
                AlsoOff: new[] { 0, 99, 100 }),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec GameRecording = new("game-recording",
        "Game DVR background recording",
        "Windows records gameplay in the background.",
        new[] { new SwitchValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0, AbsentIsOff: true) });

    public static readonly SwitchSpec GpuScheduling = new("gpu-scheduling",
        "Hardware-accelerated GPU scheduling",
        "Windows recorded GPU scheduling as off on this machine.",
        new[] { new SwitchValue(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, AbsentIsOff: true) });

    public static readonly SwitchSpec MemoryIntegrity = new("memory-integrity",
        "Memory integrity",
        "Memory integrity is on.",
        new[] { new SwitchValue(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, AbsentIsOff: true) });

    /// Every spec that is a plain SwitchRule. TelemetryServices is not: its
    /// rule also stops the services (TelemetryServicesRule).
    public static IReadOnlyList<SwitchSpec> Plain { get; } = new[]
    {
        ErrorReporting, Feedback, Copilot, Recall, DeliveryPeers,
        GameRecording, GpuScheduling, MemoryIntegrity,
    };
}
