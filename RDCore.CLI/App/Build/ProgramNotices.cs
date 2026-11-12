using System.IO.Abstractions;
using System.Text;
using System.Text.Json;

namespace RDCore.CLI.App.Build;

// the license terms a built program's folder carries: RDCore's notice and license as the platform root has them, the RDCore.SDK package's license, and the
// notices of exactly the third-party packages the folder holds, and of the .NET application host when it holds an executable. The packages, their licenses
// and their texts are the data the release writes beside the platform's own THIRD-PARTY-NOTICES.txt (tools/release/New-ThirdPartyNotices.ps1 -DataFile):
// nothing here decides a license.
internal sealed class ProgramNotices(IFileSystem fileSystem, string platformRoot)
{
    public const string Notice = "NOTICE.md";
    public const string License = "LICENSE-GPLv3.md";
    public const string SdkLicense = "LICENSE-MIT.md";
    public const string ThirdPartyNotices = "THIRD-PARTY-NOTICES.txt";
    public const string ThirdPartyData = "THIRD-PARTY-NOTICES.json";

    private const string SdkPackage = "RDCore.SDK";
    private const string LicenseKind = "license";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // writes them into the folder of the program, whose runtime is the named assemblies and whose executable, if it has one, is a copy of the host; the
    // names of those it has nothing to write from.
    public IReadOnlyList<string> Write(string folder, string program, IReadOnlyCollection<string> assemblies, string? host, string? executable, string runtime)
    {
        var lacking = new List<string>();
        foreach (var document in new[] { Notice, License })
        {
            var source = fileSystem.Path.Combine(platformRoot, document);
            if (fileSystem.File.Exists(source))
            {
                fileSystem.File.Copy(source, fileSystem.Path.Combine(folder, document), overwrite: true);
            }
            else
            {
                lacking.Add(document);
            }
        }

        if (Read() is not { } data)
        {
            lacking.AddRange([SdkLicense, ThirdPartyNotices]);
            return lacking;
        }

        var held = new HashSet<string>(assemblies, StringComparer.OrdinalIgnoreCase);
        var packages = data.Packages.Where(package => package.Assemblies.Any(held.Contains)).ToList();
        var names = packages.Select(NameOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var texts = data.Texts
            .Where(text => text.Packages.Any(names.Contains))
            .Select(text => new Text(text.Kind, text.Title, [.. text.Packages.Where(names.Contains)], [.. text.Sources], text.Text))
            .ToList();

        var sdk = packages.FirstOrDefault(package => string.Equals(package.Package, SdkPackage, StringComparison.OrdinalIgnoreCase));
        if (sdk is not null && texts.FirstOrDefault(text => text.Kind == LicenseKind && text.Packages.Contains(NameOf(sdk))) is { } sdkLicense)
        {
            fileSystem.File.WriteAllText(fileSystem.Path.Combine(folder, SdkLicense), $"{sdkLicense.Body.TrimEnd('\n')}\n", Utf8);
        }
        else
        {
            lacking.Add(SdkLicense);
        }

        var rows = packages.Select(package => new Row(package.Package, package.Version, package.License, package.Copyright, package.ProjectUrl)).ToList();
        if (executable is not null && host is not null && data.ApplicationHost is { } application)
        {
            var (id, version) = AppHost.PackOf(host, runtime);
            rows.Add(new Row(id, version, application.License, application.Copyright, application.ProjectUrl));
            texts.AddRange(application.Texts.Select(text => new Text(text.Kind, text.Title, [$"{id} {version}"], [text.Source], text.Text)));
        }
        else
        {
            executable = null;
        }

        fileSystem.File.WriteAllText(fileSystem.Path.Combine(folder, ThirdPartyNotices), Render(program, packages.Count, executable, rows, texts), Utf8);
        return lacking;
    }

    private Data? Read()
    {
        var path = fileSystem.Path.Combine(platformRoot, ThirdPartyData);
        if (!fileSystem.File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(fileSystem.File.ReadAllText(path), Json) is { Schema: 1, Packages: not null, Texts: not null } data ? data : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NameOf(PackageData package) => $"{package.Package} {package.Version}";

    // the layout of the platform's THIRD-PARTY-NOTICES.txt: a summary table, then each text once, licenses first, with the packages it applies to.
    private static string Render(string program, int packages, string? executable, IReadOnlyList<Row> rows, IReadOnlyList<Text> texts)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line.TrimEnd()).Append('\n');

        var title = $"{program} third-party notices";
        Line(title);
        Line(new string('=', title.Length));
        Line();
        Line($"{program} runs on RDCore's runtime, which ships the third-party components listed below, each used under the license shown.");
        Line("The license texts, and the notices their authors ask to be passed on, follow the summary.");
        Line();
        Line($"{program} is framework-dependent: the .NET shared framework (Microsoft.NETCore.App) is not part of this folder and is not listed here.");
        if (executable is not null)
        {
            Line($"The .NET application host is part of it: {executable} is a copy of it, listed below.");
        }

        Line($"RDCore's own license terms are in {Notice} and {License} beside this file. {SdkPackage}, listed below, is the MIT-licensed package");
        Line($"RDCore is built on, from the same authors; {SdkLicense} beside this file is its license.");
        Line();
        Line($"Third-party packages: {packages}");
        if (executable is not null)
        {
            Line($".NET application host: {executable}");
        }

        Line();
        string[] headers = ["Package", "Version", "License", "Copyright", "Project URL"];
        var cells = rows.OrderBy(row => $"{row.Package} {row.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(row => new[] { row.Package, row.Version, row.License, row.Copyright, row.ProjectUrl })
            .ToList();
        var widths = headers.Select((header, column) => cells.Select(row => row[column].Length).Append(header.Length).Max()).ToArray();
        Line(string.Join("  ", headers.Select((header, column) => header.PadRight(widths[column]))));
        Line(string.Join("  ", widths.Select(width => new string('-', width))));
        foreach (var row in cells)
        {
            Line(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))));
        }

        // the same text for several packages is written once, for them all.
        var merged = texts
            .GroupBy(entry => (entry.Kind, entry.Title, entry.Body))
            .Select(group => new Text(group.Key.Kind, group.Key.Title,
                [.. group.SelectMany(entry => entry.Packages).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
                [.. group.SelectMany(entry => entry.Sources).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase)],
                group.Key.Body))
            .OrderBy(entry => entry.Kind == LicenseKind ? 0 : 1)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Packages[0], StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Body, StringComparer.Ordinal);
        foreach (var entry in merged)
        {
            Line();
            Line();
            Line(new string('=', 100));
            Line($"{(entry.Kind == LicenseKind ? "License" : "Notice")}: {entry.Title}");
            Line(new string('=', 100));
            Line("Applies to:");
            foreach (var package in entry.Packages)
            {
                Line($"    {package}");
            }

            Line("Text from:");
            foreach (var source in entry.Sources)
            {
                Line($"    {source}");
            }

            Line(new string('-', 100));
            foreach (var line in entry.Body.Split('\n'))
            {
                Line(line);
            }
        }

        return text.ToString();
    }

    private sealed record Row(string Package, string Version, string License, string Copyright, string ProjectUrl);

    private sealed record Text(string Kind, string Title, IReadOnlyList<string> Packages, IReadOnlyList<string> Sources, string Body);

    private sealed record Data(int Schema, IReadOnlyList<PackageData> Packages, IReadOnlyList<TextData> Texts, HostData? ApplicationHost);

    private sealed record PackageData(string Package, string Version, string License, string Copyright, string ProjectUrl, IReadOnlyList<string> Assemblies);

    private sealed record TextData(string Kind, string Title, IReadOnlyList<string> Packages, IReadOnlyList<string> Sources, string Text);

    private sealed record HostData(string License, string Copyright, string ProjectUrl, IReadOnlyList<HostTextData> Texts);

    private sealed record HostTextData(string Kind, string Title, string Source, string Text);
}
