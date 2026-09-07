# CONTRIBUER
<sup>_[English version](#contributing) follows_</sup>

👋 **Merci pour votre intérêt!** 

Assurez-vous d'abord d'avoir lu et accepté les termes de [l'_Entente de Licence Contributeur_ ("CLA")](CLA.fr.md).

## Éléments clés

- 🧠 **VOUS DÉCLAREZ** que VOUS ÊTES L'AUTEUR UNIQUE de vos contributions _et que celles-ci sont LIBRES DE DROITS externes_ (i.e. vous effectuez ces contributions en votre propre nom et sans dépendances ou implications légales externes);
  - Vous pouvez donc expliquer vos contributions, justifier vos décisions, et effectuer par vous-même la maintenance de vos contributions.
- 🔒 **VOUS CONSERVEZ** votre copyright _sur vos contributions_ - mais la plateforme et ses marques de commerces (détenues ou revendiquées) ne sont pas négociables;
- ⚖️ **VOS CONTRIBUTIONS DEMEURENT OPEN-SOURCE** sous **⚖️MIT** (RDCore.SDK) ou **⚖️GPLv3** (tout autre projet sous ce référentiel);
- 🤝 **VOUS AUTORISEZ L'INTÉGRATION** de vos contributions par le détenteur de la plateforme dans une **VERSION PRIVÉE SOUS LICENCE COMMERCIALE**.

Ce dernier point constituant en toute transparence **l'intérêt commercial** motivant la publication et la réalisation de ce projet.

### Je voudrais contribuer, mais _au nom de ma compagnie ou de mon employeur_

- ⏳ **Envisageable sans problème** dès la publication d'une _CLA Corporative_, dont la parution est prévue à cet effet.


### Contributions assistées par IA

- ✅ **Oui — utilisez l'assistance de votre choix**, y compris l'implémentation de fonctionnalités complètes par un agent. L'essentiel est qu'**un·e contributeur·rice humain·e assume le résultat**.
- ❌ **Aucune soumission autonome.** Un agent qui ouvre une _pull request_ de lui-même, ou du code que la personne qui le soumet ne peut pas expliquer, justifier et maintenir, sera refusé.

Le critère n'est pas la *quantité* de code produite par l'IA — c'est la **paternité et l'imputabilité**. En soumettant, vous faites la même déclaration que tout le monde (voir **Éléments clés** ci-haut et la [CLA](CLA.fr.md)): le travail est le vôtre, vous le comprenez en entier, vous en assumez la conception, et vous en ferez la maintenance. Peu importe le chemin parcouru, **c'est vous** qui ouvrez la _pull request_ et **c'est vous** qui en répondez en révision.

La complétion automatique de type _Copilot_ et l'usage d'un assistant pour explorer ou valider des idées d'implémentation, de solutions et d'architectures demeurent, comme toujours, **fortement encouragés**.


## Bâtir et tester

RDCore cible **.NET 10**; le [SDK .NET 10](https://dotnet.microsoft.com/download) constitue le seul prérequis.

À partir de la racine du référentiel:

```powershell
dotnet build RDCore.slnx
dotnet test RDCore.slnx
```

Une exécution réussie **doit rapporter un nombre de tests non nul**: une exécution qui ne découvre aucun test n'est pas une exécution réussie. L'option `--configuration Release` bâtit et teste de la même manière; l'intégration continue valide les deux configurations.


**V I V A T 🤝 C U C U M I S** ™


---

# CONTRIBUTING 
<sup>_[Version française](#contribuer) ci-haut_</sup>

👋 **Thank you for your interest!** 

Please first read and accept the terms of our [_Contributor License Agreement_ ("CLA")](CLA.md).

## Key Elements

- 🧠 **YOU DECLARE** that YOU ARE THE SOLE AUTHOR of your contributions _and that these are FREE OF ANY EXTERNAL RIGHTS_ (i.e. you are making these contributions under your own name, without any external dependencies or legal implications);
  - You can therefore explain your contributions, justify your decisions, and maintain yourself the code you contribute.
- 🔒 **YOU KEEP** your copyright _on your contributions_ - but the platform and its trademarks (held or claimed) are not negociable;
- ⚖️ **YOUR CONTRIBUTIONS REMAIN OPEN-SOURCE** under **⚖️MIT** (RDCore.SDK) or **⚖️GPLv3** (any other project in this repository).
- 🤝 **YOU AUTHORIZE THE INTEGRATION** of your contributions by the platform owner into a **PRIVATE VERSION UNDER A COMMERCIAL LICENSE**.

This last point transparently constituting the **commercial interest** motivating the publication and implementation of this project.


### I would like to contribute, but _in the name of my company or employer_

- ⏳ **Will be possible**, under a slightly different _Corporate Contributor License Agreement_ (CLA) that will be published separately from the _personal CLA_ document.


### AI-assisted contributions

- ✅ **Yes — use whatever assistance you like**, including agent-driven implementation of whole features. What matters is that **a human contributor owns the result**.
- ❌ **No autonomous submissions.** An agent opening a pull request on its own, or code its submitter cannot explain, justify, and maintain, will be declined.

The bar is not *how much* an AI contributed — it is **authorship and accountability**. When you submit, you make the same declaration as everyone else (see **Key Elements** above and the [CLA](CLA.md)): the work is yours, you understand all of it, you stand behind its design, and you will maintain it. However you got there, **you** open the pull request and **you** answer for it in review.

_Copilot_-style autocompletion, and using an assistant to explore or validate implementation ideas, solutions and architectures, remain — as always — **warmly encouraged**.


## Building and testing

RDCore targets **.NET 10**; the [.NET 10 SDK](https://dotnet.microsoft.com/download) is the only prerequisite.

From the repository root:

```powershell
dotnet build RDCore.slnx
dotnet test RDCore.slnx
```

A successful run **must report a non-zero test count**: a run that discovers no tests is not a passing run. The `--configuration Release` switch builds and tests the same way; continuous integration validates both configurations.


**V I V A T 🤝 C U C U M I S** ™


<hr/>
<p align='left' style='margin-left: 32px;'>
<a href='https://rubberduck-vba.github.io/RDCore/index.fr.html'>ACCUEIL</a> • <a href='https://rubberduck-vba.github.io/RDCore/index.html'>HOME</a>  | ℹ️ <a href='https://rubberduck-vba.github.io/RDCore/introduction.fr.html'>BIENVENUE</a> • <a href='https://rubberduck-vba.github.io/RDCore/introduction.html'>WELCOME</a>  | 🧩 <a href='https://rubberduck-vba.github.io/RDCore/getting-started.fr.html'>BÂTISSONS</a> • <a href='https://rubberduck-vba.github.io/RDCore/getting-started.html'>BUILD</a>  | <a href='https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.html'><strong>RD-VBAL</strong></a>  |  <a href='https://rubberduck-vba.github.io/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html'>SDK</a>  | 🌐 <a href='https://rubberduckvba.ca'>rubberduckvba.ca</a>
</p>
<hr/>
<p align='center'><img alt='Logo™ 9562-7303 Québec inc.' src='./assets/vector-ducky.svg' style='width:200px; align:center;' /></p>
<h6 align='center'>V I V A T ❤️ C U C U M I S ™</h6>
<p align='center' style='font-size:8pt;'>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/><em>Seul, &quot;Rubberduck&quot; est utilisé pour fins de référence au projet open-source legacy <strong>utilisé publiquement ainsi depuis 2015</strong> et sans lien ni affiliation avec tout tiers détenteur d'une marque semblable dans quelque juridiction que ce soit.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; et &quot;VIVAT CUCUMIS&quot; sont des marques de commerce revendiquées par 9562-7303 Québec inc. (en attente); Toutes les marques appartiennent à leur détenteur respectif.<br/>RDCore n'est pas un produit de Microsoft et n'est pas affilié à Microsoft, ni directement, ni indirectement.<br/><br/>If used alone, <em>&quot;Rubberduck&quot; is used as a reference to the legacy open-source project <strong>the same way it has been used publicly since 2015</strong> and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; and &quot;VIVAT CUCUMIS&quot; are trademarks claimed by 9562-7303 Québec inc. (pending). All trademarks belong to their respective owners.<br/>RDCore is not a Microsoft product and is not affiliated with Microsoft, directly or indirectly.</small>
</p>
