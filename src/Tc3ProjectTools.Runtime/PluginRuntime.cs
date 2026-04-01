using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tc3ProjectTools.Abstractions;

namespace Tc3ProjectTools.Runtime;

public enum PluginLoadState
{
    Loaded,
    Disabled,
    Incompatible,
    Failed,
}

public sealed class PluginCatalog
{
    public IReadOnlyList<PluginDescriptor> Plugins { get; init; } = Array.Empty<PluginDescriptor>();

    public IReadOnlyDictionary<string, RegisteredTool> Tools { get; init; } = new Dictionary<string, RegisteredTool>(StringComparer.OrdinalIgnoreCase);
}

public sealed class PluginDescriptor
{
    public required PluginManifest Manifest { get; init; }

    public PluginLoadState State { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<RegisteredTool> Tools { get; init; } = Array.Empty<RegisteredTool>();

    internal AssemblyLoadContext? LoadContext { get; init; }
}

public sealed class RegisteredTool
{
    public required string PluginId { get; init; }

    public required string PluginName { get; init; }

    public required PluginToolDefinition Definition { get; init; }

    public required Type RunnerType { get; init; }
}

public static class PluginManifestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static PluginManifest Load(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions)
                       ?? throw new InvalidOperationException($"Could not deserialize manifest '{manifestPath}'.");

        if (string.IsNullOrWhiteSpace(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.EntryAssembly)
            || string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            throw new InvalidOperationException($"Manifest '{manifestPath}' is missing required fields.");
        }

        return new PluginManifest
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            EntryAssembly = manifest.EntryAssembly,
            EntryType = manifest.EntryType,
            MinHostVersion = string.IsNullOrWhiteSpace(manifest.MinHostVersion) ? "1.0.0" : manifest.MinHostVersion,
            InstallationDirectory = Path.GetDirectoryName(manifestPath)!,
            ManifestPath = manifestPath,
        };
    }
}

public sealed class PluginCatalogBootstrapper
{
    public PluginCatalog Build(IServiceCollection services, AppPaths paths, ISettingsService settingsService)
    {
        var plugins = new List<PluginDescriptor>();
        var tools = new Dictionary<string, RegisteredTool>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(paths.PluginRoot))
        {
            return new PluginCatalog
            {
                Plugins = plugins,
                Tools = tools,
            };
        }

        foreach (var pluginDirectory in Directory.EnumerateDirectories(paths.PluginRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = PluginManifestSerializer.Load(manifestPath);
                if (!settingsService.IsPluginEnabled(manifest.Id))
                {
                    plugins.Add(new PluginDescriptor
                    {
                        Manifest = manifest,
                        State = PluginLoadState.Disabled,
                        Message = "Disabled in local settings.",
                    });
                    continue;
                }

                if (!Version.TryParse(manifest.MinHostVersion, out var minimumVersion))
                {
                    throw new InvalidOperationException($"Manifest '{manifest.Id}' has an invalid minHostVersion value.");
                }

                if (minimumVersion > HostMetadata.Version)
                {
                    plugins.Add(new PluginDescriptor
                    {
                        Manifest = manifest,
                        State = PluginLoadState.Incompatible,
                        Message = $"Requires host {minimumVersion} or newer.",
                    });
                    continue;
                }

                var assemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
                if (!File.Exists(assemblyPath))
                {
                    throw new FileNotFoundException("Plugin entry assembly was not found.", assemblyPath);
                }

                var loadContext = new PluginLoadContext(assemblyPath);
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                var entryType = assembly.GetType(manifest.EntryType, throwOnError: true)
                                ?? throw new InvalidOperationException($"Could not load type '{manifest.EntryType}'.");
                if (!typeof(IPluginEntryPoint).IsAssignableFrom(entryType))
                {
                    throw new InvalidOperationException($"Type '{manifest.EntryType}' does not implement {nameof(IPluginEntryPoint)}.");
                }

                var entryPoint = (IPluginEntryPoint)(Activator.CreateInstance(entryType)
                                   ?? throw new InvalidOperationException($"Could not create plugin entry point '{manifest.EntryType}'."));
                var registrationContext = new PluginRegistrationContext(manifest, services);
                entryPoint.Configure(registrationContext);

                var pluginTools = new List<RegisteredTool>();
                foreach (var registration in registrationContext.ToolRegistrations)
                {
                    if (tools.ContainsKey(registration.Definition.Id))
                    {
                        throw new InvalidOperationException($"Tool id '{registration.Definition.Id}' is already registered.");
                    }

                    services.AddTransient(registration.RunnerType);
                    var registeredTool = new RegisteredTool
                    {
                        PluginId = manifest.Id,
                        PluginName = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
                        Definition = registration.Definition,
                        RunnerType = registration.RunnerType,
                    };

                    tools.Add(registeredTool.Definition.Id, registeredTool);
                    pluginTools.Add(registeredTool);
                }

                plugins.Add(new PluginDescriptor
                {
                    Manifest = manifest,
                    State = PluginLoadState.Loaded,
                    Message = $"Loaded {pluginTools.Count} tool(s).",
                    Tools = pluginTools,
                    LoadContext = loadContext,
                });
            }
            catch (Exception ex)
            {
                plugins.Add(new PluginDescriptor
                {
                    Manifest = new PluginManifest
                    {
                        Id = Path.GetFileName(pluginDirectory),
                        Name = Path.GetFileName(pluginDirectory),
                        InstallationDirectory = pluginDirectory,
                        ManifestPath = manifestPath,
                    },
                    State = PluginLoadState.Failed,
                    Message = ex.Message,
                });
            }
        }

        return new PluginCatalog
        {
            Plugins = plugins,
            Tools = tools,
        };
    }

    private sealed class PluginRegistrationContext : IPluginRegistrationContext
    {
        public PluginRegistrationContext(PluginManifest manifest, IServiceCollection services)
        {
            Manifest = manifest;
            Services = services;
        }

        public PluginManifest Manifest { get; }

        public IServiceCollection Services { get; }

        public IList<(PluginToolDefinition Definition, Type RunnerType)> ToolRegistrations { get; } =
            new List<(PluginToolDefinition Definition, Type RunnerType)>();

        public void RegisterTool(PluginToolDefinition toolDefinition, Type runnerType)
        {
            if (!typeof(IToolRunner).IsAssignableFrom(runnerType))
            {
                throw new InvalidOperationException($"{runnerType.FullName} does not implement {nameof(IToolRunner)}.");
            }

            ToolRegistrations.Add((toolDefinition, runnerType));
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string mainAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssemblyName = typeof(IPluginEntryPoint).Assembly.GetName().Name;
            if (string.Equals(assemblyName.Name, sharedAssemblyName, StringComparison.Ordinal)
                || string.Equals(assemblyName.Name, "Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.Ordinal)
                || string.Equals(assemblyName.Name, "Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal))
            {
                return null;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }
    }
}
