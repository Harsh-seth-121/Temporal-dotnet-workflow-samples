namespace Sandbox.Worker;

using System.Reflection;
using Sandbox.Abstractions;

/// <summary>
/// Finds the sample modules to run.
///
/// Scans the output directory rather than walking AppDomain assemblies: .NET loads
/// referenced assemblies lazily, so a sample whose types have not been touched yet
/// would not show up in a live assembly list.
///
/// Every failure here throws rather than skipping the offending sample. A skipped
/// module means its workflows are never registered, and the only symptom is a
/// workflow that sits unclaimed on the task queue until it times out. Refusing to
/// start, naming the assembly or type at fault, is far easier to diagnose.
/// </summary>
public static class SampleDiscovery
{
    private const string SampleAssemblyPattern = "Sandbox.Samples.*.dll";

    public static IReadOnlyList<ISampleModule> Discover()
    {
        var modules = new List<ISampleModule>();

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, SampleAssemblyPattern))
        {
            // Test assemblies can sit beside the samples they cover. They carry no
            // modules, and loading them would drag in the test framework.
            if (Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var type in LoadModuleTypes(path))
            {
                modules.Add(Instantiate(type));
            }
        }

        var duplicate = modules
            .GroupBy(module => module.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two sample modules both report the name '{duplicate.Key}': " +
                string.Join(", ", duplicate.Select(module => module.GetType().FullName)) +
                ". Names appear in startup logs and must be unique.");
        }

        return modules
            .OrderBy(module => module.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<Type> LoadModuleTypes(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        Type?[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Thrown when a type references something that cannot be resolved,
            // typically a sample missing a package reference.
            var reasons = ex.LoaderExceptions
                .Where(inner => inner is not null)
                .Select(inner => inner!.Message)
                .Distinct(StringComparer.Ordinal);

            throw new InvalidOperationException(
                $"Could not load types from '{Path.GetFileName(path)}': " +
                string.Join("; ", reasons),
                ex);
        }

        return types.Where(type =>
            type is { IsAbstract: false, IsInterface: false } &&
            typeof(ISampleModule).IsAssignableFrom(type))!;
    }

    private static ISampleModule Instantiate(Type type)
    {
        try
        {
            return (ISampleModule)Activator.CreateInstance(type)!;
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException)
        {
            throw new InvalidOperationException(
                $"Could not construct sample module '{type.FullName}'. Modules need a " +
                "public parameterless constructor; put anything that needs dependency " +
                $"injection in {nameof(ISampleModule.ConfigureServices)} instead.",
                ex);
        }
    }
}
