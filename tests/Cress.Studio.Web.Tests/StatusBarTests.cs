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

    [Fact]
    public void StatusBar_renders_default_labels()
    {
        var state = CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.StatusBar>();

        Assert.Contains("Status", cut.Markup);
        Assert.Contains("Ready.", cut.Markup);
        Assert.Contains("Live run", cut.Markup);
        Assert.Contains("No run in progress.", cut.Markup);
        Assert.Contains("Selection", cut.Markup);
        Assert.Contains("No selection", cut.Markup);
        Assert.Contains("Last run", cut.Markup);
        Assert.Contains("—", cut.Markup);
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
        Assert.Contains("local", cut.Markup);
    }
}
