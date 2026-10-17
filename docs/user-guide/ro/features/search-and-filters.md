# Căutare și filtre

🇬🇧 [English](../../features/search-and-filters.md) · 🇷🇴 **Română**

[← Referință](README.md)

---

## Caseta de căutare

În bara de sus și pe hartă. O casetă, mai multe feluri de răspuns, grupate:

| Grup | |
|---|---|
| **Peșteri** | |
| **Elemente** | |
| **Jurnale de tură** | |
| **Tabere** | |
| **Locuri** | Nume de locuri de la un serviciu extern de geocodare (Nominatim) |
| **În textul documentelor** | Ce scrie *în interiorul* documentelor dumneavoastră |

Fiecare rezultat oferă **Apropie la** sau **Deschide**, după caz.

### Căutarea în interiorul documentelor

Partea distinctivă. Un rezultat de document **citează propoziția care s-a potrivit** și deschide
pagina documentului **la pagina pe care s-a găsit expresia** (sau foaia, sau diapozitivul).

- **Insensibilă la diacritice în ambele sensuri**: `pestera` găsește `peșteră`, iar rezultatul
  este citat înapoi scris așa cum l-a scris autorul.
- Cuvintele sunt **lematizate în limba în care este scris documentul** — română și engleză din
  start.
- *„Se afișează n din m documente potrivite"* când sunt mai multe.
- Dacă nu s-a potrivit nimic: *„Niciun text de document nu s-a potrivit. Paginile scanate nu
  conțin text de căutat până nu sunt tastate."* — care de obicei este răspunsul real.
- O **versiune înlocuită** este marcată *„versiunea n, înlocuită"* și este găsibilă doar de cei
  care ar fi putut-o înlocui.

**Găsiți doar ce aveți voie să citiți.**

---

## Filtre

Un constructor de filtre pentru interogări salvate și partajabile peste mai multe feluri de
înregistrări deodată.

### Lumi

Un filtru acoperă unul sau mai multe **feluri de obiecte**: **Elemente · Jurnale de tură · Vederi
de hartă · Documente**. Fiecare apare o singură dată, și există o limită câte acoperă un filtru.

### Câmpuri după care puteți pune condiții

**Nume · Titlu · Fel · Categorie · Tip · Etichetă · Proprietar · Grup de speologie · Grup
organizator · Vizibilitate · Locație protejată · Creat · Ultima modificare · Tip de tură · Data
turei · A avut incident · Stare · Limbă**

### Sortare

**Adăugat · Modificat · Nume · Proprietar · Dată · Cel mai apropiat**

Dacă nimic din ce se caută nu poate fi sortat într-un anumit fel, vă spune, în loc să sorteze
arbitrar.

### Limite, declarate

Un filtru este mărginit, iar fiecare margine are mesajul ei: prea multe părți (*„Încercați să-l
împărțiți"*), imbricare prea adâncă, prea multe valori într-o condiție, text prea lung într-o
condiție, cel puțin un fel de obiect obligatoriu.

### Selectorul

Acolo unde alegeți valori, selectorul spune ce face:

- *„Tastați cel puțin n caractere"*,
- *„Nimic nu se potrivește"* / *„Nimic ales"* / *„n alese"*,
- și, important, **„n neafișate — dispărute sau nu ale dumneavoastră de văzut"**.

Unele valori sunt **localizabile** — *„Vi se poate arăta unde este"* — și oferă un *Mergi la…*
care mută harta acolo.

---

## Filtre pe hartă

Harta citește aceleași filtre. Un filtru salvat, sau un filtru construit în registrul de
peșteri, restrânge intrările de peșteri, elementele de suprafață, liniile de topografie și — în
vederea 3D — pereții peșterilor. Un filtru despre peșteri păstrează intrările acelor peșteri; un
filtru despre intrări păstrează acele intrări. Nu face decât să scoată din ce puteți vedea deja.

- **În adresă.** Adresa hărții poartă filtrul ei la fel ca adresa registrului de peșteri, așa că
  o hartă filtrată este un link pe care îl puteți trimite. Un link al cărui filtru nu poate fi
  citit — sau pe care cel care îl deschide nu îl poate rula, de pildă filtrul dumneavoastră salvat
  privat — deschide harta nefiltrată și spune *„Filtrul din acest link nu a putut fi citit…"*.
- **O notificare îl numește**: *„Hartă filtrată după …"*, cu un buton care îl golește. Un filtru
  care nu mai merge cât priviți (șters, nemaipartajat) golește straturile pe care le restrângea
  și spune asta.
- **Selectorul de etichete din panoul de straturi** este un filtru cu o singură condiție: păstrează
  intrările care poartă eticheta și intrările peșterilor care o poartă.
- **Vederile salvate** țin minte filtrul și îl readuc când sunt aplicate. Un **link partajat**
  către o vedere restrânsă de mai mult decât o etichetă arată zona și fundalul, dar nicio peșteră,
  pentru că în spatele unui link nu este nimeni ca care să ruleze filtrul; dialogul de partajare
  spune asta.
- **Un loc nu intră în vederile salvate.** Un filtru care conține un loc anume — un punct
  introdus, un contur lipit sau locul de la care se măsoară *Cel mai apropiat* — funcționează în
  adresa hărții, dar nu se salvează niciodată într-o vedere, pentru că o vedere este arătată
  tuturor celor cu care este partajată. Salvarea uneia spune asta; măsurați în schimb de la un
  obiect sau de la o zonă numită, ori aplicați un filtru salvat.

### Straturi din filtre salvate

**Adaugă un strat dintr-un filtru salvat…** în panoul de straturi desenează peste hartă încă o
întrebare: intrările și elementele pe care le păstrează filtrul salvat, într-o culoare și un
simbol proprii, schimbate din rândul stratului. Cel mult șase deodată. Se aprind, se reordonează
și se fac transparente ca orice alt strat și se păstrează în vederile salvate. Un strat al cărui
filtru a dispărut sau nu mai poate fi rulat rămâne în listă marcat **Ilizibil** și nu desenează
nimic. Marcajele acestor straturi sunt doar de privit; selecția rămâne la straturile principale.
Linkurile partajate nu le arată.

### Filtre rapide

Fixați până la șase filtre salvate — ale dumneavoastră sau partajate cu dumneavoastră — din
**Filtre salvate** (*„Fixați ca filtru rapid"*). Apar ca butoane de un clic pe hartă și în bara
registrului de peșteri, în orice browser în care vă autentificați. Un buton aplică filtrul;
apăsat din nou, îl ridică. Un buton al cărui filtru nu mai există sau nu poate fi rulat apare
estompat și spune asta, până îl scoateți, din buton sau din **Filtre salvate**.

---

## Domenii

Acolo unde apare un selector de domeniu, îngustează la: **Peșteri · Intrări · Toate elementele ·
Documente · Ture · Vederi**.

---

## Filtrarea pe paginile de listă

Fiecare pagină de listă are și filtrele ei rapide — căutare după nume, filtrare după tip, fel sau
categorie, filtrare după etichetă. Lista de peșteri, cea de elemente, de ture, de evenimente, de
tabere și listele de documente funcționează toate așa, cu paginare și sortare pe server.

---

Înrudite:
[Documente și dulapuri](documents-and-cabinets.md#căutarea-în-interiorul-documentelor) ·
[Spațiul de lucru al hărții](map-workspace.md#căutarea)
