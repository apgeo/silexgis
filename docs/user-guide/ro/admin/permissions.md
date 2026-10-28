# Permisiunile explicate

🇬🇧 [English](../../admin/permissions.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite:
[Protecția locațiilor](location-protection.md) ·
[Noțiuni de bază](../core-concepts.md#2-vizibilitatea-permisiunile-și-protecția-locației-sunt-trei-lucruri-diferite)

---

SilexGIS are **grupuri de permisiuni editabile în locul unor roluri fixe**. Nu există „Editor" sau
„Moderator" încorporat în program; există seturi de reguli denumite pe care le-a scris cineva, iar
tu faci parte din unele dintre ele.

---

## Cele trei lucruri care decid accesul

Accesul la o înregistrare este decis de trei mecanisme independente care se compun:

### 1. Proprietatea

Deții ce ai creat. Proprietatea acordă acces prin ea însăși.

### 2. Vizibilitatea

O proprietate a înregistrării, stabilită de cine a creat-o:

| Valoare | Admite |
|---|---|
| **Privată** | Doar proprietarul și cine a primit acces explicit |
| **Grup de speologie** | Membrii grupului de care este legată |
| **Utilizatori autentificați** | Oricine are cont aici |
| **Publică** | Oricine, inclusiv vizitatori fără cont |

### 3. Regulile

*Permite* sau *refuză*, pe acțiune, pe domeniu, la un anumit cuprins. Regulile trăiesc în
**grupuri de permisiuni** sau direct pe un obiect.

Deasupra tuturor celor trei stau **Administratorii deplini** — apartenența decide înainte de
consultarea vreunei reguli.

---

## Anatomia unei reguli

O regulă spune: **[subiectul] este [permis / refuzat] pentru [aceste acțiuni] pe [acest domeniu]
în cuprinsul [acestui domeniu de aplicare]**.

### Subiect

Un **utilizator** sau un **grup de speologie**. (Într-un grup de permisiuni, membrii sunt
subiectul.)

### Acțiuni

| Acțiune | |
|---|---|
| **Citire** | |
| **Scriere** | |
| **Creare** | |
| **Ștergere** | |
| **Partajare** | Generarea de linkuri de partajare |
| **Rulare** | Execuție — de ex. pornirea unei sarcini |
| **Administrare permisiuni** | Scrierea de reguli pe acest lucru |
| **Locație exactă** | Vederea coordonatelor reținute — vezi [Protecția locațiilor](location-protection.md) |

### Domenii

Obiecte · Jurnale de tură · Fișiere geodate · Hărți georeferențiate · Vederi de hartă salvate
· Depozitul de fișiere · Documente · Expediții · Straturi de hartă · Etichete · Ierarhii ·
Taxonomii · Speologi · Grupuri de speologie · Conturi de utilizator · Grupuri de permisiuni ·
Seturi de obiecte · Setările instalării · Texte mesaje · Istoricul de audit · Sarcini · Relief ·
Liste de verificare · Evenimente din calendar

### Cuprinsuri

Aceasta este partea care face sistemul practic în loc de plictisitor.

| Cuprins | Ajunge la |
|---|---|
| **Tot** | Întregul domeniu |
| **Obiectele proprii** | Ce a creat subiectul |
| **Conținutul unui grup de speologie** | Tot ce este legat de acel grup |
| **Un obiect și tot ce conține** | Un subarbore din ierarhia de obiecte |
| **Un set de obiecte** | Un set numit de obiecte — vezi mai jos |
| **Un dulap și tot ce este clasat sub el** | O ramură din arborele de clasare |
| **Un singur obiect** | O singură înregistrare |

Regulile au și un **nivel** — *global*, *colecție* sau *obiect* — în termenii căruia este
formulată rezolvarea conflictelor de mai jos.

**Ce îi dă unui membru *Creare* în cuprinsul unui grup de speologie.** Un grup de speologie nou
pornește cu un grup de permisiuni care le permite membrilor să citească, să corecteze și să creeze
**conținutul propriu al grupului**. Este un drept asupra înregistrărilor legate de grup, nu asupra
întregului domeniu — așa că un astfel de membru poate crea o tură *care aparține grupului* și
nimic nelegat. Pentru **ture**, aplicația îi oferă controalele de creare și leagă tura în locul lui
([cum arată](../features/trips.md#când-dreptul-de-a-înregistra-ture-vine-de-la-club)). Pentru
peșteri, obiecte, tabere, geodate și documente, controalele de creare se desenează încă doar
pentru cine are *Creare* asupra întregului domeniu; un membru fără acest drept nu vede deocamdată
niciun buton acolo, orice ar spune regulile grupului. Scoate *Creare* din regula grupului, sau
refuz-o, și dispar și controalele pentru ture: oferta urmează regula.

---

## Cum se rezolvă conflictele

Două reguli, declarate pe fiecare ecran care te lasă să scrii una:

> **Un refuz bate orice permisiune de la același nivel, iar o regulă scrisă pe obiectul însuși bate
> orice se moștenește.**

Deci o permisiune la nivel de obiect bate un refuz global; un refuz global bate o permisiune
globală.

**Verifică ce costă înainte să salvezi.** Un grup de permisiuni care conține un refuz nu te lasă
să-l salvezi fără să deschizi întâi previzualizarea — vezi mai jos.

---

## Grupuri de permisiuni

**Administrare → Grupuri de permisiuni.**

Un grup are un **nume**, o **descriere**, **membri** și **reguli**. Trei file: Reguli, Membri,
Previzualizare.

> *„O regulă trăiește aici sau direct pe un obiect — niciodată într-un grup de speologie."*
> Grupurile de speologie sunt despre cine este într-un club; grupurile de permisiuni sunt despre ce
> pot face oamenii.

### Grupuri incluse și protejate

Unele grupuri vin cu produsul și sunt marcate **Predefinit** sau **Protejat**. În special,
**Administratorii deplini nu dețin reguli**: apartenența însăși este acordarea, decisă înaintea
oricărei reguli.

Aplicația refuză categoric două lucruri:

- orice ar **lăsa niciun administrator deplin activ capabil să se autentifice**,
- o regulă care ar **acorda drepturi pe care nu le deții tu**.

### Acordarea dreptului de a verifica încărcările

Dreptul **Revizuirea încărcărilor** îi arată celui care îl deține **în întregime** fiecare încărcare
care așteaptă verificarea: pozițiile exacte ale peșterilor protejate și fotografiile peșterilor pe
care altfel nu le-ar putea deschide. Fila **Reguli** a unui grup spune asta de îndată ce o regulă
acordă dreptul, iar fila **Membri** o spune pentru un grup care îl acordă — inclusiv Administratorii
deplini. Dă-l doar persoanelor de încredere pentru toate peșterile. În afara paginilor de verificare,
cel care îl deține vede în continuare doar ce îi permit celelalte drepturi. Vezi
[Verificarea încărcărilor](../features/upload-review.md).

### Previzualizarea

**Alege un utilizator sau un grup de speologie pentru a vedea drepturile pe care le-ar deține.** Extinde un
rând pentru a vedea ce regulă a decis fiecare acțiune.

Un set de reguli care conține un **refuz** îți cere să deschizi previzualizarea înainte de
salvare. Aceasta este singura frecare din tot sistemul de permisiuni și există pentru că un refuz
este lucrul care strică tacit accesul cuiva peste trei luni.

### Ștergerea unui grup

*„Membrii pierd doar ce acorda el."* Ștergerea unui grup de permisiuni nu șterge nimic altceva.

---

## Acordări pe obiect

Fiecare obiect are o filă **Permisiuni** (acolo unde ai voie să administrezi permisiuni pe el).

Adaugă o regulă care numește un **utilizator** sau un **grup de speologie**, un efect, acțiuni și
un cuprins de **doar acest obiect** sau **acest obiect și tot ce conține**.

**O regulă pleacă odată cu ce numește.** Ștergerea unei înregistrări, a unui grup de speologie, a
unui set de obiecte, a unui dulap sau a unui cont elimină regulile care o numesc, iar fiecare
eliminare apare în istoricul de audit — așa că un lucru șters nu rămâne niciodată ca acordare în
lista de drepturi a cuiva. O înregistrare doar *mutată la coș* — o tură, un document sau o peșteră
care încă pot fi restaurate — își păstrează regulile, iar restaurarea ei dă înapoi același acces.
Un grup de speologie cu un refuz scris pe el și un set de obiecte sau un dulap prin care încă
ajunge o regulă sunt refuzate la ștergere, ca înainte.

### Deschis despre cineva

Mesajul care spune că cineva invitat pe o tură nu poate deschide o peșteră duce la
**Permisiunile** acelei peșteri, deschise despre acea persoană. Dialogul conține atunci un rând
pe care nu l-ai adăugat tu — **Citire** pe acest obiect pentru ea, marcat *propusă* —
și un rând deasupra tabelului care spune de ce este acolo.

Este o ciornă. **Nimic nu se acordă până nu apeși OK**, iar ce se salvează este o regulă
obișnuită: mărginită, ca oricare alta, de ceea ce deții tu însuți, și în vigoare
până când cineva o șterge. Șterge rândul ca să nu acorzi nimic. Un link care numește pe cineva
pe care aplicația nu îl găsește deschide dialogul obișnuit și nu spune nimic despre el.

Vezi [O invitație nu acordă nimic](../features/trips.md#o-invitație-nu-acordă-nimic).

### Reguli scrise de o tabără

Pe o tură care aparține unei tabere, unele reguli apar ca **De la o tabără**. Au fost scrise de
tabără și se schimbă acolo, nu aici.

Sunt arătate pe tură oricum, ca **întreaga listă a cui poate citi tura să fie într-un singur loc**.

---

## Seturi de obiecte

**Configurare → Seturi de obiecte.** Un set denumit de obiecte către care pot fi îndreptate reguli
de acces — *„felul de a spune «exact aceste peșteri»"* fără a scrie o regulă per peșteră.

- Adaugă și scoate obiecte căutându-le.
- **Schimbarea obiectelor unui set mută accesul**, pentru că regulile arată către set. Fiecare
  modificare este înregistrată.
- Un set către care încă arată reguli nu poate fi șters până nu sunt scoase acele reguli.

Dulapurile fac echivalentul pentru documente — vezi
[Documente și dulapuri](../features/documents-and-cabinets.md#dulapuri).

---

## „De ce vede persoana asta lucrul acela?"

Fiecare obiect poartă **Accesul tău aici și motivul lui**.

Pentru fiecare acțiune spune **Permis** sau **Refuzat** și numește motivul:

| Sursă | |
|---|---|
| *Administrare deplină: apartenența decide înainte de orice regulă.* | |
| *Acordat prin proprietatea asupra obiectului.* | |
| *Acordat prin vizibilitatea obiectului.* | |
| *Decis la nivelul de [obiect / colecție / global] de „[numele regulii]”.* | |
| *Decis la nivelul de … de o regulă pe care nu o poți vedea.* | Ți se spune că o regulă a decis, nu ce spune regula |
| *Decis de o regulă scrisă direct pe acest obiect.* | |
| *Nicio regulă nu acordă acest drept.* | |

Aceasta este unealta pentru fiecare discuție „dar de ce nu poate Maria să deschidă aia?".
Începe de aici.

Pentru a vedea **din ce grupuri de permisiuni face parte un cont** — numit direct sau ajuns acolo
printr-unul dintre grupurile lui de speologie — deschide rândul lui la
[Administrare → Conturi](accounts.md#registrul). Lista vine din aceeași citire a apartenențelor după
care se decid drepturile însele, deci nu poate fi în dezacord cu ele.

---

## Ce nu fac permisiunile

- **Nu trec peste [protecția locațiilor](location-protection.md).** Vederea unei poziții exacte
  este o acțiune proprie (*Locație exactă*), acordată separat.
- **Nu circulă printr-o invitație.** A fi invitat pe o tură nu acordă nimic.
- **Nu ajung la cineva fără cont.** Un speolog din listă care nu se poate autentifica nu poate
  primi nimic.

---

Înrudite: [Protecția locațiilor](location-protection.md) ·
[Persoane și cluburi](../features/people-and-clubs.md) ·
[Istoric și modificări](../features/history-and-audit.md)
