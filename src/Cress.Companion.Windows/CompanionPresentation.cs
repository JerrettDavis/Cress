using Cress.Companion;

namespace Cress.Companion.Windows;

internal enum CompanionVisualTone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger
}

internal sealed record CompanionListItemPresentation(
    string Title,
    string Subtitle,
    string Badge,
    CompanionVisualTone Tone);

internal sealed record CompanionSelectionPresentation(
    string Title,
    string Meta,
    string Summary,
    string Activity,
    string PreviewHint,
    CompanionVisualTone Tone);

internal static class CompanionPresentation
{
    public static string BuildManagerStatus(CompanionServiceSnapshot snapshot, int attachableTargetCount)
    {
        if (snapshot.Sessions.Count == 0)
        {
            return attachableTargetCount == 0
                ? "The companion is online. Open a desktop app to make it available for recording."
                : $"The companion is ready. {attachableTargetCount} attachable window(s) can be started from this workspace.";
        }

        var activeCount = snapshot.Sessions.Count(session => session.Status is CompanionSessionStatus.Recording or CompanionSessionStatus.Paused);
        return activeCount == 1
            ? "1 session is live. Keep the overlay nearby and use Studio only when you need deeper authoring or results."
            : $"{activeCount} sessions are live. The manager keeps each desktop recording compact, visible, and easy to control.";
    }

    public static CompanionListItemPresentation DescribeTarget(CompanionTargetInfo target)
        => new(
            DefaultText(target.WindowTitle, "Untitled window"),
            $"{DefaultText(target.ProcessName, "Unknown process")}  •  PID {target.ProcessId}",
            target.IsAttachable ? "Ready" : "Blocked",
            target.IsAttachable ? CompanionVisualTone.Accent : CompanionVisualTone.Danger);

    public static CompanionListItemPresentation DescribeSession(CompanionSessionSnapshot session)
        => new(
            DefaultText(session.WindowTitle, session.ProcessName),
            $"{DefaultText(session.ProcessName, "Unknown process")}  •  {HumanizeStatus(session.Status)}  •  {session.CapturedEventCount} event(s)",
            session.OverlayEnabled ? "Overlay on" : "Overlay off",
            ToneForStatus(session.Status));

    public static CompanionSelectionPresentation DescribeTargetSelection(CompanionTargetInfo target)
        => new(
            DefaultText(target.WindowTitle, target.ProcessName),
            $"{DefaultText(target.ProcessName, "Unknown process")}  •  PID {target.ProcessId}",
            target.IsAttachable
                ? "Start one focused session when you are ready to record. The overlay starts enabled so controls stay near the target window."
                : "This window is visible, but Windows did not expose it as attachable from the current process context.",
            target.IsAttachable
                ? "Use the primary action to begin recording. Preview and live event summaries appear as soon as the session starts."
                : "Run the companion with the same elevation level as the target app, then refresh the list.",
            target.IsAttachable
                ? "Preview becomes available after recording starts."
                : "No preview is available until the session is running.",
            target.IsAttachable ? CompanionVisualTone.Accent : CompanionVisualTone.Danger);

    public static CompanionSelectionPresentation DescribeSessionSelection(CompanionSessionSnapshot session, Uri baseAddress)
    {
        var lastEvent = DefaultText(session.LastEventSummary, "Waiting for the next UI event.");
        var lastStep = DefaultText(session.LastStepSummary, "No inferred step has been produced yet.");
        var overlayText = session.OverlayEnabled ? "Overlay anchored near the target titlebar." : "Overlay hidden for this session.";

        return new CompanionSelectionPresentation(
            DefaultText(session.WindowTitle, session.ProcessName),
            $"{DefaultText(session.ProcessName, "Unknown process")}  •  {HumanizeStatus(session.Status)}  •  {session.CapturedEventCount} event(s)  •  PID {session.ProcessId}",
            $"Latest event: {lastEvent}{Environment.NewLine}Latest step: {lastStep}",
            $"{overlayText} Bridge: {new Uri(baseAddress, "api/companion")}",
            string.IsNullOrWhiteSpace(session.PreviewImageDataUrl)
                ? "Preview is not available yet. It appears automatically after the companion captures the first window frame."
                : "Preview is live. Use it as a quick confidence check without switching back to Studio.",
            ToneForStatus(session.Status));
    }

    public static string BuildOverlayTitle(CompanionSessionSnapshot session)
        => DefaultText(session.WindowTitle, session.ProcessName);

    public static string BuildOverlaySummary(CompanionSessionSnapshot session)
    {
        var latestStep = DefaultText(session.LastStepSummary, "Listening for the next inferred step.");
        return $"{HumanizeStatus(session.Status)}  •  {session.CapturedEventCount} event(s){Environment.NewLine}{latestStep}";
    }

    private static CompanionVisualTone ToneForStatus(CompanionSessionStatus status)
        => status switch
        {
            CompanionSessionStatus.Recording => CompanionVisualTone.Success,
            CompanionSessionStatus.Paused => CompanionVisualTone.Warning,
            CompanionSessionStatus.Stopped => CompanionVisualTone.Neutral,
            _ => CompanionVisualTone.Accent
        };

    private static string HumanizeStatus(CompanionSessionStatus status)
        => status switch
        {
            CompanionSessionStatus.Recording => "Recording",
            CompanionSessionStatus.Paused => "Paused",
            CompanionSessionStatus.Stopped => "Stopped",
            _ => status.ToString()
        };

    private static string DefaultText(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
