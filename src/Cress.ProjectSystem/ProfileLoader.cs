using Cress.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.ProjectSystem;

public sealed class ProfileLoader
{
    public OperationResult<CressProfile> Load(string projectRoot, string profileName, bool strict = false)
    {
        var profilePath = Path.Combine(projectRoot, ".cress", "profiles", $"{profileName}.yaml");
        return LoadFile(profilePath, profileName, strict);
    }

    public IReadOnlyList<OperationResult<CressProfile>> LoadAll(string projectRoot, bool strict = false)
    {
        var profileDirectory = Path.Combine(projectRoot, ".cress", "profiles");
        if (!Directory.Exists(profileDirectory))
        {
            return
            [
                new OperationResult<CressProfile>
                {
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Code = "PRF001",
                            Message = "Profile directory was not found.",
                            File = profileDirectory
                        }
                    ]
                }
            ];
        }

        return Directory.EnumerateFiles(profileDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadFile(path, Path.GetFileNameWithoutExtension(path), strict))
            .ToList();
    }

    public OperationResult<CressProfile> LoadActive(string projectRoot, CressConfig config, string? profileName = null, bool strict = false)
    {
        var targetProfile = string.IsNullOrWhiteSpace(profileName) ? config.Project.DefaultProfile : profileName;
        return Load(projectRoot, targetProfile!, strict);
    }

    private static OperationResult<CressProfile> LoadFile(string profilePath, string defaultProfileName, bool strict)
    {
        if (!File.Exists(profilePath))
        {
            return new OperationResult<CressProfile>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "PRF002",
                        Message = "Profile file was not found.",
                        File = profilePath
                    }
                ]
            };
        }

        try
        {
            var builder = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreFields();

            if (!strict)
            {
                builder = builder.IgnoreUnmatchedProperties();
            }

            var profile = builder.Build().Deserialize<CressProfile>(File.ReadAllText(profilePath)) ?? new CressProfile();
            profile = profile with { Profile = string.IsNullOrWhiteSpace(profile.Profile) ? defaultProfileName : profile.Profile };

            var diagnostics = new List<Diagnostic>();
            if (string.IsNullOrWhiteSpace(profile.Profile))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "PRF003",
                    Message = "Profile name is required.",
                    File = profilePath
                });
            }

            return new OperationResult<CressProfile>
            {
                Value = profile,
                Diagnostics = diagnostics
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<CressProfile>
            {
                Diagnostics =
                [
                    ConfigLoader.CreateYamlDiagnostic("PRF004", "Profile YAML is invalid.", profilePath, ex)
                ]
            };
        }
    }
}
