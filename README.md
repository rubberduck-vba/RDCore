# Rubberduck Core - VIVAT CUCUMIS™

[Anglais](./README.en.md)

### Avant de commencer.

- ✅ **Oui**, vous pouvez copier, télécharger, étudier et _fork_ ce dépôt et **contribuer à l'améliorer**, et/ou **en dériver votre propre travail**. 
  - Vous êtes d'ailleurs **activement encouragé** à le faire !
  - Assurez-vous de **lire et accepter l'Entente de Contributions** _avant_ de soumettre votre première _demande de tirage_ (_pull request_).
- ✅ **Oui**, vous pouvez construire vos propres analyseurs **et les contribuer comme diagnostics de base**.
  - 👉 Nous sommes reconnaissants pour toutes les soumissions - **assurez-vous de consulter et d'accepter l'Entente de Contributions** !
- ❌ **NON, vous ne pouvez PAS** construire et distribuer vos propres analyseurs ou extensions/plug-ins **sans également rendre le code source disponible sous GPLv3**.
  -
- ✅ **Oui, on peut parler affaires**. Ce dépôt appartient à une **entité corporative** légalement enregistrée au **Québec (Canada)**. 


Rubberduck a toujours été une initiative _open source_. **RDCore l'honore avec une formule Open-Core**. Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus d'informations.

---

## VBA comme plateforme.

Ce dépôt **implémente la spécification ouverte MS-VBAL** pour le langage de programmation _Visual Basic for Applications_, visant une fidélité à 100 % avec l'implémentation **MS-VBA** des mêmes spécifications.

Il implémente également un serveur **Language Server Protocol (LSP)**. Pour VBA. Une perfection architecturale magnifiquement pure, _fonctionnelle_, découplée. Des arborescences immuables et _tread-safe_ offrant une performance digne d'un compilateur/interpréteur moderne.

Le tout dans une bibliothèque .NET 10 qui peut théoriquement s'exécuter sur n'importe quelle plateforme.

**Oui, cela signifie ce que vous pensez** : 

# LONGUE VIE À VBA!

La prochaine évolution de Rubberduck a impliqué rien de moins que de _devenir VBA_. **C'est ça, les amis. On y est.**

Elle comprend les éléments suivants :

- **RDCore.SDK** encapsule la connaissance sémantique du langage – l'essence même de VBA – et l'emballe dans une bibliothèque permissive **licenciée MIT** qui est rigoureusement documentée et entièrement testée.
- **RDCore.Parsing** encapsule les connaissances syntaxiques et la sémantique des jetons associée dans un processus serveur LSP enfant/satellite sous licence copyleft **GPLv3** qui transforme un « Uri » d'espace de travail en un *arbre syntaxe abstrait* (AST) riche sémantiquement.
- **RDCore.CoreDiagnostics** est également sous **GPLv3** et établit les bases de tous les futurs plug-ins RDCore. 
- **RDCore.Test** documente tout à travers le prisme de **MS-VBAL**, prouvant la justesse de l'implémentation et démontrant son utilisation.
- **RDCore** est une application console de serveur en langage LSP destinée à être lancée éventuellement en ligne de commande par une application cliente (éditeur) en langage LSP.

En attendant un client LSP, il y a un mode CLI à utiliser.


## RD-VBA CLI

Lancer l'application serveur serveur en langage LSP **RDCore** sans passer d'arguments la fait fonctionner comme un **contexte exécutable bac à sable** à utiliser.

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
```
------
RDCore v0.0.1a - VIVAT CUCUMIS™
©Copyright (2026) 9562-7303 Québec inc.

RD-VBA Language Core initialisation...

✅ PRÊT.
>|
```
<small>(concept seulement : ce mode n'est pas encore implémenté)</small>

**Prêt ?** C'est _presque_ exactement ce à quoi ressemblait l'apprentissage de BASIC 2.0 il y a très, très longtemps. Presque.

**Référence rapide des suffixes de type** :

Alors que le mode CLI prend en charge l'ensemble des fonctionnalités de grammaire et de langage VBA (mais pas les plug-ins ni les diagnostics... Du moins pas en alpha), c'est amusant de voir à quel point il est encore rétrocompatible avec du code probablement écrit avant votre naissance. Vous aviez tout juste _deux_ caractères pour nommer chacune de vos variables de façon unique, et un _suffixe_ pour les typer :

Le suffixe « $ » désigne une `String`.

|Suffixe | Type entier|
|---|---|
|`%` (ou omis)|`Integer`|
|`&`|`Long`|
|`^`|`LongLong`|

👉 Le type « LongLong » n'est valide que dans un environnement 64 bits.

|Suffixe | Type décimal|
|---|---|
|`#` (ou omis)| `Double`|
|`!`|`Single`|
|`@`|`Currency`|

### Commandes :

- `RUN` exécute le _programme courant_.
- `LIST` affiche le texte du code source du _programme courant_.
  - `LIST 80` fournit le texte du code source jusqu'à la ligne 80 du _programme courant_.
  - `LIST 40-80` produit le texte du code source des lignes 40 à 80 du _programme courant_.
- `AST` produit l'arborescence abstraite du _programme courant_.
  - `AST 80` affiche l'arborescence abstraite à la ligne 80 du  _programme courant_.
  - `AST 40-80` affiche l'arborescence abstraite entre les lignes 40 et 80 du _programme courant_.
- `10 DIM A$` stocke l'instruction `DIM A$` à la ligne numéro `10` du _programme courant_.
- `PRINT A$` fonctionne et se comporte exactement comme « Debug.Print » ici, donc...
- `20 PRINT A$` donne la valeur de `A$` qui est... vide.
- `CLEAR` purge le contexte et le _programme courant_.
- `SAVE C:/DEV/SCRIPT1.vba` écrit le code source du _programme en mémoire_ dans le système de fichiers.
- `LOAD C:/DEV/SCRIPT1.vba` vide et réinitialise le _contexte_ et charge le _programme courant_ depuis le fichier source spécifié.
- `PEEK A %` affiche la valeur actuellement détenue en décalage mémoire `A%` dans l'espace mémoire du _contexte_.
- `POKE A %, B %` écrit la valeur `B%` au décalage mémoire `A%` dans l'espace mémoire du _contexte_.
  - ⚠️ Attention, il n'y a aucun garde-fou ici.
- « 18089 REM LONG LIVE THE CONCOMBRE » laisse un commentaire énigmatique à la ligne « 18089 » du _current program_, qui pourrait avoir des conséquences imprévues, comme l'impression d'une version ASCII du logo **Rubberduck Core**.

D'autres commandes pourraient être ajoutées, et l'interface CLI de RD-VBA pourrait finalement devenir une chose à part. Pour l'instant, il ne reste que la joie pure et sans filtre de redécouvrir Classic-VB d'une manière qui regarde l'avenir avec un large sourire.

---

# FEUILLE DE ROUTE

C'est très cool et tout, mais en 2026, un _vrai language de programmation_ a besoin d'un vrai IDE... Les commandes en mode CLI sont une preuve de concept amusante qui montre simplement le fonctionnement de RD-VBA, mais la véritable expérience phare passe par l'implémentation _Language Server Protocol_ (LSP), qui est actuellement très minimale.

Le **RDCore SDK** nous donne tous les outils nécessaires pour construire tout ce dont nous pourrions rêver en termes d'analyseurs sémantiques (diagnostics), de refactorisations, _actions sur le code_ et bien plus encore.

**Nous sommes actuellement en phase 2 : Open-Core (pré-lancement)**. Au cours de cette phase de développement, une communauté devrait se former  autour de l'extensibilité de RDCore avec un client éditeur phare open source et de l'implémentation sous LSP de toutes les fonctionnalités de l'IDE fournies par le protocole. Cela signifie :
- IntelliSense détaillé et localisé ainsi que des infobulles
- Indices d'incrustation et de superposition directement dans l'éditeur
- Diagnostics et actions de code
- Refactorisations (renommage, extraction de méthode, modification de signature, etc.)
- **Unit Testing Essentials** doit permettre la découverte et l'exécution des tests unitaires, et intégrer une interface _Test Explorer_ toolwindow pour le client/éditeur LSP.
- **Microsoft Excel Diagnostics** extension d'analyseur sémantique apportant des diagnostics spécialisés

Nous visons à **la parité de fonctionnalités avec Rubberduck Legacy** avant le lancement officiel.
