using Cress.Core.Models;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.Validation;

public sealed class ProjectValidator
{
    private readonly ProjectLocator _projectLocator;
    private readonly ConfigLoader _configLoader;
    private readonly ProfileLoader _profileLoader;
    private readonly FlowParser _flowParser;
    private readonly CapabilityParser _capabilityParser;
    private readonly StepManifestParser _stepManifestParser;
    private readonly FixtureManifestParser _fixtureManifestParser;

    public ProjectValidator(
        ProjectLocator projectLocator,
        ConfigLoader configLoader,
        ProfileLoader profileLoader,
        FlowParser flowParser,
        CapabilityParser capabilityParser,
        StepManifestParser stepManifestParser,
        FixtureManifestParser fixtureManifestParser)
    {
        _projectLocator = projectLocator;
        _configLoader = configLoader;
        _profileLoader = profileLoader;
        _flowParser = flowParser;
        _capabilityParser = capabilityParser;
        _stepManifestParser = stepManifestParser;
        _fixtureManifestParser = fixtureManifestParser;
    }

    public ValidationResult Validate(string startDirectory, bool strict = false)
    {
        var diagnostics = new List<Diagnostic>();
        var projectRoot = _projectLocator.FindProjectRoot(startDirectory);
        if (projectRoot is null)
        {
            return new ValidationResult
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "PRJ001",
                        Message = "Could not locate a Cress project root.",
                        File = startDirectory
                    }
                ]
            };
        }

        var configResult = _configLoader.Load(projectRoot, strict);
        diagnostics.AddRange(configResult.Diagnostics);

        if (!configResult.Success)
        {
            return new ValidationResult { Diagnostics = diagnostics };
        }

        var config = configResult.Value!;

        foreach (var profileResult in _profileLoader.LoadAll(projectRoot, strict))
        {
            diagnostics.AddRange(profileResult.Diagnostics);
        }

        var parsedFlows = EnumerateFiles(projectRoot, config.Paths.Flows, "*.flow.yaml")
            .Select(flowFile => _flowParser.ParseFile(flowFile, strict))
            .ToList();
        diagnostics.AddRange(parsedFlows.SelectMany(result => result.Diagnostics));
        foreach (var duplicate in parsedFlows.Where(result => result.Value is not null)
                     .Select(result => result.Value!)
                     .GroupBy(flow => flow.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "FLW011",
                Message = $"Duplicate flow id '{duplicate.Key}' was found.",
                File = duplicate.First().SourceFile
            });
        }

        foreach (var capabilityFile in EnumerateFiles(projectRoot, config.Paths.Capabilities, "*.md"))
        {
            diagnostics.AddRange(_capabilityParser.ParseFile(capabilityFile, strict).Diagnostics);
        }

        var stepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stepFile in EnumerateFiles(projectRoot, config.Paths.Steps, "*.yaml"))
        {
            var stepResult = _stepManifestParser.ParseFile(stepFile, strict);
            diagnostics.AddRange(stepResult.Diagnostics);
            if (stepResult.Value is null)
            {
                continue;
            }

            foreach (var step in stepResult.Value.Steps)
            {
                if (!stepNames.Add(step.Name))
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "REG001",
                        Message = $"Duplicate step '{step.Name}' was found across manifests.",
                        File = step.SourceFile
                    });
                }
            }
        }

        var fixtureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fixtureFile in EnumerateFiles(projectRoot, config.Paths.Fixtures, "*.yaml"))
        {
            var fixtureResult = _fixtureManifestParser.ParseFile(fixtureFile, strict);
            diagnostics.AddRange(fixtureResult.Diagnostics);
            if (fixtureResult.Value is null)
            {
                continue;
            }

            foreach (var fixture in fixtureResult.Value.Fixtures.Keys)
            {
                if (!fixtureNames.Add(fixture))
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "FIX005",
                        Message = $"Duplicate fixture '{fixture}' was found.",
                        File = fixtureFile
                    });
                }
            }
        }

        return new ValidationResult { Diagnostics = diagnostics };
    }

    private static IEnumerable<string> EnumerateFiles(string projectRoot, string relativePath, string pattern)
    {
        var fullPath = Path.Combine(projectRoot, relativePath);
        return Directory.Exists(fullPath)
            ? Directory.EnumerateFiles(fullPath, pattern, SearchOption.AllDirectories)
            : [];
    }
}
