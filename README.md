# Pumpkin.DotNet

.NET SDK, runtime, and API for building C# plugins for the **[Pumpkin](https://github.com/Pumpkin-MC/Pumpkin)** Minecraft server, compiled to WebAssembly (WASI / WASM Component Model).

> **Project Status: Early Development (`alpha`)**
> This project is under active development. Public APIs, Source Generator attributes, the MSBuild SDK, and host WIT contracts are subject to breaking changes before a stable release.

---

## API Features

* **Declarative Plugin & Source Generator (`Pumpkin.DotNet.SourceGenerator`)**
  * Automatic generation of the plugin entry point (`[UnmanagedCallersOnly(EntryPoint = "init-plugin")]`) without boilerplate code.
  * Declarative event subscriptions via the `[EventHandler]` attribute (supports plugin instance methods, separate listener classes, and `static` methods).
  * Declarative Brigadier-style command trees via `[Command]`, `[Subcommand]`, `[Execute]`, `[Arg]`, and `[Suggest]` attributes, supporting `static` classes, optional parameters, value/string constraints (`Min`, `Max`, `StringArgMode`), and tab-completion suggestions.
  * Automatic dependency injection into handler parameters (`ICommandContext`, `IServer`, `ILogger`, and the event instance).
  * Compile-time Roslyn analyzer diagnostics (`PMP001`–`PMP005`) validating plugin class presence/uniqueness, constructor accessibility, handler method visibility, and `[Suggest]` method signatures.

* **Command System (Brigadier-style)**
  * Both declarative (attribute-based) and imperative (`Command.Create(...)`, `Command.Literal(...)`, `Command.Argument<T>(...)`) command construction.
  * Support for command aliases, descriptions, permission nodes (`Permission`), and operator permission levels (`PermissionLevel.Zero` – `Four`).
  * Strongly-typed argument retrieval from `ICommandContext` (`ctx.Get<T>("...")`).

* **Event System**
  * Event registration with configurable execution priority (`EventPriority`) and modification/blocking behavior (`IsModifying` / `blocking`).
  * Strongly-typed player events (`PlayerJoinEvent`, `PlayerLeaveEvent`, `PlayerChatEvent`, `AsyncPlayerChatEvent`, etc.) supporting event mutation and cancellation.

* **Tick-Based & Async Scheduler (`ISyncScheduler` / `IAsyncScheduler`)**
  * Synchronous server-tick scheduling: `Run`, `RunDelayTicks`, and `RunRepeatingTicks` (with cancellation via `IScheduledTask`).
  * Asynchronous operations bound to the server tick loop via a custom `SynchronizationContext`: `NextTickAsync`, `DelayTicksAsync`, `YieldAsync`, `RunAsync`, `RunDelayTicksAsync`, and fire-and-forget coroutines (`Spawn`) with `CancellationToken` support.

* **Players & LINQ Queries (`IServer.Players`)**
  * Lifecycle-managed caching for player wrappers (`IPlayer`).
  * A limited `IQueryable<IPlayer>` provider with simple equality lookups by `Name` or `Id`, plus `FirstOrDefault`, `Count`, and `Any`. Use `.AsEnumerable()` before applying arbitrary in-memory LINQ operators. Standard `First` and `Single` exception semantics are not implemented yet.

* **Text Components & Logging**
  * Rich text builder (`TextComponent`, `TextColor`) for player chat and system messages.
  * Structured logging via `ILogger` integrated with the Pumpkin server console.

* **Custom MSBuild SDK (`Pumpkin.DotNet.Sdk`)**
  * Preconfigured .NET / NativeAOT-LLVM WASM component build pipeline with automatic WIT component-type linking (`--component-type`) and MSBuild property metadata injection (`PluginName`, `Version`, `Authors`, `Description`).

---

## Quick Start & Source Generator API Examples

### 1. Project File (`.csproj`)

```xml
<Project Sdk="Pumpkin.DotNet.Sdk/0.1.0-alpha">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Plugin metadata exposed to the Pumpkin server -->
    <PluginName>ExamplePlugin</PluginName>
    <Version>0.1.0</Version>
    <Authors>YourName</Authors>
    <Description>Example C# plugin for Pumpkin</Description>
  </PropertyGroup>
</Project>
```

### 2. Main Plugin Class & Events (`[EventHandler]`)

The Source Generator automatically discovers your `PumpkinPlugin` subclass, binds `.csproj` metadata, and registers all `[EventHandler]` methods:

```csharp
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Tasks;

namespace ExamplePlugin;

public sealed class MyPlugin : PumpkinPlugin
{
    public override void OnLoad()
    {
        Logger.Info("Plugin loaded!");

        // Start after 20 ticks, then repeat every 100 ticks (5 seconds at 20 TPS)
        PumpkinTask.RunRepeatingTicks(20, 100, () =>
        {
            Logger.Info($"Players online: {Server.Players.Count()}");
        });
    }

    public override void OnUnload()
    {
        Logger.Info("Plugin unloaded.");
    }

    // Event instance, IServer, and ILogger are injected automatically
    [EventHandler(Priority = EventPriority.Normal)]
    public void OnPlayerJoin(PlayerJoinEvent evt, IServer server, ILogger logger)
    {
        logger.Info($"Player {evt.Player.Name} joined the server.");
        evt.Player.SendMessage(TextComponent.Text($"Welcome, {evt.Player.Name}!").WithColor(TextColor.Green));
    }
}
```

### 3. Declarative Commands (`[Command]`, `[Subcommand]`, `[Arg]`, `[Suggest]`)

Commands can be defined in a `static` class, a standard class with an accessible parameterless constructor, or directly inside your `PumpkinPlugin` class:

```csharp
using System.Collections.Generic;
using System.Linq;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Api.Text;

namespace ExamplePlugin;

[Command("kit", "kits", Description = "Claim starter kits", PermissionLevel = PermissionLevel.Zero)]
public static class KitCommand
{
    // Root command handler: /kit
    [Execute]
    public static void DefaultExecute(ICommandContext ctx)
    {
        ctx.Sender.SendMessage(TextComponent.Text("Usage: /kit claim <name> [amount]"));
    }

    // Subcommand with arguments, tab-completion, and range constraints:
    // /kit claim <kitName> [amount]
    [Subcommand("claim", PermissionLevel = PermissionLevel.Zero)]
    [Execute]
    public static int ClaimKit(
        ICommandContext ctx,
        IServer server,
        ILogger logger,
        [Arg(Name = "kitName")] [Suggest(nameof(SuggestKits))] string kitName,
        [Arg(Name = "amount", Min = 1, Max = 64)] int amount = 1)
    {
        // Fast-path IQueryable<IPlayer> lookup translated to host GetPlayer
        var target = server.Players.FirstOrDefault(p => p.Name == ctx.Sender.Name);
        if (target is null)
        {
            ctx.Sender.SendMessage(TextComponent.Text("This command can only be run by a player.").WithColor(TextColor.Red));
            return 0;
        }

        logger.Info($"Giving kit '{kitName}' x{amount} to {target.Name}");
        target.SendMessage(TextComponent.Text($"You received the {kitName} kit (x{amount})!").WithColor(TextColor.Gold));
        return 1;
    }

    // Tab-completion provider for kitName
    public static IEnumerable<string> SuggestKits(ICommandContext ctx, string input)
    {
        string[] availableKits = ["starter", "warrior", "builder"];
        return availableKits.Where(k => k.StartsWith(input, System.StringComparison.OrdinalIgnoreCase));
    }
}
```

### 4. Tick-Synchronized Async Code (`IAsyncScheduler`)

```csharp
using System.Threading;
using System.Threading.Tasks;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Tasks;

public static class CountdownHelper
{
    public static async ValueTask StartCountdownAsync(IServer server, IPlayer player, CancellationToken ct)
    {
        for (int i = 3; i > 0; i--)
        {
            player.SendMessage(TextComponent.Text($"Starting in {i}..."));
            // Wait 20 server ticks (1 second) without blocking the server thread
            await PumpkinTask.DelayTicksAsync(20, ct);
        }

        player.SendMessage(TextComponent.Text("Go!").WithColor(TextColor.Green));
    }
}
```

---

## Repository Structure

* `src/Pumpkin.DotNet.Api` — Public interfaces, attributes, event models, command builders, and scheduler abstractions.
* `src/Pumpkin.DotNet.Runtime` — Runtime implementation, event/command dispatchers, tick scheduler, and host WIT interop.
* `src/Pumpkin.DotNet.SourceGenerator` — Incremental Roslyn Source Generator and compile-time analyzer (`PMP001`–`PMP005`).
* `src/Pumpkin.DotNet.Sdk` — Custom MSBuild SDK for compiling and linking WASM plugin components.
* `tools/Pumpkin.DotNet.Tools.WitPostProcessor` — Post-processing CLI tool for generated WIT bindings.
* `samples/Pumpkin.DotNet.Sample` — Sample plugin project.
* `wit/` — Pumpkin plugin ABI WIT specifications.

---

## Building & Packing from Source

Requires a .NET SDK supporting `net11.0` with WASI / NativeAOT-LLVM workloads installed (and `just`, optional):

```bash
# Run tests
dotnet test Pumpkin.DotNet.slnx -c Release

# Pack NuGet packages (Api, Runtime, Sdk) into ./nupkgs
dotnet pack Pumpkin.DotNet.slnx -c Release -o ./nupkgs
```
