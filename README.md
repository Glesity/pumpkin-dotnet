# 🎃 Pumpkin.NET

A custom MSBuild SDK, Roslyn Source Generator, and runtime framework for writing native WebAssembly (**WASI Component Model**) plugins for the **[Pumpkin](https://github.com/Pumpkin-MC/Pumpkin)** Minecraft server in C# (.NET 11).

**Pumpkin.DotNet** eliminates raw `wit-bindgen` boilerplate, manual FFI/memory tracking, and slow build loops—allowing you to write clean, idiomatic C# that compiles directly to a standalone `.wasm` component via **NativeAOT-LLVM**.

---

## ✨ Features

* **Declarative Plugin & Roslyn Source Generator (`Pumpkin.DotNet.SourceGenerator`)**
  * **Declarative Events:** Subscribe using `[EventHandler]` on plugin instance methods, separate listener classes, or static methods.
  * **Declarative Brigadier-Style Commands:** Build full command trees with `[Command]`, `[Subcommand]`, `[Execute]`, `[Arg]`, and `[Suggest]` attributes—supporting static classes, optional parameters, value/string constraints (`Min`, `Max`, `StringArgMode`), and tab-completion suggestions.
  * **Built-in Dependency Injection:** Automatic parameter injection for `ICommandContext`, `IServer`, `ILogger`, and event instances.
  * **Compile-Time Diagnostics (`PMP001`–`PMP005`):** Roslyn analyzers validate plugin class presence/uniqueness, constructor accessibility, handler visibility, and `[Suggest]` method signatures directly in your IDE.

* **Brigadier-Style Command System**
  * Supports both declarative (attribute-based) and imperative (`Command.Create(...)`, `Command.Literal(...)`, `Command.Argument<T>(...)`) command construction.
  * Built-in support for aliases, descriptions, permission nodes (`Permission`), operator permission levels (`PermissionLevel.Zero` – `Four`), and strongly-typed argument extraction (`ctx.Get<T>("...")`).

* **Event System**
  * Event registration with configurable execution priorities (`EventPriority`) and mutation/blocking behavior (`IsModifying`).
  * Strongly-typed player events (`PlayerJoinEvent`, `PlayerLeaveEvent`, `PlayerChatEvent`, `AsyncPlayerChatEvent`) with cancellation support.

* **Tick-Based & Async Schedulers (`ISyncScheduler` / `IAsyncScheduler`)**
  * **Synchronous Scheduling:** Run tasks immediately, delayed, or repeating on server ticks (`Run`, `RunDelayTicks`, `RunRepeatingTicks`) with cancellation via `IScheduledTask`.
  * **Tick-Bound `async/await`:** Write native asynchronous code bound directly to the server tick loop via a custom `SynchronizationContext` (`NextTickAsync`, `DelayTicksAsync`, `YieldAsync`, `RunAsync`, `RunDelayTicksAsync`, and `PumpkinTask.Spawn` coroutines with `CancellationToken` support).

* **Players, Rich Text & Logging**
  * Lifecycle-managed `IPlayer` caching and an `IQueryable<IPlayer>` provider (`IServer.Players`) supporting fast equality lookups by `Name` or `Id`, `FirstOrDefault`, `Count`, and `Any` (use `.AsEnumerable()` for arbitrary in-memory LINQ operators).
  * `TextComponent` & `TextColor` builder for formatted chat and system messages.
  * Structured logging via `ILogger` integrated with the Pumpkin server console.

* **Custom MSBuild SDK (`Pumpkin.DotNet.Sdk`) & `WitPostProcessor`**
  * Preconfigured **NativeAOT-LLVM** WASI component build pipeline with automatic `--component-type` linking and MSBuild property metadata injection (`PluginName`, `Version`, `Authors`, `Description`).
  * Powered by a custom `WitPostProcessor` that optimizes boundary memory management and eliminates redundant `wit-bindgen` generation/compilation overhead.

---

## ⚡ Build Benchmarks

Compared against a raw `wit-bindgen` C# setup on the same sample plugin (`NativeAOT` Release build):

| Metric / Feature | Raw `wit-bindgen` Setup | Pumpkin.DotNet |
| :--- | :--- | :--- |
| **Clean Build Time (Release)** | `173.7s` | **`61.7s`** (~2.8x faster) |
| **Peak CLI RAM** | `108.8 MB` | **`104.5 MB`** |
| **WASM Output Size** | `2.6 MB` | **`3.4 MB`** |
| **Entry Point & Linking** | Manual `[UnmanagedCallersOnly]` + custom `--component-type` linker flags | Handled automatically by SDK & Source Generator |
| **API Style** | Low-level generated WIT structs, manual pointer/resource disposal | Idiomatic C# (`PumpkinPlugin`, `[EventHandler]`, `[Command]`, `async/await`) |
| **Memory Management** | Manual ownership tracking across WASM boundary | Automated lifecycle caching + `WitPostProcessor` optimizations |
| **Compile-Time Checks** | None (errors surface at runtime or during linking) | Roslyn analyzers (`PMP001`–`PMP005`) |

---

## 🚀 Quick Start

### Prerequisites
* **.NET 11 SDK** (with NativeAOT-LLVM / WASI workload dependencies)
* **Pumpkin Server** with WASI plugin support enabled

### 1. Create a Plugin Project (`.csproj`)

Reference `Pumpkin.DotNet.Sdk` in your project file and define your plugin metadata:

```xml
<Project Sdk="Pumpkin.DotNet.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Plugin Metadata -->
    <PluginName>MyAwesomePlugin</PluginName>
    <Version>1.0.0</Version>
    <Authors>YourName</Authors>
    <Description>An example Pumpkin plugin written in C#</Description>
  </PropertyGroup>

</Project>
```

### 2. Write Your Plugin (`Program.cs`)

```csharp
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Tasks;
using Pumpkin.DotNet.Api.Server;

public class MyPlugin : PumpkinPlugin
{
    public override void OnLoad()
    {
        Logger.Info("Plugin loaded!");
    }

    [EventHandler]
    public void OnPlayerJoin(PlayerJoinEvent e)
    {
        Logger.Info($"{e.Player.Name} joined");
        e.Player.SendMessage(TextComponent.Text("Welcome!").WithColor(TextColor.Gold));
    }
}

[Command("countdown", Description = "Start countdown")]
public class CountdownCommand
{
    [Execute]
    public void Countdown(ICommandContext ctx, IServer server, [Arg(Min = 1, Max = 10)] int seconds = 10)
    {
        PumpkinTask.Spawn(async ct => await RunCountdownAsync(server, seconds, ct));
    }

    private static async ValueTask RunCountdownAsync(IServer server, int seconds, CancellationToken ct)
    {
        for (int i = seconds; i > 0; i--)
        {
            foreach (var player in server.Players.AsEnumerable())
            {
                player.SendMessage(TextComponent.Text($"[Countdown] {i}...").WithColor(TextColor.Yellow));
            }

            await PumpkinTask.DelayTicksAsync(20, ct);
        }

        foreach (var player in server.Players.AsEnumerable())
        {
            player.SendMessage(TextComponent.Text("[Countdown] Go!").WithColor(TextColor.Green));
        }
    }
}
```

### 3. Build & Run

Compile the plugin in `Release` configuration:

```bash
dotnet build -c Release
```

Copy the generated `.wasm` file from `bin/Release/net11.0/wasi-wasm/native/<AssemblyName>.wasm` into your Pumpkin server's `plugins/` directory and start the server.

---

## 🔍 Roslyn Diagnostics Reference

`Pumpkin.DotNet.SourceGenerator` includes built-in compile-time analyzers to prevent common runtime mistakes:

| Rule ID | Description |
| :--- | :--- |
| **`PMP001`** | Missing or multiple `PumpkinPlugin` implementations in the assembly. |
| **`PMP002`** | Plugin or listener class must have an accessible parameterless constructor. |
| **`PMP003`** | `[EventHandler]` or `[Execute]` method has invalid visibility or unsupported parameter types. |
| **`PMP004`** | Command structure or argument constraint validation error. |
| **`PMP005`** | `[Suggest]` tab-completion method signature does not match the expected delegate. |

---

## 🤝 Contributing

Contributions, bug reports, and feature requests are welcome! Feel free to open an issue or submit a pull request.