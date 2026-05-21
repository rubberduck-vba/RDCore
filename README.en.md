# Rubberduck Core - VIVAT CUCUMIS™

[Français](./README.md)

### Before we begin.

- ✅ **Yes**, you may study and **fork** this repository and **help improve it**, and/or **derive your own work**. 
  - You are **very much encouraged** to do so!
  - Make sure you **review and accept the Contributor Agreement** _before_ you submit your first _pull request_.
- ✅ **Yes**, you may build your own analyzers **and contribute them as core diagnostics**.
  - 👉 We're grateful for all submissions - **make sure to review and accept the Contributor Agreement**!
- ❌ **NO, you may NOT** build and distribute your own analyzers or extensions/plug-ins **without also making the source code available under GPLv3**.
  -
- ✅ **Yes, we can talk business**. This repository is owned and operated by a **corporate legal entity** registered in **Québec, Canada**. 


Rubberduck was always an open-source initiative. **RDCore honors it with an Open-Core formula**. See [rubberduckvba.ca](https://rubberduckvba.ca) for more information.

---

## VBA as a Platform.

This repository **implements the MS-VBAL open specification** for the _Visual Basic for Applications_ programming language, aiming for 100% fidelity with the **MS-VBA** implementation of the same specifications.

It also implements a **Language Server Protocol (LSP) Server**. For VBA. Beautifully pure, _functional_ pattern-matching architectural perfection at play, with immutable, thread-safe state.

All in a .NET 10 library that can theoretically run on any platform.

**Yes, this means what you think it means**: 

# VBA WILL LIVE ON

The next evolution of Rubberduck involved _becoming VBA_. **This is it, folks.**

It includes the following components:

- **RDCore.SDK** encapsulates the semantic knowledge of the language - the very essence of VBA, and packages it in a permissive **MIT-licensed** library that is thoroughly documented and fully tested.
- **RDCore.Parsing** encapsulates the syntactical knowledge and the associated token semantics into a copyleft **GPLv3-licensed** child/satellite LSP server process that turns a workspace `Uri` into a semantically rich *abstract syntax tree* (AST).
- **RDCore.CoreDiagnostics** is also under **GPLv3** and establishes the foundation for every future RDCore plug-in. 
- **RDCore.Runtime** encapsulates the run-time semantics of the language into a class library (**GPLv3**) that **ensures the runtime core of VBA remains open-source** and maintained by its community.
- **RDCore.Tests** documents everything through the lens of **MS-VBAL**, proving the correctness of the implementation and demonstrating its usage.
- **RDCore** is a LSP language server console application that is intended to be eventually started via command-line by a LSP language client (editor) application.

While we're waiting for a LSP client, there's a CLI mode to play with.


## RD-VBA CLI

Starting the **RDCore** LSP language server console application without passing any arguments makes it work as a **sandbox executable context** to play with.

```text                                                                                
                                    kkkkkkkkkkkkkkO                          
                                kkkkkkkkkkkkkkkkkkkkkkk                      
                              kkkkkkkkk        kkkkkkkkkkk                   
                            kkkkkkk                kkkkkkkkk                 
                           kkkkkk                     kkkkkkkkkOkO           
                          kkkkkk             kkkkkk     kkkkkkkkkkkkkkkk     
                          kkkkk             kkkkkkk     kkkkkkkkkkkkkkkk     
                          kkkkk               kkkk    Okkkkkk     kkkkkk     
                          Okkkkk                     kkkkkk   kkkkkkkk       
      kkkkkk               kkkkkk                    kkkkkkkkkkkkkkk         
     kkkkkkkkkk             kkkkkk                    kkkkkkkkkkkk           
    kkkkkkkkkkkkkkk          kkkkkkk                                         
    kkkkk  kkkkkkkkkkkkkO     kkkkkkk                  kkk                   
   kkkkk       kkkkkkkkkkkkkkkkkkkkkkkk                kkkkk                 
   kkkkk             kkkkkkkkkkkkkkkkkk                 kkkkkk               
   kkkkk                                                 Okkkkkk             
  Okkkkk                                                   kkkkkk            
  kkkkkk                                                    kkkkkk           
  kkkkkk                                                     kkkkkk          
  kkkkkk                                                      kkkkk          
   kkkkk                                                      kkkkkO         
   Okkkkk                                                     kkkkkk         
    kkkkkk                                                    kkkkk          
    dkkkkkk                                                   kkkkk          
     kkkkkkk                                                kkkkkk           
       kkkkkkk                                             kkkkkk            
        kkkkkkkO                                         kkkkkk              
          kkkkkkkkk                                  Okkkkkkk                
            kkkkkkkkkkkk                        kkkkkkkkkkk                  
               kkkkkkkkkkkkkkkkkkkkOkkkkkkkkkkkkkkkkkkkkk                    
                   OKKKkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk                        
                          kkkkVIVAT♥CUCUMISkkk                               

------
RDCore v0.0.1a - VIVAT CUCUMIS™
©Copyright (2026) 9562-7303 Québec inc.

RD-VBA Language Core initializing...

✅ READY.
>|
```
<small>(concept only: this mode isn't implemented yet)</small>

**Ready?** This is _almost_ exactly what learning BASIC 2.0 looked and felt like a long, long time ago. Almost.

**Type suffix quick reference**:

While CLI mode supports the entire VBA grammar and language features (but not plug-ins or diagnostics... at least not in alpha), it's fun to see just how completely backward-compatible it still is with code that was probably written before you were born. You had 2 whole entire characters to uniquely name all your variables, and a _type suffix_ to type them:

The `$` suffix denotes a `String`.

|Suffix | Integer type|
|---|---|
|`%` (or none)|`Integer`|
|`&`|`Long`|
|`^`|`LongLong`|

👉 `LongLong` is only valid in a 64-bit runtime environment.

|Suffix | Float type|
|---|---|
|`#` (or none)|`Double`|
|`!`|`Single`|
|`@`|`Currency`|

### Commands:

- `RUN` executes the _current program_.
- `LIST` outputs the source code text of the _current program_.
  - `LIST 80` outputs the source code text up to line 80 of the _current program_.
  - `LIST 40-80` outputs the source code text of lines 40 to 80 of the _current program_.
- `AST` outputs the _abstract syntax tree_ nodes of the _current program_.
  - `AST 80` outputs the _abstract syntax tree_ nodes at line 80 of the _current program_.
  - `AST 40-80` outputs the _abstract syntax tree_ nodes between lines 40 and 80 of the _current program_.
- `10 DIM A$` stores the instruction `DIM A$` at line number `10` of the _current program_.
- `PRINT A$` works and behaves exactly like `Debug.Print` here, so...
- `20 PRINT A$` outputs the value of `A$` which is.. nothing of note.
- `CLEAR` flushes and resets the entire _current program_ and _execution context_.
- `SAVE "C:/DEV/SCRIPT1.VBA"` writes the _current program_ source code to the file system.
- `LOAD "C:/DEV/SCRIPT1.VBA"` flushes and resets the _execution context_ and loads the _current program_ from the specified source file.
- `PEEK A%` outputs the value currently held at memory offset `A%` in the memory space of the _execution context_.
- `POKE A%, B%` writes the value `B%` at memory offset `A%` in the memory space of the _execution context_.
  - ⚠️ There are no guardrails here, you're on your own.
- `18089 REM LONG LIVE THE CUCUMBER` leaves a cryptic comment at line `18089` of the _current program_, that may have unintended consequences, like printing an ASCII rendition of the **Rubberduck Core** logo.

More commands may be added, and RD-VBA CLI may eventually become its own thing. For now it's just the pure, unadulterated joy of rediscovering Classic-VB in a way that looks at the future with a bright smile.

---

# ROADMAP

Now this is very cool and all, but in 2026 a _real programming language_ needs a real IDE... CLI mode commands is a fun proof-of-concept that merely shows RD-VBA working, but the real flagship experience is through the _Language Server Protocol_ (LSP) implementation, which is at the moment very much anemic.

The **RDCore SDK** gives us all the tools we need to build everything we could every dream of in terms of semantic analyzers (diagnostics), refactorings & _code actions_ and so much more.

**We are currently in Phase 2: Open-Core (pre-launch)**. During this development phase, a community is expected to coalesce around extending RDCore with a flagship open-source editor client and implementing under LSP every IDE feature that is provided by the protocol. This means:
- Detailed and localized IntelliSense and tooltips
- Inlay and overlay hints directly in the editor
- Diagnostics and code actions
- Refactorings (rename, extract method, change signature, etc.)
- **Unit Testing Essentials** shall enable unit test discovery and execution, and package a _Test Explorer_ toolwindow UI for the LSP client/editor.
- **Microsoft Excel Diagnostics** semantic analyzer extension bringing specialized diagnostics to the table

We aim for **feature parity with Rubberduck Legacy** before the official launch.