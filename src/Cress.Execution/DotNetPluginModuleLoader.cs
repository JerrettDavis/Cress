using System.Reflection;
using System.Runtime.Loader;
using Cress.Sdk;

namespace Cress.Execution;

public interface IDotNetPluginModuleLoader
{
    IReadOnlyList<ICressPluginModule> LoadModules(string projectRoot, string? pluginName);
}

public sealed class ReflectionDotNetPluginModuleLoader : IDotNetPluginModuleLoader
{
    private readonly Dictionary<string, PluginLoadResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ICressPluginModule> LoadModules(string projectRoot, string? pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            return [];
        }

        var cacheKey = CreateCacheKey(projectRoot, pluginName);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached.Modules;
        }

        var pluginDirectory = Path.Combine(projectRoot, "steps", "dotnet");
        var candidates = Directory.Exists(pluginDirectory)
            ? Directory.EnumerateFiles(pluginDirectory, $"{pluginName}.dll", SearchOption.AllDirectories)
                .Where(IsRuntimePluginAssembly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(1)
            : [];

        var loadResult = PluginLoadResult.Empty;
        foreach (var candidate in candidates)
        {
            var loadContext = new PluginAssemblyLoadContext(Path.GetDirectoryName(candidate)!);
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(candidate));
            var modules = new List<ICressPluginModule>();
            foreach (var type in assembly.GetTypes().Where(type =>
                         typeof(ICressPluginModule).IsAssignableFrom(type) &&
                         !type.IsAbstract &&
                         type.GetConstructor(Type.EmptyTypes) is not null))
            {
                if (Activator.CreateInstance(type) is ICressPluginModule module)
                {
                    modules.Add(module);
                }
            }

            loadResult = new PluginLoadResult(loadContext, modules);
            break;
        }

        _cache[cacheKey] = loadResult;
        return loadResult.Modules;
    }

    private static string CreateCacheKey(string projectRoot, string pluginName)
        => $"{Path.GetFullPath(projectRoot)}::{pluginName}";

    private static bool IsRuntimePluginAssembly(string path)
    {
        if (!path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var depsFile = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}.deps.json");
        return File.Exists(depsFile);
    }

    private sealed record PluginLoadResult(PluginAssemblyLoadContext? LoadContext, IReadOnlyList<ICressPluginModule> Modules)
    {
        public static PluginLoadResult Empty { get; } = new(null, []);
    }

    private sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _assemblyDirectory;

        public PluginAssemblyLoadContext(string assemblyDirectory)
            : base(isCollectible: false)
        {
            _assemblyDirectory = assemblyDirectory;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (sharedAssembly is not null)
            {
                return sharedAssembly;
            }

            var candidatePath = Path.Combine(_assemblyDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(candidatePath)
                ? LoadFromAssemblyPath(candidatePath)
                : null;
        }
    }
}
