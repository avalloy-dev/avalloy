using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Avalloy.Preferences;

/// <summary>
/// Writes <typeparamref name="TEnum"/> as a camelCase string, the way
/// <see cref="PreferencesService{T}"/> writes every enum, without the runtime
/// code generation the non-generic <see cref="JsonStringEnumConverter"/> needs.
/// </summary>
/// <remarks>Declare it on each enum a preferences model reaches —
/// <c>[JsonConverter(typeof(CamelCaseEnumConverter&lt;MyEnum&gt;))]</c> — when the
/// model is (de)serialized through a source-generated context, as trimmed and
/// Native AOT apps must. Numbers are still accepted on read, as before.</remarks>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;

/// <summary>
/// Checks, at construction, the two ways a source-generated context silently
/// changes the file, and throws naming the type instead. An enum left without a
/// converter is written as a number rather than a camelCase string. An object
/// the context has no properties for is written as <c>{}</c> and read back as
/// defaults: what happens when its properties come from another source
/// generator, which System.Text.Json's cannot see.
/// </summary>
internal static class ContextFormatGuard
{
    public static void Require(JsonTypeInfo root, string fileName)
    {
        var seen = new HashSet<Type>();
        Visit(root, seen, fileName);
    }

    private static void Visit(JsonTypeInfo info, HashSet<Type> seen, string fileName)
    {
        if (!seen.Add(info.Type)) return;

        if (Nullable.GetUnderlyingType(info.Type) is { } underlying)
        {
            Visit(info.Options.GetTypeInfo(underlying), seen, fileName);
            return;
        }
        if (info.Type.IsEnum)
        {
            Check(info, fileName);
            return;
        }

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                if (info.Properties.Count == 0)
                    throw new ArgumentException(
                        $"The context has no properties for {info.Type.Name}, so it would be written to {fileName} as {{}} " +
                        "and every setting in it read back as its default. If they come from CommunityToolkit.Mvvm's " +
                        "[ObservableProperty] on fields, the JSON source generator cannot see them: declare them as " +
                        "[ObservableProperty] public partial properties instead.",
                        "typeInfoResolver");
                foreach (var property in info.Properties)
                {
                    // A converter on the property itself is the app's explicit choice.
                    if (property.CustomConverter is not null) continue;
                    Visit(info.Options.GetTypeInfo(property.PropertyType), seen, fileName);
                }
                break;
            case JsonTypeInfoKind.Enumerable:
            case JsonTypeInfoKind.Dictionary:
                if (info.ElementType is { } element)
                    Visit(info.Options.GetTypeInfo(element), seen, fileName);
                break;
        }
    }

    private static void Check(JsonTypeInfo info, string fileName)
    {
        var names = Enum.GetNames(info.Type);
        if (names.Length == 0) return;

        // Names and underlying values come back in the same (value) order.
        var sample = Enum.ToObject(info.Type, Enum.GetValuesAsUnderlyingType(info.Type).GetValue(0)!);
        var written = JsonSerializer.Serialize(sample, info);
        var expected = $"\"{JsonNamingPolicy.CamelCase.ConvertName(names[0])}\"";
        if (written == expected) return;

        throw new ArgumentException(
            $"{info.Type.Name} would be written to {fileName} as {written} rather than {expected}. " +
            $"Add [JsonConverter(typeof(CamelCaseEnumConverter<{info.Type.Name}>))] to it.",
            "typeInfoResolver");
    }
}
