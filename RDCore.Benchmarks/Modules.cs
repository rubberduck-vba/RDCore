using System.Text;

namespace RDCore.Benchmarks;

// the modules the benchmarks read: a generated big module, and the modules of rdc bench's corpus.
public static class Modules
{
    public const string BigName = "Big";

    // a standard module of 8-line functions, each with a parameter, a local, an assignment and an If block: the shape of the keystroke measurement's module.
    public static string Big(int functions = 300)
    {
        var text = new StringBuilder($"Attribute VB_Name = \"{BigName}\"\r\nOption Explicit\r\n\r\n");
        for (var i = 1; i <= functions; i++)
        {
            text.Append($"Public Function F{i}(ByVal a As Long) As Long\r\n    Dim b As Long\r\n    b = a + {i}\r\n    If b > 10 Then\r\n")
                .Append($"        b = b - 1\r\n    End If\r\n    F{i} = b\r\nEnd Function\r\n");
        }

        return text.ToString();
    }

    // a module of the corpus rdc bench embeds, by its file name.
    public static string Corpus(string fileName)
    {
        using var stream = typeof(RDCore.CLI.App.Bench.BenchRunner).Assembly.GetManifestResourceStream($"Bench/Corpus/{fileName}")
            ?? throw new ArgumentException($"No corpus module named {fileName}.", nameof(fileName));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
