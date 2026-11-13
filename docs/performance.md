# Performance
<sup>_Ce document est disponible en [français](performance.fr.html)_</sup>

---
## What to expect

RD-VBA runs most of a program as compiled code. The figures below compare it to MS-VBA running the same code in Excel, on the same machine
(Windows 11 x64, Excel 64-bit). Each is the time RD-VBA takes as a multiple of the time MS-VBA takes for the same work: below 1 is faster, above 1 slower.
They describe the default settings: the `Compiled` execution engine and the `Debug` program configuration (`--configuration`, see [The shell and the rdc commands](rdc.md)).

| Kind of code | Time compared to MS-VBA |
|---|---|
| Arithmetic on `Long` and `Double` variables, conditions, `Select Case`, local and static variables | 0.3–0.8× (faster) |
| String assignment and comparison | 0.7–2× |
| `Variant` arithmetic | about 2× |
| Calls to your own procedures, recursion, `ByRef` arguments | 3–4× |
| `On Error Resume Next` in a loop that raises errors | about 5× |
| Calls to the members of your own classes and interfaces | about 7× |
| `Currency` and `Date` arithmetic | about 7.5× |
| Filling and summing a typed array | about 9× |
| Building a string piece by piece, concatenation in a loop | 7–19× |
| Assigning a field of a user-defined type | about 19× |
| Creating an object on every iteration | about 23× |
| `Set` assignment, assigning a property of an object | about 36× |
| Calls to the standard library (`Len`, `CStr`, `CLng`) in a loop | about 75× |
| Reading and writing the elements of an array held in a `Variant` | about 120× |
| The `Mid` statement | about 135× |

### Excel automation

| Work | Time compared to MS-VBA |
|---|---|
| Writing a block of cells at once (`Range(...).Value = values`) | 1.5–2× |
| Reading a block of cells at once (`values = Range(...).Value`) | about 45× |
| Reading and writing cells one at a time (`Cells(r, c).Value`) | about 550× |

Every call to Excel's object model crosses from the RD-VBA program to Excel and back. A call costs RD-VBA much more than it costs MS-VBA, so the number of
calls decides how long the code takes.

## Writing code that runs well

- **Work with ranges in blocks.** Read a range into an array with one `Range(...).Value`, work on the array, and write it back with one assignment.
  10,000 cells done one at a time take about 2.5 minutes. Done as a block, they take under a third of a second.
- **Keep a hot loop's values typed.** A `Long` or `Double` loop runs faster than in MS-VBA. The same loop over `Variant` values, or over an array held in
  a `Variant`, is slower.
- **See what runs compiled.** `rdc compilation` lists the procedures that do not run compiled, and what keeps each one in the interpreter. Code in the
  interpreter is slower than compiled code.
- **Measure your own code.** `rdc bench` times each `Public Sub Bench*()` of a workspace's standard modules. Run it before and after a change (see
  [The shell and the rdc commands](rdc.md)).

## See also

- [The shell and the rdc commands](rdc.md)
- [Getting started with RDCore](getting-started.md)

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
