using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
// the static front end of MS-VBAL, which was part of this assembly: what it uses of the model that is not the model's public API.
[assembly: InternalsVisibleTo("RDCore.Runtime.LanguageCore")]
