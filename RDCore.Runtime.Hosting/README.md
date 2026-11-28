# RDCore.Runtime.Hosting

[![NuGet](https://img.shields.io/nuget/vpre/RDCore.Runtime.Hosting)](https://www.nuget.org/packages/RDCore.Runtime.Hosting)

<sup>_Ce document est disponible en [français](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.Runtime.Hosting/README.fr.md)_</sup>

What a host runs an RD-VBA program with: the composition `rdc compose` wrote, loaded with no parser, and the machines that run it. It implements the runtime hosting contract of [RDCore.SDK](https://www.nuget.org/packages/RDCore.SDK) (`RDCore.SDK.Runtime.Hosting`).

**This is a pre-alpha.** The API and the composition format can change, and a platform reads only the compositions of its own format.

## Composing a program

`rdc compose` parses a workspace, binds and defines its symbols, and writes what it defined to a composition file: `bin/<configuration>/<project>.rdcomposition`, or the file `--output` names. The descriptions of the libraries the project references go in the `Symbols` folder beside it. A module that does not parse or load is reported, and nothing is written.

```powershell
rdc compose C:\Projects\NavalBattle --configuration Release
```

Deploy the composition file and its `Symbols` folder with the host. The source files are not needed.

## Running it

```csharp
await using var program = await ComposedProgramFile.LoadAsync(@"bin\Release\NavalBattle.rdcomposition");
await using var machine = await program.CreateMachineAsync(cancellationToken);
var result = await machine.InvokeAsync(new ProgramInvocation("Program", "Total", arguments), cancellationToken);
```

| Member | What it does |
|---|---|
| `ComposedProgramFile.LoadAsync` | Reads the composition. Throws `InvalidDataException` for a file that is not one, or for a composition of another format. |
| `IComposedProgram.Documents` | The program's files, each with its parse, as they were when the program was composed. |
| `IComposedProgram.CreateMachineAsync` | Makes a machine: a runtime session of its own, composed from the composition. Machines can be made concurrently, and run at the same time, each on its own thread. |
| `IProgramMachine.InvokeAsync` | Runs one procedure by name, with its arguments and its result as JSON. A machine runs one invocation at a time. A cancelled invocation ends as `Interrupted`, and an `End` statement as `Halted`. |
| `IProgramMachine.ResetAsync` | Puts the machine's session back in the state the program was composed in. |
| `IProgramMachine.DisposeAsync` | Releases the machine's session, once any invocation that is running ends. It can be called more than once. |

Disposing the program disposes none of its machines.

## The outside world

A machine makes its forms, the objects of automation servers and its native calls in an external host, a process started the first time a machine needs it. `ComposedProgramOptions.ExternalHost` names its executable: `rdc`, run in external mode as `rdc run` runs a program, or the host itself. It is the `rdc` beside the host's own assemblies when it is not given.

A host that deploys no `rdc` is its own external host, as a built program is. Its `Main` runs as the external host when it was started as one, and names its own executable:

```csharp
public static int Main(string[] args)
{
    if (ProgramExternalHost.IsThisProcess)
    {
        return ProgramExternalHost.Run(args);
    }

    var options = new ComposedProgramOptions { ExternalHost = ProgramExternalHost.Executable };
    // load the program with the options, and run the host
}
```

`ProgramExternalHost.Executable` is `null` for a host run with `dotnet`, whose executable cannot run as an external host. Forms are made in RDForms on every operating system. Automation servers and native calls are Windows ones.

## Licence

GPL-3.0-or-later. The package holds no parser: a host that references it runs a program from its composition alone.
