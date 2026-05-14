using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class StatusBarTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void StatusBar_renders_default_labels()
    {
        var state = CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.StatusBar>();

        Assert.Contains("Status", cut.Markup);
        Assert.Contains(state.StatusMessage, cut.Markup);
        Assert.Contains("Live run", cut.Markup);
        Assert.Contains("No run in progress.", cut.Markup);
        Assert.Contains("Selection", cut.Markup);
        Assert.Contains("No selection", cut.Markup);
        Assert.Contains("Last run", cut.Markup);
        Assert.Contains("—", cut.Markup);
        Assert.Contains("Idle", cut.Find("[data-testid='status-bar-state-label']").TextContent);
    }

    [Fact]
    public void StatusBar_shows_last_run_description_when_run_exists()
    {
        var state = CreateState();

        var run = new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-001",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)
                },
                Flows = []
            }
        };
        state.Runs.Add(run);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.StatusBar>();

        Assert.Contains("Last run", cut.Markup);
        Assert.DoesNotContain(">—<", cut.Markup);
        Assert.Contains("run-001", cut.Find("[data-testid='status-bar-last-run-id']").TextContent);
        Assert.Contains("local", cut.Markup);
    }

    [Theory]
    [InlineData(true, "Working hard", "Running", "statusbar-dot--running")]
    [InlineData(false, "Run failed", "Issue", "statusbar-dot--error")]
    [InlineData(false, "Run complete", "Ready", "statusbar-dot--success")]
    public void StatusBar_uses_expected_status_badge_and_dot(bool isBusy, string statusMessage, string expectedLabel, string expectedDotClass)
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.IsBusy), isBusy);
        SetPrivate(state, nameof(StudioWorkspaceState.StatusMessage), statusMessage);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.StatusBar>();

        Assert.Equal(expectedLabel, cut.Find("[data-testid='status-bar-state-label']").TextContent);
        Assert.Contains(expectedDotClass, cut.Find(".statusbar-dot").ClassList);
        Assert.Contains(statusMessage, cut.Find("[data-testid='status-bar-status-text']").TextContent);
    }
}
