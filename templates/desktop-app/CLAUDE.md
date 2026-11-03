# [Project Name] — Desktop App

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 desktop application built with [Avalonia | WPF | WinForms | MAUI] using MVVM. View models hold all behavior and state and are unit-tested without a UI. Views are thin XAML bound to them. Target platforms: [Windows | macOS | Linux].

## Tech Stack

- **.NET 10** / C# 14
- **UI framework** — [Avalonia 12 | WPF | WinForms | MAUI]
- **CommunityToolkit.Mvvm 8.4+** — `[ObservableProperty]` partial properties, `[RelayCommand]`, `IMessenger`
- **Microsoft.Extensions.Hosting** — DI, configuration, logging, `IHttpClientFactory`
- **Publishing** — [self-contained per RID | Native AOT (Avalonia only) | single-file] + [installer/updater, e.g. Velopack]
- **xUnit v3** — view model tests (no UI thread required)

## Architecture

```
src/
  [ProjectName].Desktop/
    ViewModels/
      MainWindowViewModel.cs     # shell: hosts Navigator.Current
      [Feature]ViewModel.cs      # one per page/dialog
    Views/
      MainWindow.axaml(.xaml)
      [Feature]View.axaml(.xaml) # x:DataType set on every view
    Services/
      Navigator.cs               # view-model-first navigation
      IDialogService.cs          # dialogs/file pickers behind an interface
      [Domain]Service.cs         # business logic, no UI types
    App.axaml(.xaml) + .cs       # Generic Host composition root, VM → View DataTemplates
    Program.cs                   # (Avalonia) BuildAvaloniaApp
tests/
  [ProjectName].Desktop.Tests/
    ViewModels/
      [Feature]ViewModelTests.cs
```

### View Model Convention

```csharp
public sealed partial class OrdersViewModel(IOrderService orders, TimeProvider clock) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? CustomerName { get; set; }

    public ObservableCollection<OrderRow> Orders { get; } = [];

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task LoadAsync(CancellationToken ct)
    {
        Orders.Clear();
        await foreach (var order in orders.StreamAsync(ct))
            Orders.Add(order);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync(CancellationToken ct) => orders.CreateAsync(CustomerName!, clock.GetUtcNow(), ct);

    private bool CanSave() => !string.IsNullOrWhiteSpace(CustomerName);
}
```

## Coding Standards

- **C# 14 features** — Primary constructors, collection expressions, records, pattern matching
- **File-scoped namespaces**, `sealed` classes, `internal` by default
- **View models are UI-agnostic** — No `Window`, `MessageBox`, `Dispatcher`, brushes, or `Visibility` in view models
- **Partial properties for `[ObservableProperty]`** — Not fields
- **Async commands only** — `[RelayCommand]` on `Task`-returning methods with `CancellationToken`; never `async void` outside event handlers
- **Code-behind is `InitializeComponent` + view-only concerns** — No service calls, no business logic
- **Compiled bindings** — `x:DataType` on every view and `DataTemplate` (Avalonia/MAUI)
- **Background work** — `Task.Run` only for CPU-bound work; mutate bound collections on the UI thread
- **`TimeProvider`** — Never `DateTime.Now`

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `desktop-mvvm` — CommunityToolkit.Mvvm, Generic Host, threading, navigation, messaging
- `avalonia` — Compiled bindings, controls, ControlThemes, cross-platform services (Avalonia projects)
- `native-aot` — AOT/trimming (Avalonia only)
- `dependency-injection` — Service lifetimes and registration
- `configuration` — Options pattern and user settings
- `testing` — xUnit v3 patterns
- `ci-cd` — Per-OS build and publish matrix
- `workflow-mastery` — Parallel worktrees, verification loops, context discipline

## MCP Tools

> **Setup:** Install once globally with `dotnet tool install -g CWM.RoslynNavigator` and register with `claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution ${workspaceFolder}`.

- **Before modifying a view model** — `find_symbol` / `get_file_outline` (generated members come from the MVVM source generator)
- **To audit lifetimes** — `get_di_registrations` (singleton vs transient view models)
- **To check for errors** — `get_diagnostics` (includes MVVM Toolkit generator and trim/AOT warnings)
- **XAML/AXAML** — Read directly; Roslyn does not index markup

## Commands

```bash
# Build / test
dotnet build
dotnet test

# Run
dotnet run --project src/[ProjectName].Desktop

# Publish per OS (run on the matching OS for Native AOT)
dotnet publish src/[ProjectName].Desktop -c Release -r win-x64
dotnet publish src/[ProjectName].Desktop -c Release -r osx-arm64
dotnet publish src/[ProjectName].Desktop -c Release -r linux-x64

# Format check
dotnet format --verify-no-changes
```

## Workflow

- **Plan first** — Enter plan mode for any non-trivial task (3+ steps or design decisions).
- **View model first** — Write and test the view model before building the view.
- **Verify before done** — `dotnet build` + `dotnet test`; for AOT/trimmed builds, launch the published app.
- **Learn from corrections** — Capture corrections in memory so the same mistake never recurs.

## Anti-patterns

Do NOT generate code that:

- Puts business logic or service calls in code-behind event handlers
- Hand-writes `INotifyPropertyChanged` boilerplate — use `[ObservableProperty]`
- Uses `async void` commands, `.Result`, or `.Wait()` on the UI thread
- Adds to an `ObservableCollection` from a background thread
- References `MessageBox`, `Window`, or `Dispatcher` from a view model — use interfaces (`IDialogService`, `IUiDispatcher`)
- Uses reflection-based view location or `{ReflectionBinding}` in trimmed/AOT builds
- Resolves services through a static `App.Services` service locator inside view models — inject them
- Uses Windows-only APIs in cross-platform (Avalonia/MAUI) code without an `OperatingSystem.IsWindows()` guard
- Uses `DateTime.Now` or `new HttpClient()`
