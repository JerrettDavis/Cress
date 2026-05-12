using Cress.Companion;
using Cress.Companion.Windows;

namespace Cress.Companion.Core.Tests;

public sealed class CompanionPresentationTests
{
    [Fact]
    public void BuildManagerStatus_prefers_attachable_windows_when_idle()
    {
        var snapshot = new CompanionServiceSnapshot
        {
            IsAvailable = true,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sessions = []
        };

        var status = CompanionPresentation.BuildManagerStatus(snapshot, 2);

        Assert.Contains("2 attachable window(s)", status);
    }

    [Fact]
    public void DescribeTarget_uses_readable_window_first_copy()
    {
        var presentation = CompanionPresentation.DescribeTarget(new CompanionTargetInfo
        {
            ProcessId = 4040,
            ProcessName = "notepad",
            WindowTitle = "Release notes",
            IsAttachable = true
        });

        Assert.Equal("Release notes", presentation.Title);
        Assert.Contains("notepad", presentation.Subtitle);
        Assert.Equal("Ready", presentation.Badge);
        Assert.Equal(CompanionVisualTone.Accent, presentation.Tone);
    }

    [Fact]
    public void DescribeSession_surfaces_status_events_and_overlay_state()
    {
        var presentation = CompanionPresentation.DescribeSession(new CompanionSessionSnapshot
        {
            ProcessId = 1200,
            ProcessName = "calc",
            WindowTitle = "Calculator",
            Status = CompanionSessionStatus.Paused,
            CapturedEventCount = 12,
            OverlayEnabled = true
        });

        Assert.Equal("Calculator", presentation.Title);
        Assert.Contains("Paused", presentation.Subtitle);
        Assert.Contains("12 event(s)", presentation.Subtitle);
        Assert.Equal("Overlay on", presentation.Badge);
        Assert.Equal(CompanionVisualTone.Warning, presentation.Tone);
    }

    [Fact]
    public void DescribeSessionSelection_builds_readable_focus_copy()
    {
        var presentation = CompanionPresentation.DescribeSessionSelection(
            new CompanionSessionSnapshot
            {
                ProcessId = 1200,
                ProcessName = "calc",
                WindowTitle = "Calculator",
                Status = CompanionSessionStatus.Recording,
                CapturedEventCount = 3,
                LastEventSummary = "Invoked #equalButton",
                LastStepSummary = "Click the equals button",
                OverlayEnabled = true
            },
            new Uri("http://127.0.0.1:7421/"));

        Assert.Contains("PID 1200", presentation.Meta);
        Assert.Contains("Latest event: Invoked #equalButton", presentation.Summary);
        Assert.Contains("Latest step: Click the equals button", presentation.Summary);
        Assert.Contains("api/companion", presentation.Activity);
        Assert.Equal(CompanionVisualTone.Success, presentation.Tone);
    }
}
