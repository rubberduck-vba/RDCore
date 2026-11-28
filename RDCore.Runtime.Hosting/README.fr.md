# RDCore.Runtime.Hosting

[![NuGet](https://img.shields.io/nuget/vpre/RDCore.Runtime.Hosting)](https://www.nuget.org/packages/RDCore.Runtime.Hosting)

<sup>_This document is available in [English](README.md)_</sup>

Ce avec quoi un hôte exécute un programme RD-VBA : la composition que `rdc compose` a écrite, chargée sans analyseur, et les machines qui l'exécutent. Il implémente le contrat d'hébergement de l'environnement d'exécution de [RDCore.SDK](https://www.nuget.org/packages/RDCore.SDK) (`RDCore.SDK.Runtime.Hosting`).

**Ceci est une préalpha.** L'API et le format de la composition peuvent changer, et une plateforme ne lit que les compositions de son propre format.

## Composer un programme

`rdc compose` analyse un espace de travail, lie et définit ses symboles, et écrit ce qu'il a défini dans un fichier de composition : `bin/<configuration>/<projet>.rdcomposition`, ou le fichier que nomme `--output`. Les descriptions des bibliothèques que le projet référence vont dans le dossier `Symbols` à côté. Un module qui ne s'analyse pas ou ne se charge pas est signalé, et rien n'est écrit.

```powershell
rdc compose C:\Projects\NavalBattle --configuration Release
```

Déployez le fichier de composition et son dossier `Symbols` avec l'hôte. Les fichiers source ne sont pas nécessaires.

## L'exécuter

```csharp
await using var program = await ComposedProgramFile.LoadAsync(@"bin\Release\NavalBattle.rdcomposition");
await using var machine = await program.CreateMachineAsync(cancellationToken);
var result = await machine.InvokeAsync(new ProgramInvocation("Program", "Total", arguments), cancellationToken);
```

| Membre | Ce qu'il fait |
|---|---|
| `ComposedProgramFile.LoadAsync` | Lit la composition. Lève `InvalidDataException` pour un fichier qui n'en est pas une, ou pour une composition d'un autre format. |
| `IComposedProgram.Documents` | Les fichiers du programme, chacun avec son analyse, tels qu'ils étaient quand le programme a été composé. |
| `IComposedProgram.CreateMachineAsync` | Crée une machine : une session d'exécution qui lui est propre, composée à partir de la composition. Les machines peuvent être créées en parallèle, et s'exécuter en même temps, chacune sur son propre fil d'exécution. |
| `IProgramMachine.InvokeAsync` | Exécute une procédure par son nom, avec ses arguments et son résultat en JSON. Une machine exécute une invocation à la fois. Une invocation annulée se termine par `Interrupted`, et une instruction `End` par `Halted`. |
| `IProgramMachine.ResetAsync` | Remet la session de la machine dans l'état où le programme a été composé. |
| `IProgramMachine.DisposeAsync` | Libère la session de la machine, une fois terminée l'invocation en cours, s'il y en a une. Elle peut être appelée plus d'une fois. |

Disposer le programme ne dispose aucune de ses machines.

## Le monde extérieur

Une machine crée ses formulaires, les objets des serveurs d'automatisation et ses appels natifs dans un hôte externe : `rdc`, exécuté en mode externe, comme `rdc run` exécute un programme. Il est démarré la première fois qu'une machine en a besoin. `ComposedProgramOptions.ExternalHost` nomme l'exécutable `rdc`, qui est celui à côté des assemblys de l'hôte lorsqu'il n'est pas donné.

## Licence

GPL-3.0-or-later. Le paquet ne contient aucun analyseur : un hôte qui le référence exécute un programme à partir de sa composition seule.
