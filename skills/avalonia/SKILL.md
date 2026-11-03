---
name: avalonia
description: >
  Avalonia UI 12 for cross-platform .NET desktop apps: compiled bindings
  (x:DataType, on by default in 12), StyledProperty/DirectProperty, custom and
  templated controls, ControlThemes, DataTemplates/ViewLocator, Generic Host +
  DI, StorageProvider/Clipboard, Native AOT/trimming, and packaging. Load when
  the user mentions "Avalonia", "AXAML", "x:DataType", "compiled binding",
  "StyledProperty", "TemplatedControl", "ViewLocator", or "cross-platform desktop".
---

# Avalonia UI (v12)

Verified against Avalonia 12.1.x. Avalonia 12 requires .NET 8+ (.NET 10 recommended) and makes compiled bindings the default. Pair this skill with `desktop-mvvm` for view-model patterns.

## Core Principles

1. **Compiled bindings everywhere** — In Avalonia 12, `{Binding}` in XAML compiles by default (`AvaloniaUseCompiledBindingsByDefault=true`). Every view and `DataTemplate` needs `x:DataType`. Binding typos become build errors instead of silent blank fields, and there's no reflection, so trimming and AOT keep working.
2. **Views are mapped to view models at compile time** — Use typed `DataTemplate`s, or a dictionary-based `ViewLocator`. The template's reflection `ViewLocator` (`Type.GetType` + `Activator.CreateInstance`) breaks under trimming.
3. **`UserControl` for composition, `TemplatedControl` for reusable controls** — A lookless templated control can be restyled by consumers. A `UserControl` is just a reusable chunk of a screen.
4. **Platform services come from `TopLevel`** — Use `TopLevel.GetTopLevel(visual)` to get the file picker (`StorageProvider`), `Clipboard`, and `Launcher`. These work on Windows, macOS, Linux, and browser. Never reach for Win32/WinForms dialogs.
5. **Build and test on every target OS** — Fonts, DPI, window chrome, and file paths differ by platform. A CI matrix (windows/macos/ubuntu) catches problems before users do.

## Patterns

### Startup with Generic Host

```csharp
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Must stay public static: the XAML previewer calls it
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<OrdersViewModel>();
        var host = builder.Build();
        host.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = host.Services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.Exit += (_, _) => host.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

**Why:** `OnFrameworkInitializationCompleted` is the composition root, the point where the platform is ready but no window exists yet. The `IClassicDesktopStyleApplicationLifetime` check keeps the same `App` usable on mobile and browser lifetimes. If you call `UseSkia()` explicitly and see "No text shaping system configured", add `Avalonia.HarfBuzz` and `.UseHarfBuzz()`, a 12.0 change.

### Compiled Bindings with `x:DataType`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Contoso.Desktop.ViewModels"
             x:Class="Contoso.Desktop.Views.OrdersView"
             x:DataType="vm:OrdersViewModel">
    <DockPanel>
        <TextBox DockPanel.Dock="Top" Text="{Binding CustomerName}" PlaceholderText="Customer" />
        <Button DockPanel.Dock="Top" Content="Save" Command="{Binding SaveCommand}" />
        <ListBox ItemsSource="{Binding Orders}" SelectedItem="{Binding SelectedOrder}">
            <ListBox.ItemTemplate>
                <DataTemplate x:DataType="vm:OrderRow">
                    <TextBlock Text="{Binding Number}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </DockPanel>
</UserControl>
```

**Why:** the XAML compiler resolves `CustomerName` against `OrdersViewModel` at build time. Each `DataTemplate` changes the data context, so it declares its own `x:DataType`. In 12, `TextBox.Watermark` was renamed `PlaceholderText`. Use `{ReflectionBinding}` only for a genuinely dynamic path, and treat each one as a trimming liability.

### Mapping View Models to Views (Trim-Safe)

```xml
<!-- App.axaml: typed templates, resolved at compile time -->
<Application.DataTemplates>
    <DataTemplate DataType="vm:OrdersViewModel"><views:OrdersView /></DataTemplate>
    <DataTemplate DataType="vm:SettingsViewModel"><views:SettingsView /></DataTemplate>
</Application.DataTemplates>
```

```csharp
// If you prefer a ViewLocator: explicit factories, no reflection
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(OrdersViewModel)] = () => new OrdersView(),
        [typeof(SettingsViewModel)] = () => new SettingsView(),
    };

    public Control? Build(object? param) =>
        param is not null && Views.TryGetValue(param.GetType(), out var factory)
            ? factory()
            : new TextBlock { Text = $"No view for {param?.GetType().Name}" };

    public bool Match(object? data) => data is ObservableObject;
}
```

**Why:** `<ContentControl Content="{Binding Current}" />` (see navigation in `desktop-mvvm`) then renders the right view. The template's name-convention locator compiles but fails at runtime once trimmed, because the trimmer removes "unused" view types.

### StyledProperty vs DirectProperty

```csharp
public sealed class Badge : TemplatedControl
{
    // StyledProperty: settable from styles/themes, supports inheritance and animation
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<Badge, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<Badge, IBrush?>(nameof(Accent));

    // DirectProperty: plain backing field, cheap, NOT settable from styles — use for read-only state
    public static readonly DirectProperty<Badge, bool> IsEmptyProperty =
        AvaloniaProperty.RegisterDirect<Badge, bool>(nameof(IsEmpty), o => o.IsEmpty);

    private bool isEmpty = true;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool IsEmpty
    {
        get => isEmpty;
        private set => SetAndRaise(IsEmptyProperty, ref isEmpty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            IsEmpty = string.IsNullOrEmpty(Text);
            PseudoClasses.Set(":empty", IsEmpty);
        }
    }
}
```

**Why:** styled properties go through Avalonia's value-priority system (local value > style > theme > inherited > default), which costs a little but enables theming. Direct properties skip that system. Pseudo-classes such as `:empty` let themes style a state without extra properties.

### Templated Control with a ControlTheme

```xml
<!-- Themes/Badge.axaml, merged into App.axaml resources -->
<ControlTheme x:Key="{x:Type local:Badge}" TargetType="local:Badge">
    <Setter Property="Accent" Value="{DynamicResource SystemAccentColor}" />
    <Setter Property="Template">
        <ControlTemplate>
            <Border Name="PART_Border" Background="{TemplateBinding Accent}" CornerRadius="8" Padding="6,2">
                <TextBlock Text="{TemplateBinding Text}" />
            </Border>
        </ControlTemplate>
    </Setter>
    <Style Selector="^:empty /template/ Border#PART_Border">
        <Setter Property="IsVisible" Value="False" />
    </Style>
</ControlTheme>
```

```csharp
// Same Badge class as above, shown here for the template part only
[TemplatePart("PART_Border", typeof(Border))]
public sealed class Badge : TemplatedControl
{
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var border = e.NameScope.Find<Border>("PART_Border"); // may be null if a consumer re-templated
    }
}
```

**Why:** a `ControlTheme` keyed by type is the control's default look, and consumers replace it without subclassing. `TemplateBinding` is the cheap one-way link from template to control. Always tolerate a missing `PART_` because consumers are free to re-template.

### Cross-Platform Services via `TopLevel`

```csharp
public sealed class AvaloniaDialogService(Func<TopLevel> topLevel) : IFileDialogService
{
    public async Task<Stream?> OpenCsvAsync()
    {
        var files = await topLevel().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import orders",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        return files is [var file] ? await file.OpenReadAsync() : null;
    }
}
```

**Why:** `IStorageProvider` works on every platform, including sandboxed macOS and the browser, where raw paths aren't available. The old `OpenFileDialog`/`SaveFileDialog` classes were removed in 12. Get the `TopLevel` with `TopLevel.GetTopLevel(control)`, since casting the visual root to `TopLevel` no longer works in 12.

### Native AOT and Trimming

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>          <!-- app project only -->
  <IsAotCompatible>true</IsAotCompatible> <!-- every project, enables the analyzers -->
</PropertyGroup>
```

Checklist:
- Compiled bindings only (12 default); audit any `{ReflectionBinding}` and `x:CompileBindings="False"`
- Trim-safe view mapping (typed `DataTemplate`s or the dictionary `ViewLocator`)
- CommunityToolkit.Mvvm generators (no reflection), source-generated JSON (`native-aot` skill)
- Don't load XAML at runtime (`AvaloniaRuntimeXamlLoader`); add `TrimmerRootAssembly` only for assemblies you create controls from dynamically

**Why:** Avalonia supports Native AOT, which gives fast cold start and no runtime install, a real advantage over WPF. But one reflection binding or locator becomes a runtime crash that only shows up in the published build. Always smoke-test the AOT output.

### Packaging

```bash
dotnet publish src/Contoso.Desktop -c Release -r win-x64
dotnet publish src/Contoso.Desktop -c Release -r osx-arm64
dotnet publish src/Contoso.Desktop -c Release -r linux-x64
```

**Why:** publish per RID on that OS (Native AOT needs the native toolchain of the target OS). macOS needs an `.app` bundle (Info.plist + icon) plus signing and notarization. On Windows, sign the exe. For installers and auto-update across all three OSes, use a dedicated tool such as Velopack rather than hand-rolled update code.

## Anti-patterns

### Bindings Without `x:DataType`

```xml
<!-- BAD — 12 compiles bindings by default: no x:DataType = build error; ReflectionBinding hides typos -->
<TextBlock Text="{ReflectionBinding CustmerName}" />

<!-- GOOD — typed, validated at build time -->
<UserControl x:DataType="vm:OrdersViewModel">
    <TextBlock Text="{Binding CustomerName}" />
</UserControl>
```

### Bindings in C# Using the `Binding` Class

```csharp
// BAD — in Avalonia 12, Binding always means ReflectionBinding
textBlock.Bind(TextBlock.TextProperty, new Binding("CustomerName"));

// GOOD — explicit compiled binding
textBlock.Bind(TextBlock.TextProperty,
    CompiledBinding.Create((OrdersViewModel vm) => vm.CustomerName));
```

### Platform-Specific APIs in Shared UI Code

```csharp
// BAD — Windows-only, crashes on macOS/Linux
System.Diagnostics.Process.Start("explorer.exe", folder);

// GOOD — launcher from TopLevel (desktop OSes; sandboxed targets need IStorageFolder overloads)
await TopLevel.GetTopLevel(this)!.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
```

### Touching Controls from Background Threads

```csharp
// BAD — throws: call from invalid thread
_ = Task.Run(() => statusText.Text = "Done");

// GOOD — bind to a view model property and await the work; or dispatch explicitly
await Dispatcher.UIThread.InvokeAsync(() => Status = "Done");
```

In Avalonia 12 each thread can have its own dispatcher. App code with a single UI thread can use `Dispatcher.UIThread`, but reusable control libraries should use the control's `Dispatcher` property.

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New cross-platform desktop app | Avalonia 12 + CommunityToolkit.Mvvm + Generic Host |
| Binding a view | `x:DataType` on the root and on every `DataTemplate` |
| Truly dynamic binding path | `{ReflectionBinding}`, isolated and not in AOT builds |
| VM → View mapping | Typed `DataTemplate`s in `App.axaml` (or dictionary `ViewLocator`) |
| Reusable, restylable control | `TemplatedControl` + `ControlTheme` + `StyledProperty` |
| One-off screen section | `UserControl` |
| Computed / read-only control state | `DirectProperty` + pseudo-class |
| File open/save | `TopLevel.StorageProvider` |
| Fastest startup, no runtime install | Native AOT publish per RID |
| Installers + auto-update | Velopack (or platform-native packaging) |
| Migrating from 11.x | Read the official "Breaking changes in Avalonia 12" page first: bindings, clipboard, `TopLevel`, and the removed dialogs all changed |
| Developer tools | `AttachDeveloperTools()` from `AvaloniaUI.DiagnosticsSupport` (replaces `AttachDevTools`) |
