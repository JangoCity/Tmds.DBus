[![Travis](https://api.travis-ci.org/tmds/Tmds.DBus.svg?branch=master)](https://travis-ci.org/tmds/Tmds.DBus)
[![NuGet](https://img.shields.io/nuget/v/Tmds.DBus.svg)](https://www.nuget.org/packages/Tmds.DBus)

# Introduction

From https://www.freedesktop.org/wiki/Software/dbus/

> D-Bus is a message bus system, a simple way for applications to talk to one another. In addition to interprocess
communication, D-Bus helps coordinate process lifecycle; it makes it simple and reliable to code a "single instance"
application or daemon, and to launch applications and daemons on demand when their services are needed.

Higher-level bindings are available for various popular frameworks and languages (Qt, GLib, Java, Python, etc.).
[dbus-sharp](https://github.com/mono/dbus-sharp) (a fork of [ndesk-dbus](http://www.ndesk.org/DBusSharp)) is a C#
implementation which targets Mono and .NET 2.0. Tmds.DBus builds on top of the protocol implementation of dbus-sharp
and provides an API based on the asynchronous programming model introduced in .NET 4.5. The library targets
netstandard 1.5 which means it runs on .NET 4.6.1 (Windows 7 SP1 and later) and .NET Core. You can get Tmds.DBus from NuGet.

# Example

In this section we build an example console application that writes a message when a network interface changes state.
To detect the state changes we use the NetworkManager daemon's D-Bus service.

The steps include using the `Tmds.DBus.Tool` to generate code and then enhancing the generated code.

We use the dotnet cli to create a new console application:

```
$ dotnet new console -o netmon
$ cd netmon
```

Now we add references to `Tmds.DBus` and `Tmds.DBus.Tool`. in `netmon.csproj`. We also set the `LangVersion` to be able to create an `async Main` (C# 7.1).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.0</TargetFramework>
    <LangVersion>7.1</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Tmds.DBus" Version="0.5.0-*" />
    <DotNetCliToolReference Include="Tmds.DBus.Tool" Version="0.1.0-*" />
  </ItemGroup>
</Project>
```

Let's restore to fetch these dependencies:

```
$ dotnet restore
```

Next, we use the `list` command to find out some information about the NetworkManager service:
```
$ dotnet dbus list --bus system services | grep NetworkManager
org.freedesktop.NetworkManager
$ dotnet dbus list --bus system --service org.freedesktop.NetworkManager objects | head -2
/org/freedesktop : org.freedesktop.DBus.ObjectManager
/org/freedesktop/NetworkManager : org.freedesktop.NetworkManager
```

These command show us that the `org.freedesktop.NetworkManager` service is on the `system` bus
and has an entry point object at `/org/freedesktop/NetworkManager` which implements `org.freedesktop.NetworkManager`.

Now we'll invoke the `codegen` command to generate C# interfaces for the NetworkManager service.

```
$ dotnet dbus codegen --bus system --service org.freedesktop.NetworkManager
```

This generates a `NetworkManager.DBus.cs` file in the local folder.

We update `Program.cs` to have an async `Main` and instiantiate an `INetworkManager` proxy object.

```C#
using System;
using Tmds.DBus;
using NetworkManager.DBus;
using System.Threading.Tasks;

namespace netmon
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Monitoring network state changes. Press Ctrl-C to stop.");

            var networkManager = Connection.System.CreateProxy<INetworkManager>("org.freedesktop.NetworkManager", "/org/freedesktop/NetworkManager");

            await Task.Delay(int.MaxValue);
        }
    }
}
```

If we look at the `INetworkManager` interface in `NetworkManager.DBus.cs`, we see it has a `GetDevicesAsync` method.

```C#
Task<ObjectPath[]> GetDevicesAsync();
```

This method is returning an `ObjectPath[]`. These paths refer to other objects of the D-Bus service. We can use these
paths with `CreateProxy`. Instead, we'll update the method to reflect it is returning `IDevice` objects.

```C#
Task<IDevice[]> GetDevicesAsync();
```

We can now add the code to iterate over the devices and add a signal handler for the state change:

```C#
foreach (var device in await networkManager.GetDevicesAsync())
{
    var interfaceName = await device.GetInterfaceAsync();
    await device.WatchStateChangedAsync(
        change => Console.WriteLine($"{interfaceName}: {change.oldState} -> {change.newState}")
    );
}
```

When we run our program and change our network interfaces (e.g. turn on/off WiFi) notifications show up:

```
$ dotnet run
Press any key to close the application.
wlp4s0: 100 -> 20
```

If we look up the documentation of the StateChanged signal, we find the meaning of the magical constants: [enum `NMDeviceState`](https://developer.gnome.org/NetworkManager/stable/nm-dbus-types.html#NMDeviceState).

We can model this enumeration in C#:
```C#
enum DeviceState : uint
{
    Unknown = 0,
    Unmanaged = 10,
    Unavailable = 20,
    Disconnected = 30,
    Prepare = 40,
    Config = 50,
    NeedAuth = 60,
    IpConfig = 70,
    IpCheck = 80,
    Secondaries = 90,
    Activated = 100,
    Deactivating = 110,
    Failed = 120
}
```

We add the enum to `NetworkManager.DBus.cs` and then update the signature of the `WatchStateChangedAsync` so it
uses `DeviceState` instead of `uint`.

```C#
Task<IDisposable> WatchStateChangedAsync(Action<(DeviceState newState, DeviceState oldState, uint reason)> action);
```

When we run our application again, we see more meaningful messages.

```
$ dotnet run
Press any key to close the application.
wlp4s0: Activated -> Unavailable
```

# Further Reading

* [D-Bus](docs/dbus.md): Short overview of D-Bus.
* [Tmds.DBus Modelling](docs/modelling.md): Describes how to model D-Bus types in C# for use with Tmds.DBus.
* [Tmds.DBus.Tool](docs/tool.md): Documentation of dotnet dbus tool.