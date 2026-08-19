using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace TelemetryDashboard.Core.Plugins;

/// <summary>
/// Loads compiled .NET plugin assemblies and invokes their public filter methods.
/// </summary>
/// <remarks>
/// Each assembly is loaded into its own collectible <see cref="AssemblyLoadContext"/>, so replacing
/// the file on disk genuinely unloads the previous version. Without a collectible context the old
/// assembly stays resident and its file stays locked, which makes hot-reload impossible after the
/// first load.
/// <para>
/// A filter method is any public static method taking a single <c>double</c> (or an
/// <c>IReadOnlyDictionary&lt;string,double&gt;</c>) and returning a <c>double</c>.
/// </para>
/// </remarks>
public sealed class ManagedAssemblyScriptEngine : IScriptEngine
{
    private static readonly string[] Extensions = { ".dll" };

    public string Name => "managed";

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IScriptModule? Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        var context = new AssemblyLoadContext($"plugin:{Path.GetFileName(filePath)}", isCollectible: true);
        try
        {
            // Load from a copy of the bytes so the plugin file itself is never locked.
            using var stream = new MemoryStream(File.ReadAllBytes(filePath));
            Assembly assembly = context.LoadFromStream(stream);

            var methods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in assembly.GetExportedTypes())
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (IsFilterMethod(method)) methods[method.Name] = method;
                }
            }

            if (methods.Count == 0)
            {
                context.Unload();
                return null;
            }

            return new ManagedScriptModule(filePath, Name, context, methods);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException or ReflectionTypeLoadException)
        {
            context.Unload();
            return null;
        }
    }

    private static bool IsFilterMethod(MethodInfo method)
    {
        if (method.ReturnType != typeof(double)) return false;

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 1) return false;

        Type parameterType = parameters[0].ParameterType;
        return parameterType == typeof(double) ||
               parameterType == typeof(IReadOnlyDictionary<string, double>);
    }

    private sealed class ManagedScriptModule : IScriptModule
    {
        private readonly AssemblyLoadContext _context;
        private readonly IReadOnlyDictionary<string, MethodInfo> _methods;

        public ManagedScriptModule(
            string sourcePath,
            string engineName,
            AssemblyLoadContext context,
            IReadOnlyDictionary<string, MethodInfo> methods)
        {
            SourcePath = sourcePath;
            EngineName = engineName;
            _context = context;
            _methods = methods;
        }

        public string SourcePath { get; }

        public string EngineName { get; }

        public IReadOnlyCollection<string> FunctionNames => _methods.Keys.ToList();

        public bool TryInvoke(string functionName, ScriptInvocationContext context, out object? result)
        {
            result = null;
            if (!_methods.TryGetValue(functionName ?? string.Empty, out MethodInfo? method)) return false;

            try
            {
                object argument = method.GetParameters()[0].ParameterType == typeof(double)
                    ? context.Resolve(context.NodeId, "value")
                    : context.Variables;

                result = method.Invoke(null, new[] { argument });
                return true;
            }
            catch (TargetInvocationException)
            {
                return false; // plugin threw; isolate it from the pipeline
            }
        }

        public void Dispose() => _context.Unload();
    }
}
