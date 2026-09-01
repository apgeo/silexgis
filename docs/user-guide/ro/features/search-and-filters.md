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
