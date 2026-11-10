# AVIS
<sup>_[English version](#notice) follows_</sup>

**RDCore™** — © Copyright **9562-7303 Québec inc.** (2026)

Ce fichier accompagne le code source de RDCore et chaque archive publiée de la plateforme RDCore.

## Licences

- ⚖️ **GPLv3 ou toute version ultérieure** — voir [LICENSE-GPLv3.md](LICENSE-GPLv3.md) : **RDCore**, soit tout le code source de ce référentiel et, dans une archive de la plateforme, `rdc` et RDCore.Runtime (`RDCore.CLI/`), RDCore.LanguageServer (`RDCore.LanguageServer/`), RDCore.ParseServer (`RDCore.Parsing/`), RDCore.Diagnostics (`Extensions/RDCore.Diagnostics/`), ainsi que les manifests de la plateforme (`rdcore.json`, `extension.manifest.json`).
- ⚖️ **RDCore.SDK** — une archive de la plateforme contient aussi `RDCore.SDK.dll` et `RDCore.SDK.Protocol.dll`, avec leurs _assemblies_ satellites françaises `fr/RDCore.SDK.resources.dll` et `fr/RDCore.SDK.Protocol.resources.dll`, copiés dans le dossier de chaque composant. Ils proviennent des paquets NuGet **RDCore.SDK** et **RDCore.SDK.Protocol**, publiés sous licence **MIT** à partir de [rubberduck-vba/RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK); `THIRD-PARTY-NOTICES.txt` reproduit le texte de cette licence.
- 📚 **Descriptions de bibliothèques** — les fichiers `Symbols/*.json` décrivent les interfaces de programmation des bibliothèques qu'un projet VBA peut référencer (Excel, Word, Access, Office, MSForms, VBIDE, DAO, ADODB, Scripting, stdole) : les noms, signatures et constantes de leurs types et membres, exportés de leurs bibliothèques de types. Ils ne contiennent aucun code de ces bibliothèques. Les bibliothèques, leurs noms et leur documentation appartiennent à leurs propriétaires.

Le fichier de licence reproduit le texte officiel de la licence, en anglais. Une version française de cette licence est disponible sur demande.

Les paquets tiers redistribués dans l'archive demeurent sous leurs propres licences (voir plus bas).

### Vos projets vous appartiennent

La GPLv3 couvre RDCore lui-même, et non les projets avec lesquels il travaille. Le code source VBA et RD-VBA d'un projet, ses fichiers de projet, et tout ce que RDCore en construit, compile ou génère appartiennent à leurs auteurs respectifs, qui sont libres de les placer sous la licence de leur choix. Cela comprend toute partie de RDCore qu'une construction place dans son résultat afin que celui-ci puisse s'exécuter. Ouvrir, analyser, exécuter, déboguer ou construire un projet avec RDCore n'impose à ce projet aucune obligation de la GPLv3.

## Code tiers dans les binaires RDCore

- Les _lexers_ et _parsers_ de **RDCore.ParseServer** sont générés par ANTLR, à la compilation, à partir des grammaires `RDCore.Parsing/Syntax/*.g4` du référentiel source.
  - `RDCore.Parsing/Syntax/README.md` indique que ces grammaires sont tirées directement du _projet legacy_ **Rubberduck**, licencié sous GPLv3.
  - Les en-têtes de `VBALexer.g4` et de `VBAParser.g4` portent la mention `Copyright (C) 2014 Ulrich Wolffgang <u.wol@wwu.de>` et placent ces fichiers sous la GNU General Public License publiée par la Free Software Foundation, version 3 de la licence ou (à votre choix) toute version ultérieure. `VBAConditionalCompilation.g4`, `VBADate.g4` et `VBALike.g4` ne portent aucun en-tête.
- `RDCore.Parsing/Syntax/IdentifierNameExtensions.cs` indique que sa forme suit `Identifier.GetName` de Rubberduck (GPLv3).

## Paquets tiers et code source correspondant

- `THIRD-PARTY-NOTICES.txt` liste les paquets tiers redistribués dans l'archive, avec leurs licences.
- `SOURCE.md` indique où obtenir le code source correspondant : le référentiel et le _commit_ à partir desquels l'archive a été produite.
- Ces deux fichiers sont générés à chaque _build_ de publication; ils se trouvent à la racine de l'archive, pas dans le référentiel.

## Marques de commerce

Seul, "Rubberduck" est utilisé pour fins de référence au projet open-source legacy **utilisé publiquement ainsi depuis 2015** et sans lien ni affiliation avec tout tiers détenteur d'une marque semblable dans quelque juridiction que ce soit.  
"Rubberduck VBA", "RDCore" et "VIVAT CUCUMIS" sont des marques de commerce revendiquées par 9562-7303 Québec inc. (en attente); Toutes les marques appartiennent à leur détenteur respectif.  
RDCore n'est pas un produit de Microsoft et n'est pas affilié à Microsoft, ni directement, ni indirectement.


---

# NOTICE
<sup>_[Version française](#avis) ci-haut_</sup>

**RDCore™** — © Copyright **9562-7303 Québec inc.** (2026)

This file accompanies the RDCore source code and every published archive of the RDCore platform.

## Licences

- ⚖️ **GPLv3 or later** — see [LICENSE-GPLv3.md](LICENSE-GPLv3.md): **RDCore**, that is all the source code in this repository and, in a platform archive, `rdc` and RDCore.Runtime (`RDCore.CLI/`), RDCore.LanguageServer (`RDCore.LanguageServer/`), RDCore.ParseServer (`RDCore.Parsing/`), RDCore.Diagnostics (`Extensions/RDCore.Diagnostics/`), and the platform manifests (`rdcore.json`, `extension.manifest.json`).
- ⚖️ **RDCore.SDK** — a platform archive also contains `RDCore.SDK.dll` and `RDCore.SDK.Protocol.dll`, with their French satellite assemblies `fr/RDCore.SDK.resources.dll` and `fr/RDCore.SDK.Protocol.resources.dll`, copied into each component's folder. They come from the **RDCore.SDK** and **RDCore.SDK.Protocol** NuGet packages, published under the **MIT** licence from [rubberduck-vba/RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK); `THIRD-PARTY-NOTICES.txt` reproduces that licence's text.
- 📚 **Library descriptions** — `Symbols/*.json` describe the programming interfaces of libraries a VBA project can reference (Excel, Word, Access, Office, MSForms, VBIDE, DAO, ADODB, Scripting, stdole): the names, signatures and constants of their types and members, exported from their type libraries. They contain no code from those libraries. The libraries, their names and their documentation belong to their owners.

The licence file reproduces the official text of the licence, in English. A French version of this licence is available on request.

Third-party packages redistributed in the archive remain under their own licences (see below).

### Your projects are yours

The GPLv3 covers RDCore itself, not the projects it works with. A project's VBA and RD-VBA source code, its project files, and whatever RDCore builds, compiles or generates from them belong to their respective authors, who are free to license them as they please. This includes any part of RDCore that a build places in its output so that the output can run. Opening, analysing, running, debugging or building a project with RDCore places no obligation of the GPLv3 on that project.

## Third-party code in RDCore binaries

- The lexers and parsers of **RDCore.ParseServer** are generated by ANTLR, at build time, from the `RDCore.Parsing/Syntax/*.g4` grammars in the source repository.
  - `RDCore.Parsing/Syntax/README.md` states that these grammars are lifted directly from the _legacy **Rubberduck** project_, licensed under GPLv3.
  - The headers of `VBALexer.g4` and `VBAParser.g4` read `Copyright (C) 2014 Ulrich Wolffgang <u.wol@wwu.de>` and place those files under the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. `VBAConditionalCompilation.g4`, `VBADate.g4` and `VBALike.g4` carry no header.
- `RDCore.Parsing/Syntax/IdentifierNameExtensions.cs` notes that its shape follows Rubberduck's `Identifier.GetName` (GPLv3).

## Third-party packages and corresponding source

- `THIRD-PARTY-NOTICES.txt` lists the third-party packages redistributed in the archive, with their licences.
- `SOURCE.md` tells where to get the corresponding source: the repository and commit the archive was built from.
- Both files are generated for each release build; they are found at the root of the archive, not in the repository.

## Trademarks

If used alone, "Rubberduck" is used as a reference to the legacy open-source project **the same way it has been used publicly since 2015** and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction.  
"Rubberduck VBA", "RDCore" and "VIVAT CUCUMIS" are trademarks claimed by 9562-7303 Québec inc. (pending). All trademarks belong to their respective owners.  
RDCore is not a Microsoft product and is not affiliated with Microsoft, directly or indirectly.
