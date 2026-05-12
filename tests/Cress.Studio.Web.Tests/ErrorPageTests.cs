using System.Diagnostics;
using Bunit;
using Cress.Studio.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Cress.Studio.Web.Tests;

public sealed class ErrorPageTests : TestContext
{
    [Fact]
    public void Error_page_shows_request_id_from_http_context()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-123"
        };

        var cut = RenderComponent<CascadingValue<HttpContext>>(parameters => parameters
            .Add(component => component.Value, httpContext)
            .AddChildContent<Error>());

        Assert.Contains("An error occurred while processing your request.", cut.Markup);
        Assert.Contains("trace-123", cut.Markup);
    }

    [Fact]
    public void Error_page_omits_request_id_when_no_context_or_activity_is_available()
    {
        var priorActivity = Activity.Current;
        Activity.Current = null;

        try
        {
            var cut = RenderComponent<Error>();

            Assert.DoesNotContain("Request ID:", cut.Markup);
            Assert.Contains("Development Mode", cut.Markup);
        }
        finally
        {
            Activity.Current = priorActivity;
        }
    }
}
