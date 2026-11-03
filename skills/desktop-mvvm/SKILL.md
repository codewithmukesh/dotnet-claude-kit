---
name: desktop-mvvm
description: >
  MVVM for .NET desktop apps (WPF, Avalonia, WinForms, MAUI) with
  CommunityToolkit.Mvvm 8.4+ source generators: [ObservableProperty] partial
  properties, [RelayCommand] async commands, Generic Host + DI, UI-thread
  dispatching, navigation, messaging, and testable view models. Load when the
  user mentions "MVVM", "ViewModel", "WPF", "desktop app", "ObservableProperty",
  "RelayCommand", "ICommand", "INotifyPropertyChanged", "Dispatcher", or "UI thread".
---

# Desktop MVVM (.NET 10, CommunityToolkit.Mvvm)

## Core Principles

1. **View models know nothing about the UI framework** — No `Window`, `MessageBox`, `Dispatcher`, or `Visibility` in a view model. That keeps them unit-testable without a UI thread and portable between WPF, Avalonia, and MAUI.
2. **Source generators over hand-written `INotifyPropertyChanged`** — CommunityToolkit.Mvvm (8.4.x, verified) generates the property, change notification, and command boilerplate at compile time. There's no reflection, so it stays trimming- and AOT-friendly.
3. **Use partial properties, not fields** — Since 8.4, `[ObservableProperty]` goes on `partial` properties. From 8.4.1 that works on C# 14 with no `LangVersion=preview`. You get real properties that you can put attributes on and debug, instead of fields with magic names.
4. **Async commands, never `async void`** — `[RelayCommand]` on a `Task`-returning method generates an `IAsyncRelayCommand`. It disables itself while running, surfaces exceptions, and supports cancellation.
5. **Generic Host is the composition root** — DI, configuration, logging, and `IHttpClientFactory` work the same as in ASP.NET Core. Views and view models are resolved from the container, never `new`-ed in code-behind.

## Patterns

### View Model with Generated Properties and Commands

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class OrdersViewModel(IOrderService orders, TimeProvider clock) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? CustomerName { get; set; }

    [ObservableProperty]
    public partial OrderRow? SelectedOrder { get; set; }

    public ObservableCollection<OrderRow> Orders { get; } = [];

    // Generates LoadCommand (IAsyncRelayCommand) + LoadCancelCommand
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task LoadAsync(CancellationToken ct)
    {
        Orders.Clear();
        await foreach (var order in orders.StreamAsync(ct))
            Orders.Add(order); // continuation resumes on the UI thread
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync(CancellationToken ct) =>
        orders.CreateAsync(CustomerName!, clock.GetUtcNow(), ct);

    private bool CanSave() => !string.IsNullOrWhiteSpace(CustomerName);

    partial void OnSelectedOrderChanged(OrderRow? value) =>
        CustomerName = value?.Customer; // generated hook, no setter boilerplate
}
```

**Why:** the class must be `partial` so the generator can add members. `Async` is stripped from command names (`LoadAsync` → `LoadCommand`). The command is disabled while it runs (unless `AllowConcurrentExecutions = true`), so double-clicks can't start two loads. Bind a busy indicator to `LoadCommand.IsRunning` instead of keeping a hand-rolled `IsBusy` flag.

### Generic Host in WPF

Remove `StartupUri` from `App.xaml`, then own startup in `App.xaml.cs`:

```csharp
public sealed partial class App : Application
{
    private readonly IHost host = CreateHost();

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory // appsettings.json next to the exe, not the cwd
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddHttpClient<IOrderService, OrderService>();
        builder.Services.AddTransient<OrdersViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        host.Start(); // starts IHostedService background work, if any
        host.Services.GetRequiredService<MainWindow>().Show();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Process exit is the one place sync-over-async is acceptable (like Program.cs)
        host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        host.Dispose();
        base.OnExit(e);
    }
}

public sealed partial class MainWindow : Window
{
    public MainWindow(OrdersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

**Why:** one composition root shared with your backend knowledge. Constructor injection into windows keeps code-behind to `InitializeComponent` + `DataContext`. Setting the content root to `AppContext.BaseDirectory` stops a shortcut's "Start in" folder from changing which config loads. Avalonia wiring lives in the `avalonia` skill.

### UI-Thread Dispatching

`await` started on the UI thread resumes on the UI thread, so most code needs **no** dispatching. Dispatch only for callbacks that arrive on background threads (timers, `FileSystemWatcher`, sockets, message buses).

```csharp
// 1. Progress<T> captures the UI SynchronizationContext when constructed on the UI thread
[RelayCommand]
private async Task ExportAsync(CancellationToken ct)
{
    var progress = new Progress<int>(percent => ExportProgress = percent);
    await Task.Run(() => exporter.Export(Orders.ToList(), progress, ct), ct); // CPU work off the UI thread
}

// 2. A tiny abstraction for background-origin events, implemented per framework
public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action) =>
        await Application.Current.Dispatcher.InvokeAsync(action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action) =>
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
}
```

**Why:** `Progress<T>` is framework-neutral and needs zero plumbing. The `IUiDispatcher` interface keeps the `Dispatcher` type out of view models, and tests can supply an inline implementation (`action(); return Task.CompletedTask;`). `Task.Run` belongs around CPU-bound work only. Never wrap I/O that is already async.

### View-Model-First Navigation

```csharp
public sealed partial class Navigator(IServiceProvider services) : ObservableObject
{
    [ObservableProperty]
    public partial ObservableObject? Current { get; set; }

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject =>
        Current = services.GetRequiredService<TViewModel>();
}
```

```xml
<!-- Shell window: the content area follows Navigator.Current -->
<ContentControl Content="{Binding Navigator.Current}" />

<!-- App resources: one DataTemplate per view model maps VM → View -->
<DataTemplate DataType="{x:Type vm:OrdersViewModel}">
    <views:OrdersView />
</DataTemplate>
```

**Why:** view models navigate by type, and the view layer decides how each one renders. That makes navigation testable (`Assert.IsType<OrdersViewModel>(navigator.Current)`). `IServiceProvider` is confined to the navigator so it doesn't spread as a service locator. Register page view models as `Transient` for a fresh page each visit, `Singleton` to keep state across visits.

### Decoupled Messaging Between View Models

```csharp
public sealed record OrderSaved(Guid OrderId);

// Sender
messenger.Send(new OrderSaved(order.Id));

// Receiver
public sealed partial class DashboardViewModel : ObservableObject, IRecipient<OrderSaved>
{
    public DashboardViewModel(IMessenger messenger) => messenger.RegisterAll(this);

    public void Receive(OrderSaved message) => RefreshCommand.Execute(null);

    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) => Task.CompletedTask; // reload tiles
}
```

**Why:** sibling view models shouldn't reference each other. `WeakReferenceMessenger` holds recipients weakly, so a closed page's view model can still be garbage collected with no explicit unregister. Messages are immutable records.

### Validation

```csharp
public sealed partial class CustomerFormViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, MaxLength(100)]
    public partial string? Name { get; set; }

    [RelayCommand]
    private void Submit()
    {
        ValidateAllProperties();
        if (HasErrors) return;
        // save
    }
}
```

**Why:** `ObservableValidator` implements `INotifyDataErrorInfo`, which WPF, Avalonia, and MAUI all render natively. Validation rules live with the data, not in XAML.

### Testing View Models

```csharp
[Fact]
public async Task SaveCommand_EmptyCustomer_CannotExecute()
{
    // Arrange
    var vm = new OrdersViewModel(new InMemoryOrderService(), new FakeTimeProvider());

    // Act
    vm.CustomerName = "  ";

    // Assert
    Assert.False(vm.SaveCommand.CanExecute(null));
}
```

**Why:** view models are plain classes, so tests run without a UI thread or window. Use a hand-written fake for your own services (no mocking framework) and `FakeTimeProvider` for time.

### MAUI Note

MAUI uses the same view models unchanged. Register them in `MauiProgram.CreateMauiApp()` via `builder.Services`, navigate with Shell routes (`Shell.Current.GoToAsync("orders")`), and marshal background callbacks with `MainThread.InvokeOnMainThreadAsync`. Set `x:DataType` on pages for compiled bindings, as in Avalonia.

## Anti-patterns

### `async void` Commands and Blocking the UI Thread

```csharp
// BAD — exceptions crash the app, button stays enabled, .Result can deadlock the UI thread
public ICommand LoadCommand => new RelayCommand(async () => await LoadAsync());
private void Refresh() => Orders = service.GetAllAsync().Result;

// GOOD — generated async command with cancellation and re-entrancy protection
[RelayCommand]
private async Task LoadAsync(CancellationToken ct) => await ReloadAsync(ct);
```

### Mutating Bound Collections from a Background Thread

```csharp
// BAD — ObservableCollection raises CollectionChanged off the UI thread → crash
await Task.Run(() => { foreach (var o in Fetch()) Orders.Add(o); });

// GOOD — compute in the background, mutate on the UI thread
var rows = await Task.Run(() => Fetch().ToList(), ct);
foreach (var row in rows) Orders.Add(row);
```

### UI Types in View Models

```csharp
// BAD — untestable, ties the VM to WPF
if (MessageBox.Show("Delete?", "", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { }

// GOOD — an interface the view layer implements
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
}
if (await dialogs.ConfirmAsync("Delete order", $"Delete {SelectedOrder!.Number}?")) { }
```

### Hand-Written Property Boilerplate

```csharp
// BAD — 8 lines per property, typo-prone string names
private string? name;
public string? Name
{
    get => name;
    set { if (name != value) { name = value; OnPropertyChanged(nameof(Name)); } }
}

// GOOD
[ObservableProperty]
public partial string? Name { get; set; }
```

### Logic in Code-Behind

```csharp
// BAD — click handler talks to services; nothing is testable
private async void SaveButton_Click(object sender, RoutedEventArgs e) =>
    await new OrderService().SaveAsync(NameBox.Text);

// GOOD — the view binds to a command; the view model owns behavior
// <Button Content="Save" Command="{Binding SaveCommand}" />
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New cross-platform desktop app | Avalonia + CommunityToolkit.Mvvm (see `avalonia` skill) |
| Windows-only, existing WPF skills or controls | WPF + CommunityToolkit.Mvvm + Generic Host |
| Mobile + desktop from one codebase | MAUI (or Avalonia if Linux desktop is required) |
| Legacy WinForms | Keep WinForms; move logic into view models (`Button.Command` / `DataContext` binding exists since .NET 7) |
| Observable property | `[ObservableProperty] public partial T Name { get; set; }` |
| Derived property (e.g. `FullName`) | `[NotifyPropertyChangedFor(nameof(FullName))]` on its inputs |
| Button enablement depends on state | `[RelayCommand(CanExecute = ...)]` + `[NotifyCanExecuteChangedFor]` |
| Long-running command | `async Task` + `CancellationToken`, `IncludeCancelCommand = true` |
| Busy indicator | Bind to `XCommand.IsRunning` |
| CPU-heavy work | `Task.Run` + `Progress<T>`, then update state on the UI thread |
| Cross-VM notifications | `IMessenger` (`WeakReferenceMessenger`) with record messages |
| Dialogs / file pickers | `IDialogService` interface implemented in the view layer |
| Need trimming / Native AOT | Avalonia with compiled bindings; WPF/WinForms don't support trimming |
