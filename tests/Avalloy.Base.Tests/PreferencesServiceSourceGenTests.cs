using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using Avalloy.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Avalloy.Base.Tests;

/// <summary>
/// <see cref="PreferencesService{T}"/> given a source-generated JSON context.
/// </summary>
/// <remarks>
/// <para>Without one, the first <c>Load</c> builds reflection metadata for the whole model, which was measured
/// at 80–100 ms of an app's launch for a small file. A context hands the serializer that metadata ready-made.
/// It must change nothing else: an app switching to one reads the file its previous version wrote and writes
/// the same bytes back, so these compare the two paths byte for byte rather than checking a few fields.</para>
/// </remarks>
[TestClass]
public sealed class PreferencesServiceSourceGenTests
{
    private const string Dir = "/prefs";
    private const string PrefsPath = "/prefs/preferences.json";

    [TestMethod]
    public void The_File_Written_With_A_Context_Is_Byte_For_Byte_The_Reflection_One()
    {
        var reflection = Saved(resolver: null);
        var sourceGen = Saved(SourceGenPrefsJsonContext.Default);

        sourceGen.Should().Be(reflection);
        sourceGen.Should().Contain("\"variant\": \"highContrast\"", "enums stay camelCase strings")
            .And.Contain("\"nickname\": null", "nulls are still written, as documentation of every setting")
            .And.Contain("\"fontSize\": 14", "names stay camelCase, nested sections included");
    }

    [TestMethod]
    public void A_Context_Reads_What_A_Hand_Edited_File_Can_Hold()
    {
        var io = new MockFileSystem();
        io.AddFile(PrefsPath, new MockFileData("""
            {
              // comments are allowed
              "VARIANT": "dark",
              "editor": { "fontSize": 18, },
            }
            """));

        var service = Create(io, SourceGenPrefsJsonContext.Default);
        service.Initialize();

        service.LastLoadError.Should().BeNull();
        service.Current.Variant.Should().Be(SourceGenVariant.Dark, "names match case-insensitively");
        service.Current.Editor.FontSize.Should().Be(18, "trailing commas are accepted");
    }

    [TestMethod]
    public void The_Context_Is_What_Supplies_The_Metadata()
    {
        // The proof that reflection is out of the picture: every type the serializer needs is asked of the
        // resolver, and it answers for them all. Reading and writing use separate options, and both must ask
        // it: one asking is enough to make the type list look complete.
        var counting = new CountingResolver(SourceGenPrefsJsonContext.Default);
        var io = new MockFileSystem();

        var service = Create(io, counting);
        service.Initialize();
        service.Current.Editor.FontSize = 20;
        service.SaveNow();
        service.Load();

        counting.Asked.Should().Contain(typeof(SourceGenPrefs)).And.Contain(typeof(EditorSection));
        counting.Unanswered.Should().BeEmpty();
        counting.OptionsAskingFor(typeof(SourceGenPrefs)).Should().Be(2, "the read side and the write side");
        service.Current.Editor.FontSize.Should().Be(20);
    }

    [TestMethod]
    public void A_Context_Without_The_Model_Fails_At_Construction()
    {
        // Rather than at the first Load, where the error would be swallowed into LastLoadError and the app
        // would quietly start on defaults.
        var act = () => Create(new MockFileSystem(), UnrelatedJsonContext.Default);

        act.Should().Throw<ArgumentException>().WithMessage("*SourceGenPrefs*");
    }

    [TestMethod]
    public void An_Enum_Without_Its_Converter_Fails_At_Construction()
    {
        // The source-generated path gives enums no converter of its own (the one that serves them all needs
        // runtime code generation), so an enum that declares none would be written as a number: a silent
        // change to the user's file. Construction names it instead.
        var io = new MockFileSystem();
        io.Directory.CreateDirectory(Dir);
        var act = () => new PreferencesService<UnannotatedPrefs>(Dir, TimeSpan.FromHours(1), io,
            "preferences.json", watch: false, UnannotatedPrefsJsonContext.Default, watcherFactory: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*UnannotatedLevel*0*\"low\"*CamelCaseEnumConverter<UnannotatedLevel>*");
    }

    [TestMethod]
    public void Without_A_Context_An_Enum_Needs_No_Converter_Of_Its_Own()
    {
        // Apps written before the converter existed declare none: the reflection path must keep writing
        // their enums as camelCase strings.
        var io = new MockFileSystem();
        io.Directory.CreateDirectory(Dir);
        var unannotated = new PreferencesService<UnannotatedPrefs>(Dir, TimeSpan.FromHours(1), io,
            "preferences.json", watch: false, typeInfoResolver: null, watcherFactory: null);

        unannotated.Initialize();
        unannotated.Current.Level = UnannotatedLevel.High;
        unannotated.SaveNow();

        io.File.ReadAllText(PrefsPath).Should().Contain("\"level\": \"high\"");
    }

    [TestMethod]
    public void An_Enum_Written_In_Another_Case_Fails_At_Construction()
    {
        // A plain JsonStringEnumConverter<T> writes "Low": still a string, but not the file's camelCase.
        var io = new MockFileSystem();
        io.Directory.CreateDirectory(Dir);
        var act = () => new PreferencesService<PascalPrefs>(Dir, TimeSpan.FromHours(1), io,
            "preferences.json", watch: false, PascalPrefsJsonContext.Default, watcherFactory: null);

        act.Should().Throw<ArgumentException>().WithMessage("*PascalLevel*\"Low\"*\"low\"*");
    }

    [TestMethod]
    public void Core_Appearance_Prefs_Are_Written_The_Same_Either_Way()
    {
        // AppearancePrefs is what every Avalloy app embeds, and ThemeVariantPreference is the enum in it:
        // Base declares its converter, so an app's context needs nothing more than the model.
        string Write(IJsonTypeInfoResolver? resolver)
        {
            var io = new MockFileSystem();
            io.Directory.CreateDirectory(Dir);
            var service = new PreferencesService<AppearanceOnlyPrefs>(Dir, TimeSpan.FromHours(1), io,
                "preferences.json", watch: false, resolver, watcherFactory: null);
            service.Initialize();
            service.Current.Appearance.ThemeVariant = ThemeVariantPreference.Dark;
            service.Current.Appearance.ThemeOverridesDark["accent"] = "#ff8800";
            service.SaveNow();
            return io.File.ReadAllText(PrefsPath);
        }

        var sourceGen = Write(AppearanceOnlyPrefsJsonContext.Default);

        // AppearancePrefs uses [ObservableProperty] on fields, which an app's context could not see if the app
        // compiled it. It arrives compiled, generated properties included, so they are all here.
        sourceGen.Should().Be(Write(resolver: null)).And.Contain("\"themeVariant\": \"dark\"")
            .And.Contain("\"autoSwitchVariant\": true").And.Contain("\"densityPreset\": \"normal\"");
    }

    [TestMethod]
    public void A_Context_Round_Trips_A_Model_And_Reads_The_Reflection_File()
    {
        // Every kind of value an app's model holds: a nested section, a bool, a nullable string set and unset, and
        // an enum. Written through the context, a fresh service reads all of it back. An app moving to a context
        // reads the file its reflection build wrote, and the reverse, so either build can follow the other.
        static (MockFileSystem Io, string Json) Write(IJsonTypeInfoResolver? resolver)
        {
            var io = new MockFileSystem();
            var service = CreateRoundTrip(io, resolver);
            service.Initialize();
            service.Current.Level = RoundTripLevel.VeryHigh;
            service.Current.Enabled = true;
            service.Current.Nickname = "crab";
            service.Current.Window.Width = 1280;
            service.Current.Window.Maximized = true;
            service.SaveNow();
            return (io, io.File.ReadAllText(PrefsPath));
        }
        static RoundTripPrefs Read(MockFileSystem io, IJsonTypeInfoResolver? resolver)
        {
            var service = CreateRoundTrip(io, resolver);
            service.Initialize();
            service.LastLoadError.Should().BeNull();
            return service.Current;
        }

        var (sourceGenIo, sourceGenJson) = Write(RoundTripPrefsJsonContext.Default);
        var (reflectionIo, reflectionJson) = Write(resolver: null);

        sourceGenJson.Should().Be(reflectionJson);
        sourceGenJson.Should().Contain("\"level\": \"veryHigh\"").And.Contain("\"enabled\": true")
            .And.Contain("\"monitor\": null").And.Contain("\"maximized\": true");
        foreach (var read in new[]
                 {
                     Read(sourceGenIo, RoundTripPrefsJsonContext.Default),
                     Read(reflectionIo, RoundTripPrefsJsonContext.Default),
                     Read(sourceGenIo, resolver: null),
                 })
        {
            read.Level.Should().Be(RoundTripLevel.VeryHigh);
            read.Enabled.Should().BeTrue();
            read.Nickname.Should().Be("crab");
            read.Window.Width.Should().Be(1280);
            read.Window.Maximized.Should().BeTrue();
            read.Window.Monitor.Should().BeNull();
        }
    }

    [TestMethod]
    public void A_Section_The_Context_Has_No_Properties_For_Fails_At_Construction()
    {
        // [ObservableProperty] on a field: the context sees the private field and not the property generated from
        // it, so the section would be saved as {} and every setting in it load as its default, with no error.
        var io = new MockFileSystem();
        io.Directory.CreateDirectory(Dir);
        var act = () => new PreferencesService<FieldStylePrefs>(Dir, TimeSpan.FromHours(1), io,
            "preferences.json", watch: false, FieldStylePrefsJsonContext.Default, watcherFactory: null);

        act.Should().Throw<ArgumentException>().WithMessage("*FieldStyleSection*{}*public partial properties*");

        // Reflection sees the generated property, so the reflection path is unchanged for such a model.
        var reflection = new PreferencesService<FieldStylePrefs>(Dir, TimeSpan.FromHours(1), io,
            "preferences.json", watch: false, typeInfoResolver: null, watcherFactory: null);
        reflection.Initialize();
        io.File.ReadAllText(PrefsPath).Should().Contain("\"checkOnLaunch\": true");
    }

    private static PreferencesService<RoundTripPrefs> CreateRoundTrip(MockFileSystem io, IJsonTypeInfoResolver? resolver)
    {
        io.Directory.CreateDirectory(Dir);
        return new PreferencesService<RoundTripPrefs>(Dir, TimeSpan.FromHours(1), io, "preferences.json",
            watch: false, resolver, watcherFactory: null);
    }

    private static string Saved(IJsonTypeInfoResolver? resolver)
    {
        var io = new MockFileSystem();
        var service = Create(io, resolver);
        service.Initialize();
        service.Current.Variant = SourceGenVariant.HighContrast;
        service.Current.Editor.FontSize = 14;
        service.SaveNow();
        return io.File.ReadAllText(PrefsPath);
    }

    private static PreferencesService<SourceGenPrefs> Create(MockFileSystem io, IJsonTypeInfoResolver? resolver)
    {
        io.Directory.CreateDirectory(Dir);
        return new PreferencesService<SourceGenPrefs>(Dir, TimeSpan.FromHours(1), io, "preferences.json",
            watch: false, resolver, watcherFactory: null);
    }

    private sealed class CountingResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        private readonly List<(Type Type, JsonSerializerOptions Options)> _requests = new();

        public List<Type> Asked { get; } = new();
        public List<Type> Unanswered { get; } = new();

        public int OptionsAskingFor(Type type) =>
            _requests.Where(r => r.Type == type).Select(r => r.Options).Distinct().Count();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            Asked.Add(type);
            _requests.Add((type, options));
            var info = inner.GetTypeInfo(type, options);
            if (info is null) Unanswered.Add(type);
            return info;
        }
    }
}

[JsonConverter(typeof(CamelCaseEnumConverter<SourceGenVariant>))]
public enum SourceGenVariant { Light, Dark, HighContrast }

public enum UnannotatedLevel { Low, High }

public sealed class EditorSection : INotifyPropertyChanged
{
    private int _fontSize = 12;

    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontSize)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SourceGenPrefs : IPreferencesModel, INotifyPropertyChanged
{
    private SourceGenVariant _variant;

    public SourceGenVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Variant)));
        }
    }

    public string? Nickname { get; set; }

    public EditorSection Editor { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RegisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged += handler;
        Editor.PropertyChanged += handler;
    }

    public void UnregisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged -= handler;
        Editor.PropertyChanged -= handler;
    }
}

// Deliberately declares none of the service's options: the service's own must win.
[JsonSerializable(typeof(SourceGenPrefs))]
internal sealed partial class SourceGenPrefsJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(EditorSection))]
internal sealed partial class UnrelatedJsonContext : JsonSerializerContext;

public sealed class UnannotatedPrefs : IPreferencesModel
{
    public UnannotatedLevel Level { get; set; }
    public void RegisterMutationHandler(PropertyChangedEventHandler handler) { }
    public void UnregisterMutationHandler(PropertyChangedEventHandler handler) { }
}

[JsonConverter(typeof(JsonStringEnumConverter<PascalLevel>))]
public enum PascalLevel { Low, High }

public sealed class PascalPrefs : IPreferencesModel
{
    public PascalLevel Level { get; set; }
    public void RegisterMutationHandler(PropertyChangedEventHandler handler) { }
    public void UnregisterMutationHandler(PropertyChangedEventHandler handler) { }
}

[JsonSerializable(typeof(PascalPrefs))]
internal sealed partial class PascalPrefsJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(UnannotatedPrefs))]
internal sealed partial class UnannotatedPrefsJsonContext : JsonSerializerContext;

public sealed class AppearanceOnlyPrefs : IPreferencesModel
{
    public AppearancePrefs Appearance { get; set; } = new();
    public void RegisterMutationHandler(PropertyChangedEventHandler handler) => Appearance.PropertyChanged += handler;
    public void UnregisterMutationHandler(PropertyChangedEventHandler handler) => Appearance.PropertyChanged -= handler;
}

[JsonSerializable(typeof(AppearanceOnlyPrefs))]
internal sealed partial class AppearanceOnlyPrefsJsonContext : JsonSerializerContext;

[JsonConverter(typeof(CamelCaseEnumConverter<RoundTripLevel>))]
public enum RoundTripLevel { Low, VeryHigh }

// Written the way app models are: CommunityToolkit.Mvvm partial properties, which the JSON source generator sees.
public sealed partial class RoundTripWindow : ObservableObject
{
    public RoundTripWindow() => Width = 800;

    [ObservableProperty] public partial int Width { get; set; }
    [ObservableProperty] public partial bool Maximized { get; set; }
    [ObservableProperty] public partial string? Monitor { get; set; }
}

public sealed partial class RoundTripPrefs : ObservableObject, IPreferencesModel
{
    [ObservableProperty] public partial RoundTripLevel Level { get; set; }
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial string? Nickname { get; set; }
    public RoundTripWindow Window { get; set; } = new();

    public void RegisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged += handler;
        Window.PropertyChanged += handler;
    }

    public void UnregisterMutationHandler(PropertyChangedEventHandler handler)
    {
        PropertyChanged -= handler;
        Window.PropertyChanged -= handler;
    }
}

[JsonSerializable(typeof(RoundTripPrefs))]
internal sealed partial class RoundTripPrefsJsonContext : JsonSerializerContext;

// [ObservableProperty] on fields: the properties exist only in CommunityToolkit's generated code, which the JSON
// source generator, running beside it, never sees.
public sealed partial class FieldStyleSection : ObservableObject
{
    [ObservableProperty] private bool _checkOnLaunch = true;
}

public sealed class FieldStylePrefs : IPreferencesModel
{
    public FieldStyleSection Status { get; set; } = new();
    public void RegisterMutationHandler(PropertyChangedEventHandler handler) => Status.PropertyChanged += handler;
    public void UnregisterMutationHandler(PropertyChangedEventHandler handler) => Status.PropertyChanged -= handler;
}

[JsonSerializable(typeof(FieldStylePrefs))]
internal sealed partial class FieldStylePrefsJsonContext : JsonSerializerContext;
