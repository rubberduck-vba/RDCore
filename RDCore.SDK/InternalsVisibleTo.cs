using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
// the static front end of MS-VBAL: what it uses of the model beyond the model's public API.
[assembly: InternalsVisibleTo("RDCore.Runtime.LanguageCore")]
