# Flux: importul unui sezon de puncte GPS

🇬🇧 [English](../../workflows/import-a-gps-file.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Geodate](../features/geodata.md) ·
[Reguli de detectare](../admin/vocabularies.md#configurare--reguli-de-detectare)

---

Un GPS golit la sfârșit de sezon este cazul clasic dificil: două sute de puncte, jumătate
numite cum numește clubul lucrurile (`P. Urșilor`, `Aven 3`, `Izbuc`, `Doline SE`), jumătate
numite `WPT047`, plus câteva trasee.

Desenarea fișierului pe hartă este doar jumătate din treabă. Ecranul de verificare al aplicației
transformă punctele în peșteri, intrări și fenomene de suprafață reale — și propune ce este
fiecare, pornind de la obiceiurile de denumire ale clubului tău.

---

## Pasul 1 — Încarcă fișierul

**Geodate → Fișiere vectoriale**, apoi trage fișierul înăuntru (sau fă clic pentru a-l
alege).

Acceptate: **GPX, KML, KMZ, GeoJSON, shapefile arhivat, WKT și CSV cu coloane de coordonate.**

Fișierul este importat în fundal. Starea trece *În așteptare → Se importă… → Importat*. Din acel
moment este un strat pe care îl poți aprinde în arborele de straturi al hărții — geometria
este pe hartă, iar traseele lui sunt desenate și în vizualizarea 3D, dar nimic nu a intrat încă în
registru.

> **Un traseu pe unde a mers echipa într-o singură tură** își are locul pe acea tură, nu în
> registru: folosește în schimb **Importă traseul (GPX)** de pe pagina turei, care face din traseu
> schița turei. Vezi [Schița turei](../features/trips.md#schița-turei).

## Pasul 2 — Deschide verificarea

Din lista de geodate, deschide **Verifică și importă în registru**.

Verificarea este un spațiu de lucru, nu o fereastră de dialog:

- o **previzualizare pe hartă** a candidaților,
- un **tabel** cu fiecare candidat și ce ar deveni,
- un panou **Ce revendică fiecare regulă**, care arată ce a prins fiecare regulă de detectare,
- și panourile de opțiuni descrise mai jos.

**Verificarea supraviețuiește închiderii filei.** Poți s-o lași pe jumătate decisă și să te
întorci.

## Pasul 3 — Stabilește opțiunile

### Reguli și denumire

- **Set de reguli** — care set de reguli de detectare se aplică. Seturile pot aparține
  instalării, unui grup de speologie sau ție. Vezi
  [Reguli de detectare](../admin/vocabularies.md#configurare--reguli-de-detectare).
- **Limbi** — participă doar termenii din aceste limbi. Termenii marcați ca aparținând
  niciunei limbi participă întotdeauna.
- **Prefix pentru nume** — pus înaintea a tot ce se creează.

O regulă recunoaște un termen în numele unui punct (sau în descriere) și propune ce devine.
`P. Urșilor` devine o peșteră numită *Urșilor*, cu termenul scos înapoi din nume. Când două
reguli pretind același candidat, **câștigă cea aflată mai sus în set**, iar verificarea îți spune
ce alte reguli l-au mai pretins.

### Ce se creează

- **Caută ceva deja existent pe o rază de** *n* metri — fiecare candidat spune apoi dacă
  registrul are deja ceva în apropiere. Se compară doar cu obiectele a căror poziție exactă
  ai voie s-o vezi; o peșteră protejată pe care nu o poți localiza nu este folosită tacit
  ca potrivire. Distanța de pornire și cât de asemănătoare trebuie să fie două nume ca să conteze
  drept același lucru se stabilesc pentru toată instalarea la
  [Mesagerie → Import vectorial](../admin/messaging.md#import-vectorial).
- **Altitudinea GPS** — păstrează ce spune fișierul, lasă gol, sau decide per candidat. Nu se
  ia nimic dintr-un model de elevație: o grilă publică lângă o faleză nu este o îmbunătățire
  față de un număr măsurat.
- **Marchează tot ce se creează ca locație protejată** — dacă zona este sensibilă, stabilește
  o dată aici, în loc să editezi cincizeci de înregistrări după aceea.

### Punctele pe care nicio regulă nu le-a recunoscut

Acest panou decide cât te costă jumătatea `WPT047` a fișierului:

- **Lasă-le pentru verificare, unul câte unul** (implicit, prudent),
- **Propune un fenomen de suprafață** de un tip ales de tine,
- **Propune o intrare de peșteră** de un tip ales de tine.

### Trasee și rute

Lasă-le în fișier, sau importă-le ca obiect de tip linie, de felul pe care îl alegi.

### Care câmp este care

De obicei *Determină automat*. Suprascrie dacă fișierul pune numele într-un câmp de descriere sau
poartă codul de identificare într-un loc neobișnuit.

## Pasul 4 — Lucrează tabelul

Tabelul are un rând pe candidat, cu coloanele: **Nume · Devine · Tip · Regulă · Poziție ·
Deja existent · Decizie**.

Îngustează-l în timp ce lucrezi: caută nume, filtrează după regulă, după fel, după puncte /
trasee / zone, sau afișează **doar cele care au ceva în apropiere** — cel mai rapid mod de a
găsi duplicatele.

**Decizia** fiecărui rând este una dintre:

| Decizie | Efect |
|---|---|
| **Creează** | Se face o peșteră, o intrare sau un fenomen de suprafață nou |
| **Deja existent** | Candidatul este recunoscut ca o înregistrare existentă — *„Este aceasta"* sau *„A doua intrare la X"* |
| **Sari peste** | Nu se întâmplă nimic pentru acest rând |

Selectează rânduri individual, toate de pe pagină, toate de pe toate paginile, sau inversează.

> **Fișiere mari.** O verificare citește un număr limitat de rânduri. Dacă fișierul este mai
> mare, primești *„Sunt afișate primele N din M rânduri"* — confirmă-le și deschide din nou
> verificarea pentru restul.

## Pasul 5 — Confirmă

Fie **Creează *n* obiecte** pentru rândurile selectate, fie **Creează tot ce au revendicat
regulile**, dacă ai încredere în corespondență în bloc.

**Nimic nu se creează până nu apeși asta.** O confirmare mare arată cât a înaintat — *„n din m
rânduri parcurse"* — și, dacă a întrerupt-o o eroare trecătoare, că se reia în scurt timp. După
aceea primești un raport: ce s-a creat, ce a
fost recunoscut ca deja existent, ce s-a sărit și — dacă vreun rând a eșuat — de ce, ca să
poți repara și confirma separat.

## Pasul 6 — Dacă a fost greșit, anulează

**Geodate → Importuri** listează fiecare import confirmat: fișierul din care a venit, cum s-a
făcut (verificat sau fără verificare), când a fost confirmat și ce a creat.

**Anulează** șterge toate obiectele create de acel import, dintr-o singură apăsare. Inclusiv pe
cele agățate de peșteri care existau deja.

Aceasta este plasa de siguranță care face rezonabil să încerci un set de reguli agresiv și să
vezi ce iese.

---

## Potrivirea regulilor pentru clubul tău

Setul livrat cunoaște termenii carstici obișnuiți în română și engleză. Clubul tău
scrie aproape sigur unele lucruri în felul lui.

**Configurare → Reguli de detectare.** Oricine are ceva de importat își ține propriile seturi;
doar promovarea unui set la ce moștenește un grup sau întreaga instalare este un act de
administrator.

O regulă are: un nume, dacă este activă, după ce compară (conține / cuvânt întreg / începe cu /
expresie regulată), termenii, limba lor, ce propune (o peșteră, o intrare, un fenomen de suprafață) și de ce
tip, dacă citește numele și/sau descrierea, și dacă scoate termenul înapoi din nume (și de
unde).

Un set poate fi **salvat ca fișier** și **încărcat dintr-un fișier** — așa dai convențiile
clubului altui club.

---

## Exportul înapoi

Direcția inversă se află pe paginile de listă și pe hartă: peșterile, obiectele și fișierele de
geodate se exportă ca **GeoJSON, GPX, KML, CSV sau shapefile arhivat**.

---

Urmează: [Fotografii în locuri](photographs-to-places.md) ·
Referință: [Geodate](../features/geodata.md)
