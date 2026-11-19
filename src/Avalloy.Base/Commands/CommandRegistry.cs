using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Avalloy.Commands;

/// <summary>
/// Per-app catalog mapping <see cref="CommandAttribute.Name"/> to a
/// <see cref="CommandDescriptor"/> plus the implementation type. Instance-based
/// so multiple apps in one process can coexist; tests can build their own
/// registries without static state. Commands are resolved from the DI container
/// by the dispatcher — the registry only owns names and types.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDescriptor> _byName =
        new(StringComparer.Ordinal);

    /// <summary>Register one command type. Throws on duplicate name or when
    /// the type lacks <see cref="CommandAttribute"/>. The DI container should
    /// have the type added separately (AvalloyApp does this automatically
    /// for every type passed through the registry).</summary>
    public CommandDescriptor Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCommand>() where TCommand : class, IAlloyCommand =>
        Register(typeof(TCommand));

    /// <summary>Register one command type by reflection. Same semantics as the
    /// generic overload; useful for assembly-scan plumbing.</summary>
    public CommandDescriptor Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type commandType)
    {
        var descriptor = CommandDescriptor.FromType(commandType);
        if (_byName.ContainsKey(descriptor.Name))
            throw new InvalidOperationException(
                $"A command named '{descriptor.Name}' is already registered.");
        _byName[descriptor.Name] = descriptor;
        return descriptor;
    }

    /// <summary>Scan an assembly for non-abstract <see cref="IAlloyCommand"/>
    /// implementations carrying <see cref="CommandAttribute"/> and register
    /// each. Types without the attribute are silently skipped — only attributed
    /// commands are considered registrable. Useful for app startup and plugin
    /// assemblies. Not for trimmed or Native AOT apps, where the scan can miss
    /// types the trimmer removed: register each with <see cref="Register{TCommand}"/>
    /// instead.</summary>
    [RequiresUnreferencedCode("Scans the assembly's types, which trimming can remove. Register each command with Register<TCommand>() instead.")]
    public void RegisterFromAssembly(Assembly assembly)
    {
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IAlloyCommand).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttribute<CommandAttribute>() is null) continue;
            Register(type);
        }
    }

    [RequiresUnreferencedCode("Enumerates the assembly's types, which trimming can remove.")]
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    /// <summary>Look up by name. Returns null when no command is registered
    /// under <paramref name="name"/>.</summary>
    public CommandDescriptor? Get(string name) =>
        _byName.TryGetValue(name, out var d) ? d : null;

    /// <summary>All registered descriptors. Ordering is insertion order.</summary>
    public IReadOnlyCollection<CommandDescriptor> All => _byName.Values;
}
