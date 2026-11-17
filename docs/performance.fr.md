# Performance
<sup>_This document is available in [English](performance.html)_</sup>

---
## À quoi s'attendre

RD-VBA exécute la plus grande partie d'un programme en code compilé. Chaque chiffre ci-dessous est le temps que prend RD-VBA pour un cas du corpus de
`rdc bench`, en multiple du temps que prend MS-VBA pour le même code dans Excel : sous 1×, c'est plus rapide; au-dessus de 1×, plus lent. Chaque badge
donne aussi les deux temps, en millisecondes, sur la machine de référence (Excel 16, 64 bits, Windows 11 x64, 12 cœurs). Les chiffres décrivent les
réglages par défaut : le moteur d'exécution `Compiled` et la configuration de programme `Debug` (`--configuration`, voir
[Le _shell_ et les commandes rdc](rdc.fr.md)).

Les chiffres de RD-VBA sont mesurés à chaque fusion. Tous les cas du corpus sont listés, les plus lents compris. Chaque cas mène à sa source, et
la page [Bancs d'essai](benchmarks/index.fr.md) explique comment les exécuter sur votre propre machine.

## Appels

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Calls.BenchRecursion`](benchmarks/calls.fr.md#benchrecursion) | La récursion : `Fib(25)`, 242 785 appels courts | ![Calls.BenchRecursion](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchRecursion.json) |
| [`Calls.BenchProcedureCalls`](benchmarks/calls.fr.md#benchprocedurecalls) | Une fonction du même module, appelée dans une boucle | ![Calls.BenchProcedureCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchProcedureCalls.json) |
| [`Calls.BenchByRefCalls`](benchmarks/calls.fr.md#benchbyrefcalls) | Une variable passée `ByRef` et affectée par le paramètre | ![Calls.BenchByRefCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchByRefCalls.json) |
| [`Calls.BenchClassMemberCalls`](benchmarks/calls.fr.md#benchclassmembercalls) | Un membre d'une classe, appelé par une variable de la classe | ![Calls.BenchClassMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchClassMemberCalls.json) |
| [`Calls.BenchInterfaceMemberCalls`](benchmarks/calls.fr.md#benchinterfacemembercalls) | Le même membre, appelé par une interface | ![Calls.BenchInterfaceMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchInterfaceMemberCalls.json) |
| [`Calls.BenchStandardLibraryCalls`](benchmarks/calls.fr.md#benchstandardlibrarycalls) | `N = Len(CStr(I)) + CLng(I)` dans une boucle | ![Calls.BenchStandardLibraryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchStandardLibraryCalls.json) |
| [`Calls.BenchFactoryCalls`](benchmarks/calls.fr.md#benchfactorycalls) | Une méthode de fabrique d'une instance par défaut, un nouvel objet à chaque appel | ![Calls.BenchFactoryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchFactoryCalls.json) |
| [`Calls.BenchDefaultInstanceCalls`](benchmarks/calls.fr.md#benchdefaultinstancecalls) | Un membre appelé sur une instance par défaut | ![Calls.BenchDefaultInstanceCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchDefaultInstanceCalls.json) |
| [`Calls.BenchSiblingCalls`](benchmarks/calls.fr.md#benchsiblingcalls) | Un membre qui appelle un membre privé de sa propre classe par son seul nom | ![Calls.BenchSiblingCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchSiblingCalls.json) |

## Instructions

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Statements.BenchSetAssignment`](benchmarks/statements.fr.md#benchsetassignment) | `Set D = C` d'une variable objet | ![Statements.BenchSetAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSetAssignment.json) |
| [`Statements.BenchMemberAssignment`](benchmarks/statements.fr.md#benchmemberassignment) | `P.X = I`, une propriété d'un objet | ![Statements.BenchMemberAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMemberAssignment.json) |
| [`Statements.BenchRecordFieldAssignment`](benchmarks/statements.fr.md#benchrecordfieldassignment) | `R.X = I`, un champ d'un type défini par l'utilisateur | ![Statements.BenchRecordFieldAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchRecordFieldAssignment.json) |
| [`Statements.BenchSelectCase`](benchmarks/statements.fr.md#benchselectcase) | `Select Case I Mod 4` à trois branches | ![Statements.BenchSelectCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSelectCase.json) |
| [`Statements.BenchConditions`](benchmarks/statements.fr.md#benchconditions) | Les conditions de `If`, `ElseIf` et `Do While` : des comparaisons jointes par `And` | ![Statements.BenchConditions](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchConditions.json) |
| [`Statements.BenchMidStatement`](benchmarks/statements.fr.md#benchmidstatement) | L'instruction `Mid$` : `Mid$(S, 1, 1) = "r"` | ![Statements.BenchMidStatement](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMidStatement.json) |
| [`Statements.BenchOnErrorResumeNext`](benchmarks/statements.fr.md#benchonerrorresumenext) | Une boucle sous `On Error Resume Next`, qui divise par zéro une fois sur dix | ![Statements.BenchOnErrorResumeNext](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchOnErrorResumeNext.json) |

## Tableaux

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Arrays.BenchArrayFillAndSum`](benchmarks/arrays.fr.md#bencharrayfillandsum) | Écrire, puis lire, chaque élément d'un tableau de `Long` | ![Arrays.BenchArrayFillAndSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchArrayFillAndSum.json) |
| [`Arrays.BenchVariantArray2D`](benchmarks/arrays.fr.md#benchvariantarray2d) | Un tableau `Variant` à deux dimensions, chaque élément doublé | ![Arrays.BenchVariantArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantArray2D.json) |
| [`Arrays.BenchVariantHeldArray2D`](benchmarks/arrays.fr.md#benchvariantheldarray2d) | La même chose, le tableau contenu dans un `Variant`, comme le donne `V = Range(...).Value` | ![Arrays.BenchVariantHeldArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantHeldArray2D.json) |

## Chaînes

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Strings.BenchStringAssignment`](benchmarks/strings.fr.md#benchstringassignment) | Une `String` affectée dans une boucle | ![Strings.BenchStringAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringAssignment.json) |
| [`Strings.BenchStringConcatenation`](benchmarks/strings.fr.md#benchstringconcatenation) | `S = S & "x"`, une chaîne qui grandit d'un caractère à la fois | ![Strings.BenchStringConcatenation](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringConcatenation.json) |
| [`Strings.BenchStringComparison`](benchmarks/strings.fr.md#benchstringcomparison) | Des chaînes courtes jointes et comparées | ![Strings.BenchStringComparison](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringComparison.json) |
| [`Strings.BenchStringBuilding`](benchmarks/strings.fr.md#benchstringbuilding) | Une ligne de nombres écrits en texte, un séparateur entre chacun | ![Strings.BenchStringBuilding](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringBuilding.json) |
| [`Strings.BenchStringArguments`](benchmarks/strings.fr.md#benchstringarguments) | Des chaînes passées à une procédure, par valeur et par référence | ![Strings.BenchStringArguments](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringArguments.json) |

## Calcul numérique

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Numeric.BenchLongCounterDoubleSum`](benchmarks/numeric.fr.md#benchlongcounterdoublesum) | `S = S + I`, un compteur `Long` et une somme `Double` | ![Numeric.BenchLongCounterDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongCounterDoubleSum.json) |
| [`Numeric.BenchDoubleSum`](benchmarks/numeric.fr.md#benchdoublesum) | La même boucle sur des `Double` | ![Numeric.BenchDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchDoubleSum.json) |
| [`Numeric.BenchLongArithmetic`](benchmarks/numeric.fr.md#benchlongarithmetic) | De l'arithmétique sur des `Long`, gardée dans les bornes par `Mod` | ![Numeric.BenchLongArithmetic](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongArithmetic.json) |
| [`Numeric.BenchVariantSum`](benchmarks/numeric.fr.md#benchvariantsum) | La même boucle, chaque opérande un `Variant` | ![Numeric.BenchVariantSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantSum.json) |
| [`Numeric.BenchCurrencyAndDate`](benchmarks/numeric.fr.md#benchcurrencyanddate) | Une somme `Currency` et une `Date` avancée d'un millième de jour à chaque tour | ![Numeric.BenchCurrencyAndDate](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchCurrencyAndDate.json) |
| [`Numeric.BenchVariantTypedMix`](benchmarks/numeric.fr.md#benchvarianttypedmix) | Un `Long` et un `Double` ajoutés à une somme `Variant` | ![Numeric.BenchVariantTypedMix](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantTypedMix.json) |

## Variables et paramètres

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Slots.BenchWideActivations`](benchmarks/variables-and-parameters.fr.md#benchwideactivations) | Un appel à six paramètres typés et quatre variables locales | ![Slots.BenchWideActivations](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchWideActivations.json) |
| [`Slots.BenchModuleVariableLoop`](benchmarks/variables-and-parameters.fr.md#benchmodulevariableloop) | Une variable de module, lue et écrite à chaque tour | ![Slots.BenchModuleVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchModuleVariableLoop.json) |
| [`Slots.BenchLocalVariableLoop`](benchmarks/variables-and-parameters.fr.md#benchlocalvariableloop) | La même boucle sur une variable locale | ![Slots.BenchLocalVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchLocalVariableLoop.json) |
| [`Slots.BenchStaticVariableLoop`](benchmarks/variables-and-parameters.fr.md#benchstaticvariableloop) | La même boucle sur une variable locale `Static` | ![Slots.BenchStaticVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchStaticVariableLoop.json) |
| [`Slots.BenchCurrencySum`](benchmarks/variables-and-parameters.fr.md#benchcurrencysum) | Une somme `Currency` | ![Slots.BenchCurrencySum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchCurrencySum.json) |
| [`Slots.BenchBooleanFlag`](benchmarks/variables-and-parameters.fr.md#benchbooleanflag) | Un indicateur `Boolean` inversé et testé à chaque tour | ![Slots.BenchBooleanFlag](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchBooleanFlag.json) |

## Bibliothèque standard

Chaque cas appelle une fonction 100 000 fois, avec les arguments indiqués.

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`StdLib.BenchCLng`](benchmarks/standard-library.fr.md#benchclng) | `CLng(X)` d'un `Double` | ![StdLib.BenchCLng](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCLng.json) |
| [`StdLib.BenchCDbl`](benchmarks/standard-library.fr.md#benchcdbl) | `CDbl(I)` d'un `Long` | ![StdLib.BenchCDbl](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCDbl.json) |
| [`StdLib.BenchCStr`](benchmarks/standard-library.fr.md#benchcstr) | `CStr(I)` d'un `Long` | ![StdLib.BenchCStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCStr.json) |
| [`StdLib.BenchVal`](benchmarks/standard-library.fr.md#benchval) | `Val(S)` de `"123.5"` | ![StdLib.BenchVal](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVal.json) |
| [`StdLib.BenchLen`](benchmarks/standard-library.fr.md#benchlen) | `Len(S)` d'une `String` | ![StdLib.BenchLen](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLen.json) |
| [`StdLib.BenchLeft`](benchmarks/standard-library.fr.md#benchleft) | `Left$(S, 2)` | ![StdLib.BenchLeft](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLeft.json) |
| [`StdLib.BenchMidFunction`](benchmarks/standard-library.fr.md#benchmidfunction) | `Mid$(S, 4, 3)` | ![StdLib.BenchMidFunction](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchMidFunction.json) |
| [`StdLib.BenchInStr`](benchmarks/standard-library.fr.md#benchinstr) | `InStr(S, "V")` | ![StdLib.BenchInStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInStr.json) |
| [`StdLib.BenchReplace`](benchmarks/standard-library.fr.md#benchreplace) | `Replace(S, "-", "_")` | ![StdLib.BenchReplace](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchReplace.json) |
| [`StdLib.BenchTrim`](benchmarks/standard-library.fr.md#benchtrim) | `Trim$(S)` | ![StdLib.BenchTrim](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTrim.json) |
| [`StdLib.BenchUCase`](benchmarks/standard-library.fr.md#benchucase) | `UCase$(S)` | ![StdLib.BenchUCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUCase.json) |
| [`StdLib.BenchStrComp`](benchmarks/standard-library.fr.md#benchstrcomp) | `StrComp(S, "rd-vba", vbTextCompare)` | ![StdLib.BenchStrComp](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchStrComp.json) |
| [`StdLib.BenchAsc`](benchmarks/standard-library.fr.md#benchasc) | `Asc(S)` | ![StdLib.BenchAsc](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAsc.json) |
| [`StdLib.BenchChr`](benchmarks/standard-library.fr.md#benchchr) | `Chr$(65 + I Mod 26)` | ![StdLib.BenchChr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchChr.json) |
| [`StdLib.BenchFormat`](benchmarks/standard-library.fr.md#benchformat) | `Format$(D, "0.00")` | ![StdLib.BenchFormat](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchFormat.json) |
| [`StdLib.BenchIsNumeric`](benchmarks/standard-library.fr.md#benchisnumeric) | `IsNumeric(S)` de `"123.5"` | ![StdLib.BenchIsNumeric](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsNumeric.json) |
| [`StdLib.BenchIsEmpty`](benchmarks/standard-library.fr.md#benchisempty) | `IsEmpty(V)` d'un `Variant` `Empty` | ![StdLib.BenchIsEmpty](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsEmpty.json) |
| [`StdLib.BenchVarType`](benchmarks/standard-library.fr.md#benchvartype) | `VarType(V)` d'un `Variant` qui contient un `Double` | ![StdLib.BenchVarType](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVarType.json) |
| [`StdLib.BenchTypeName`](benchmarks/standard-library.fr.md#benchtypename) | `TypeName(V)` du même | ![StdLib.BenchTypeName](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTypeName.json) |
| [`StdLib.BenchAbs`](benchmarks/standard-library.fr.md#benchabs) | `Abs(-I)` | ![StdLib.BenchAbs](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAbs.json) |
| [`StdLib.BenchRound`](benchmarks/standard-library.fr.md#benchround) | `Round(D, 2)` | ![StdLib.BenchRound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchRound.json) |
| [`StdLib.BenchSqr`](benchmarks/standard-library.fr.md#benchsqr) | `Sqr(I)` | ![StdLib.BenchSqr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchSqr.json) |
| [`StdLib.BenchInt`](benchmarks/standard-library.fr.md#benchint) | `Int(D)` | ![StdLib.BenchInt](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInt.json) |
| [`StdLib.BenchDateAdd`](benchmarks/standard-library.fr.md#benchdateadd) | `DateAdd("d", 1, T)` | ![StdLib.BenchDateAdd](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchDateAdd.json) |
| [`StdLib.BenchYear`](benchmarks/standard-library.fr.md#benchyear) | `Year(T)` | ![StdLib.BenchYear](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchYear.json) |
| [`StdLib.BenchNow`](benchmarks/standard-library.fr.md#benchnow) | `Now` | ![StdLib.BenchNow](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchNow.json) |
| [`StdLib.BenchUBound`](benchmarks/standard-library.fr.md#benchubound) | `UBound(A)` d'un tableau | ![StdLib.BenchUBound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUBound.json) |

## Automatisation d'Excel

Ces cas appellent le modèle objet d'Excel en liaison tardive depuis VBA. Ils exigent Excel : ils ne sont donc mesurés que lors de l'exécution de
référence, et leurs badges donnent la date de cette exécution plutôt qu'un chiffre à jour.

| Cas | Ce qu'il mesure | RD-VBA ÷ MS-VBA |
|---|---|---|
| Bloc de 1 000 × 10, lecture | `V = Range(...).Value` | ![Excel.Block1000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Read.json) |
| Bloc de 1 000 × 10, parcours | La boucle sur `V(R, C)`, sans appel à Excel | ![Excel.Block1000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Pass.json) |
| Bloc de 1 000 × 10, écriture | `Range(...).Value = V` | ![Excel.Block1000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Write.json) |
| Bloc de 1 000 × 10, total | Lecture, parcours et écriture | ![Excel.Block1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Total.json) |
| Cellule par cellule, 1 000 × 10 | 10 000 cellules, une lecture et une écriture chacune | ![Excel.CellByCell1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.CellByCell1000x10.Total.json) |
| Bloc de 100 000 × 10, lecture | `V = Range(...).Value` | ![Excel.Block100000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Read.json) |
| Bloc de 100 000 × 10, parcours | La boucle sur `V(R, C)`, sans appel à Excel | ![Excel.Block100000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Pass.json) |
| Bloc de 100 000 × 10, écriture | `Range(...).Value = V` | ![Excel.Block100000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Write.json) |
| Bloc de 100 000 × 10, total | Lecture, parcours et écriture | ![Excel.Block100000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Total.json) |

Chaque appel au modèle objet d'Excel passe du programme RD-VBA à Excel, puis revient. Un appel coûte beaucoup plus cher à RD-VBA qu'à MS-VBA : c'est
le nombre d'appels qui décide du temps que prend le code.

## Comment les chiffres sont mesurés

- **L'exécution de référence.** Sur la machine de référence, MS-VBA et RD-VBA exécutent le corpus en cinq rondes alternées (MS-VBA en premier aux quatre
  premières, RD-VBA en premier à la cinquième), et les cas d'automatisation d'Excel en trois rondes, MS-VBA en premier. Chaque cas s'exécute sept fois
  par ronde (cinq pour l'automatisation d'Excel), et le chiffre retenu est l'exécution la plus rapide. L'exécution de référence est refaite quand le
  corpus change.
- **À chaque fusion.** Une machine de compilation Windows exécute le corpus en trois rondes alternées avec deux compilations de `rdc` : celle de la
  fusion, et celle de l'exécution de référence. Le temps de RD-VBA pour un cas est celui de l'exécution de référence, multiplié par le rapport entre les
  deux compilations sur cette machine. Comparer les deux compilations sur une même machine écarte la vitesse propre de cette machine. Le fichier des
  chiffres (`bench.json`, sur la branche `badges`) nomme le processeur de cette machine.
- **Chaque exécution part d'un tas récupéré.** Avant chaque exécution chronométrée, `rdc bench` lance une récupération de mémoire sur tout le tas :
  qu'une récupération de mémoire tombe pendant une exécution ne dépend alors que de ce que cette exécution alloue et garde. Le temps d'une récupération
  pendant une exécution fait partie du chiffre.
- **Allocation.** Pour chaque cas, `rdc bench` donne aussi ce que son exécution la plus rapide a alloué et les récupérations de mémoire de chaque
  génération faites pendant celle-ci (les colonnes `allocated` et `gc 0/1/2`). À chaque fusion, elles sont consignées à côté des temps dans le fichier
  des chiffres, pour la compilation de la fusion. Elles ne dépendent pas de la vitesse de la machine. Les badges ne donnent que le rapport.
- **Résolution.** `rdc bench` donne les temps au dixième de milliseconde : un cas qui prend moins d'une milliseconde est juste à environ ±0,05 ms.

## Écrire du code qui s'exécute bien

- **Travaillez les plages par blocs.** Lisez une plage dans un tableau avec un seul `Range(...).Value`, travaillez sur le tableau, puis réécrivez-le
  avec une seule affectation. Les cas cellule par cellule et par bloc ci-dessus montrent l'écart.
- **Gardez typées les valeurs d'une boucle critique.** Une boucle sur des `Long` ou des `Double` s'exécute plus vite que dans MS-VBA. La même boucle
  sur des `Variant`, ou sur un tableau contenu dans un `Variant`, est plus lente.
- **Voyez ce qui s'exécute compilé.** `rdc compilation` liste les procédures qui ne s'exécutent pas compilées, et ce qui garde chacune dans
  l'interpréteur. Le code dans l'interpréteur est plus lent que le code compilé.
- **Mesurez votre propre code.** `rdc bench` chronomètre chaque `Public Sub Bench*()` des modules standard d'un espace de travail. Lancez-le avant et
  après une modification (voir [Le _shell_ et les commandes rdc](rdc.fr.md)).

## Voir aussi

- [Le _shell_ et les commandes rdc](rdc.fr.md)
- [Démarrer avec RDCore](getting-started.fr.md)

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
