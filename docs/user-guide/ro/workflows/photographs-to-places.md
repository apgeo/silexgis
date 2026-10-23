# Flux: transformarea fotografiilor unei ture în locuri

🇬🇧 [English](../../workflows/photographs-to-places.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Fotografii](../features/photographs.md)

---

Te-ai întors dintr-un weekend cu patru sute de poze și niciun punct GPS. Telefoanele moderne și
multe aparate scriu o poziție în fiecare cadru. Acest flux transformă asta în înregistrări pe
hartă — fără să te pună să respingi patruzeci de puncte unul câte unul.

**Nimic nu se scrie vreodată înapoi în fișierele tale de imagine.** Fiecare poziție,
nume și decizie trăiește în verificare și apoi în înregistrările pe care le creează.

---

## Pasul 1 — Deschide verificarea

Două căi:

- **Geodate → Din fotografii**, sau
- din pagina unei ture: **Creează obiecte din fotografiile turei** — care clasează totul
  sub acea tură.

Apoi lasă pozele înăuntru.

## Pasul 2 — Înțelege ce a făcut, înainte să atingi ceva

Verificarea grupează pozele în **locuri**, nu în puncte. Douăsprezece fotografii ale unei
intrări sosesc ca *un singur candidat cu o galerie*. Un puț fotografiat de la buză și de la bază
este o judecată de valoare, motiv pentru care distanța de grupare este o setare.

Fiecare rând de candidat arată: **Poze · Nume · Devine · Tip · Poziție · Deja existent ·
Decizie**.

Coloana **Poziție** este cea de citit cu atenție, pentru că spune *cum* a fost plasat locul și
*cât valorează asta*:

| Sursă | Înseamnă |
|---|---|
| **De la aparat** | Poziția pe care a înregistrat-o aparatul când s-a făcut poza |
| **Dintr-un traseu** | Dedusă comparând momentul capturii cu un traseu înregistrat |
| **Așezată manual** | Cineva a tras-o pe hartă |
| **Neașezată** | Nici aparatul, nici un traseu nu au plasat-o |

Fixările de la aparat poartă și o calitate — *excelentă / bună / moderată / necunoscută* — așa
cum a raportat-o aparatul.

Dacă o poză a înregistrat **încotro era îndreptat aparatul**, acel azimut este desenat pe hartă.
Adesea asta transformă „undeva pe versantul ăsta" într-o gaură la care poți merge înapoi.

## Pasul 3 — Salvează pozele fără poziție

Verificarea îți spune câte sunt. Două moduri de a le repara:

**Potrivirea cu un traseu.** Alege un traseu GPX pe care l-ai parcurs. Apoi stabilește:

- **Decalajul ceasului aparatului** — adăugat la ora declarată a fiecărei poze înainte de
  comparație. Ceasurile aparatelor derivează, iar unul lăsat pe alt fus orar este greșit cu ore
  întregi arătând perfect plauzibil. Acest câmp este diferența dintre o plasare și o ficțiune.
- **Potrivește în limita a** *n* secunde — cât de departe de o poziție înregistrată mai poate
  fi plasată o poză. Dincolo de asta, traseul chiar nu spune nimic despre unde a fost făcută.

Fiecare rând potrivit arată apoi la câte secunde se află de o fixare reală. Câteva secunde
înseamnă o plasare; două minute înseamnă o presupunere.

**Sau plaseaz-o manual.** Selectează rândul, **Așază pe hartă**, clic. Se scrie în verificare,
niciodată înapoi în fișier.

## Pasul 4 — Stabilește opțiunile

**Locuri și grupare**
- *Fiecare loc devine* — felul implicit pentru un candidat nou.
- *Prefix de nume*.
- *Două fotografii sunt același loc în limita a* — distanța de grupare.

**Ce se creează**
- *Caută ceva deja existent în limita a n metri* — ca la importul GPS, se compară doar cu
  obiectele a căror poziție exactă ai voie s-o vezi.
- *Altitudinea capturii* — lăsată deoparte dacă nu o ceri. Altitudinea GPS a unui telefon
  este cel mai puțin fiabil dintre cele trei numere pe care le raportează.

## Pasul 5 — Decide fiecare loc

Fiecare candidat îți oferă ce se află deja în registru prin apropiere. Clasarea unei poze pe
peștera căreia îi aparține este o singură apăsare — nu trebuie să creezi o înregistrare duplicat
doar ca să ai unde pune fotografia.

Sau **creează una nouă**, alege-i tipul, dă-i nume.

## Pasul 6 — Confirmă, și anulează dacă a fost greșit

**Creează *n* selectate.** Nimic nu există până atunci.

Iar ca la importul GPS, întreaga confirmare apare la **Geodate → Importuri** și iese înapoi
dintr-o singură apăsare — inclusiv pozele agățate de peșteri care existau deja.

---

## Ce se întâmplă cu fotografiile în sine

Ajung în [galerie](../features/photographs.md). De acolo poți:

- să le dai **legende**, un **fotograf** (din listă, sau un nume tastat) și o **licență**
  (CC0, familia CC BY, sau toate drepturile rezervate),
- să le pui în **albume**, să le aranjezi ordinea, să alegi o copertă,
- să le **rotești**, în masă,
- să marchezi una cu stea ca **fotografie reprezentativă** a unei peșteri sau a unei ture,
- să **publici** fotografii individuale în galeria publică a instalării, ceea ce este o
  decizie separată de cine are voie să le citească.

> **Descărcarea originalelor este controlată.** Dacă punctul de capturare al unei fotografii ar
> localiza o peșteră a cărei poziție exactă nu ai voie s-o vezi, ți se spune că nu poți
> descărca originalul. Randarea se vede în continuare. Aceasta este
> [protecția locațiilor](../admin/location-protection.md) făcându-și treaba.

---

## Fotografii din subteran: plasate după oră, nu după GPS

Tot ce este mai sus plasează o fotografie după poziția pe care o poartă fișierul ei, iar un
aparat în subteran nu înregistrează niciuna. O fotografie făcută într-o **tură urmărită** se
plasează altfel: după *când* a fost făcută și *despre cine* este, citite față de
[jurnalul de urmărire](../features/live-tracking.md) al turei.

1. Pune fotografiile pe tură (fila **Fișiere**), ca la orice tură.
2. În fila **Urmărire** a turei alege **Adaugă fotografii** — sau butonul cu poză de pe rândul
   unui raport, care deschide dialogul la momentul acelui raport.
3. Bifează fotografiile. Fiecare este pusă la momentul pe care îl spune fișierul ei; una al cărei
   fișier nu spune nimic este semnalată și primește momentul la care a fost deschis dialogul.
4. Spune despre cine sunt, o dată sus sau pentru fiecare fotografie pe rândul ei.
5. **Citește rândul de sub fiecare înainte de a atașa**: numește stația la care reluarea va
   desena acea fotografie. Dacă rândurile spun *Până atunci nu fusese raportată nicio stație
   pentru această persoană — doar pe cronologie*, sau numesc o stație la care poza evident nu a fost făcută, ceasul
   aparatului era greșit — corectează-l o singură dată la **Ceasul aparatului era înainte cu** și
   toate rândurile îl urmează.

O fotografie despre nimeni anume rămâne pe cronologie la momentul ei și nu se desenează la nicio
stație: o echipă care s-a împărțit este în două locuri. Nimic de aici nu scrie un loc pe
fotografie — corectează ulterior un raport și fotografia se mută odată cu el. Și nimic de aici
nu îți spune o stație pe care nu o puteai vedea deja: acolo unde poziția peșterii îți este
ascunsă, rândul spune asta. Rândurile sunt explicate unul câte unul la
[Echipa pe ridicare](../features/live-tracking.md#echipa-pe-ridicare).

**Sau dă-i o stație pentru totdeauna.** Când știi unde în subteran a fost făcută o fotografie,
iar ea ține de peșteră, nu de o singură tură, leag-o de acea stație: deschide topografia
(**Vezi în 3D** pe pagina peșterii), fă clic pe stație, apoi **Leagă asta…**, și leagă
fotografia. Stația poartă apoi un mic aparat foto în vizualizatorul topografic și în vizualizarea
3D, iar apăsarea lui arată fotografiile făcute acolo. Vezi
[Topografii și modele](../features/surveys-and-models.md#modele-topo-3d) și
[Legături](../features/links.md).

---

Urmează: [Planificarea și redactarea unei ture](plan-and-log-a-trip.md) ·
Referință: [Fotografii](../features/photographs.md)
