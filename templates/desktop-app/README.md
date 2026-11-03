# Desktop App Template

## When to Use

Use this template when you're building:
- A cross-platform desktop app with Avalonia (Windows, macOS, Linux)
- A Windows desktop app with WPF or WinForms
- A MAUI app whose view models should follow the same MVVM conventions
- Internal line-of-business tools, admin consoles, or editors with a rich UI

## How to Use

1. Copy `CLAUDE.md` into the root of your project
2. Replace `[Project Name]` and `[ProjectName]`
3. Pick the UI framework, target platforms, and publishing model, then delete the alternatives
4. Remove the `avalonia` and `native-aot` skill references if you use WPF or WinForms

Or run `/dotnet-init`. It detects `UseWPF`, `UseWindowsForms`, `UseMaui`, or an Avalonia reference and generates this file for you.

## What's Included

This template configures Claude Code to:
- Write view models with CommunityToolkit.Mvvm source generators (partial properties, async relay commands)
- Use the Generic Host as the composition root for DI, configuration, and logging
- Keep view models free of UI types so they're unit-testable without a UI thread
- Handle UI-thread affinity correctly (async continuations, `Progress<T>`, explicit dispatch only for background events)
- Navigate view-model-first, with views mapped through typed `DataTemplate`s
- Use compiled bindings (`x:DataType`) and trim-safe patterns for Avalonia Native AOT
- Publish per OS and package with an installer/updater

## Customization

### WPF or WinForms Only

Delete the Avalonia `Program.cs` entry, change `.axaml` to `.xaml`, and remove `native-aot`. Neither framework supports trimming. Use ReadyToRun or single-file publishing for faster startup.

### MAUI

Register services in `MauiProgram.CreateMauiApp()` and navigate with Shell routes. View model conventions stay the same.
