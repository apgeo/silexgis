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
fiecare, pornind de la obiceiurile de denumire ale clubului dumneavoastră.

---

## Pasul 1 — Încărcați fișierul

**Geodate → Fișiere vectoriale**, apoi trageți fișierul înăuntru (sau faceți clic pentru a-l
alege).

Acceptate: **GPX, KML, KMZ, GeoJSON, shapefile arhivat, WKT și CSV cu coloane de coordonate.**

Fișierul este importat în fundal. Starea trece *În așteptare → Se importă… → Importat*. Din acel
moment este un strat pe care îl puteți aprinde în arborele de straturi al hărții — geometria
este pe hartă, dar nimic nu a intrat încă în cadastru.

## Pasul 2 — Deschideți verificarea

Din lista de geodate, deschideți **Verifică și importă în cadastru**.

Verificarea este un spațiu de lucru, nu o fereastră de dialog:

- o **previzualizare pe hartă** a candidaților,
- un **tabel** cu fiecare candidat și ce ar deveni,
- un panou **Ce pretinde fiecare regulă**, care arată ce a prins fiecare regulă de detectare,
- și panourile de opțiuni descrise mai jos.

**Verificarea supraviețuiește închiderii filei.** Puteți s-o lăsați pe jumătate decisă și să vă
întoarceți.

## Pasul 3 — Stabiliți opțiunile

### Reguli și denumire

- **Set de reguli** — care set de reguli de detectare se aplică. Seturile pot aparține
  instalării, unui grup de speologie sau dumneavoastră. Vedeți
  [Reguli de detectare](../admin/vocabularies.md#configurare--reguli-de-detectare).
- **Limbi** — participă doar termenii din aceste limbi. Termenii marcați ca aparținând
  niciunei limbi participă întotdeauna.
- **Prefix de nume** — pus înaintea a tot ce se creează.

O regulă recunoaște un termen în numele unui punct (sau în descriere) și propune ce devine.
`P. Urșilor` devine o peșteră numită *Urșilor*, cu termenul scos înapoi din nume. Când două
reguli pretind același candidat, **câștigă cea aflată mai sus în set**, iar verificarea vă spune
ce alte reguli l-au mai pretins.

### Ce se creează

- **Caută ceva deja existent în raza de** *n* metri — fiecare candidat spune apoi dacă
  cadastrul are deja ceva în apropiere. Se compară doar cu obiectele a căror poziție exactă
  aveți voie s-o vedeți; o peșteră protejată pe care nu o puteți localiza nu este folosită tacit
  ca potrivire.
- **Altitudine GPS** — păstrați ce spune fișierul, lăsați gol, sau decideți per candidat. Nu se
  ia nimic dintr-un model de elevație: o grilă publică lângă o faleză nu este o îmbunătățire
  față de un număr măsurat.
- **Marchează tot ce se creează drept locație protejată** — dacă zona este sensibilă, stabiliți
  o dată aici, în loc să editați cincizeci de înregistrări după aceea.

### Punctele pe care nicio regulă nu le-a recunoscut

Acest panou decide cât vă costă jumătatea `WPT047` a fișierului:

- **Lasă-le pentru verificare, una câte una** (implicit, prudent),
- **Propune un fenomen de suprafață** de un tip ales de dumneavoastră,
- **Propune o intrare în peșteră** de un tip ales de dumneavoastră.

### Trasee și rute

Lăsați-le în fișier, sau importați-le ca element de tip linie, de felul pe care îl alegeți.

### Care câmp este care

De obicei *Deduce singur*. Suprascrieți dacă fișierul pune numele într-un câmp de descriere sau
poartă codul de identificare într-un loc neobișnuit.

## Pasul 4 — Lucrați tabelul

Tabelul are un rând pe candidat, cu coloanele: **Nume · Devine · Tip · Regulă · Poziție ·
Deja existent · Decizie**.

Îngustați-l în timp ce lucrați: căutați nume, filtrați după regulă, după fel, după puncte /
trasee / zone, sau afișați **doar cele care au ceva în apropiere** — cel mai rapid mod de a
găsi duplicatele.

**Decizia** fiecărui rând este una dintre:

| Decizie | Efect |
|---|---|
| **Creează** | Se face o peșteră, o intrare sau un fenomen nou |
| **Deja existent** | Candidatul este recunoscut ca o înregistrare existentă — *„Este aceasta"* sau *„A doua intrare a peșterii X"* |
| **Sari peste** | Nu se întâmplă nimic pentru acest rând |

Selectați rânduri individual, toate de pe pagină, toate de pe toate paginile, sau inversați.

> **Fișiere mari.** O verificare citește un număr limitat de rânduri. Dacă fișierul este mai
> mare, primiți *„primele N din M rânduri sunt afișate"* — confirmați-le și deschideți din nou
> verificarea pentru restul.

## Pasul 5 — Confirmați

Fie **Creează *n* obiecte** pentru rândurile selectate, fie **Creează tot ce au pretins
regulile**, dacă aveți încredere în corespondență în bloc.

**Nimic nu se creează până nu apăsați asta.** După aceea primiți un raport: ce s-a creat, ce a
fost recunoscut ca deja existent, ce s-a sărit și — dacă vreun rând a eșuat — de ce, ca să
puteți repara și confirma separat.

## Pasul 6 — Dacă a fost greșit, anulați

**Geodate → Importuri** listează fiecare import confirmat: fișierul din care a venit, cum s-a
făcut (verificat sau fără verificare), când a fost confirmat și ce a creat.

**Anulează** șterge toate obiectele create de acel import, dintr-o singură apăsare. Inclusiv pe
cele agățate de peșteri care existau deja.

Aceasta este plasa de siguranță care face rezonabil să încercați un set de reguli agresiv și să
vedeți ce iese.

---

## Potrivirea regulilor pentru clubul dumneavoastră

Setul livrat cunoaște termenii carstici obișnuiți în română și engleză. Clubul dumneavoastră
scrie aproape sigur unele lucruri în felul lui.

**Configurare → Reguli de detectare.** Oricine are ceva de importat își ține propriile seturi;
doar promovarea unui set la ce moștenește un grup sau întreaga instalare este un act de
administrator.

O regulă are: un nume, dacă este activă, după ce compară (conține / cuvânt întreg / începe cu /
expresie regulată), termenii, limba lor, ce propune (o peșteră, o intrare, un fenomen) și de ce
tip, dacă citește numele și/sau descrierea, și dacă scoate termenul înapoi din nume (și de
unde).

Un set poate fi **salvat ca fișier** și **încărcat dintr-un fișier** — așa dați convențiile
clubului altui club.

---

## Exportul înapoi

Direcția inversă se află pe paginile de listă și pe hartă: peșterile, elementele și fișierele de
geodate se exportă ca **GeoJSON, GPX, KML, CSV sau shapefile arhivat**.

---

Urmează: [Fotografii în locuri](photographs-to-places.md) ·
Referință: [Geodate](../features/geodata.md)
