# Topografii, poligonații și modele 3D

🇬🇧 [English](../../features/surveys-and-models.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
[Măsurători și statistici](measurements-and-statistics.md)

---

SilexGIS afișează topografii; nu le compilează. Aduceți ce produc Therion sau Survex, iar
aplicația le desenează, le măsoară, le proiectează pe hartă și păstrează sursele din care le-ați
făcut.

Tot ce urmează trăiește pe **pagina unei peșteri**.

---

## Modele topo 3D

**Încarcă model.** Acceptate: **Therion `.lox`**, **Survex `.3d`**, sau **pereți de peșteră ca
`.stl` binar** — până la 100 MB.

Stare: *În așteptare → În curs → Gata*, sau *Nu a putut fi procesat*.

Un model este desenat în **vizualizatorul topografic** (CaveView.js) și, pentru pereți, în
[scena 3D](3d-view.md).

### Citirea unei topografii în rânduri

O topografie compilată nu este lăsată ca un fișier pe care îl desenează browserul. Este **citită
în stațiile și vizările ei**, ceea ce face posibile
[statisticile](measurements-and-statistics.md). Graficul poate fi privit în timp ce asta se
întâmplă; lista se actualizează singură când citirea s-a terminat.

### Unde stă o topografie în lume

Aici greșesc oamenii, așa că formularele sunt explicite.

- **Un fișier `.lox` nu are câmp pentru un sistem de coordonate**, deci nu spune niciodată unde
  se află. Trebuie să spuneți ce înseamnă numerele lui, altfel topografia nu poate fi plasată.
- **Un fișier `.3d` își declară sistemul propriu** doar dacă topografia a fost compilată cu unul.
  Dacă îl declară, acela este folosit și ce spuneți dumneavoastră este ignorat.

### Sistemele de coordonate vin din instalare, nu de pe internet

Când dați un cod EPSG — pentru un `.stl` sau pentru o topografie al cărei fișier nu-și declară
sistemul — definiția este rezolvată **din baza de date de sisteme de coordonate care vine cu
aplicația**, nu adusă de la un serviciu web public.

Rezultă două lucruri:

- **O instalare fără ieșire la internet georeferențiază corect topografiile.** Nimic nu face o
  cerere externă în momentul citirii unei topografii.
- **Grilele naționale funcționează.** Stereo70 (EPSG:31700) este cazul evident: nu este
  precodificat în vizualizator, iar pe o instalare care ar fi trebuit să aducă definiții și-ar
  pierde georeferențierea sau n-ar mai putea fi încărcat deloc.

Dacă sunteți obișnuit ca topografiile să-și piardă tăcut poziția pe un sistem găzduit propriu,
iată de ce aici nu se întâmplă.

### Precizie pierdută înainte de sosire

Dacă coordonatele dintr-un fișier au fost prea mari pentru numerele în care le stochează,
topografia ajunge mai grosieră decât a fost ridicată. Aplicația detectează asta și o spune: *„
Fișierul a pierdut precizie înainte să ajungă."* Reexportarea în jurul unei origini locale o
recuperează.

---

## Pereții peșterii (`.stl`)

Un fișier `.stl` este triunghiuri goale. Nimic din el nu spune în ce sistem de coordonate sunt
numerele — citit ca fusul vecin, ar ateriza la kilometri distanță. Așa că îl declarați:

**Metri față de un punct** — exportul obișnuit. Numerele sunt metri est, nord și sus față de un
punct. Dați **longitudinea și latitudinea** acelui punct și **altitudinea nivelului zero al
fișierului**.

> Formularul precompletează originea din **intrarea principală a acestei peșteri**, de unde
> pornește de obicei un export local. Dacă nu este înregistrată nicio poziție de intrare, sau
> dacă poziția arătată *contului dumneavoastră* este deliberat aproximativă pentru că locația
> peșterii este protejată, formularul o spune și vă cere punctul adevărat.

**Un sistem de coordonate proiectat** — numerele sunt coordonate est/nord într-o grilă națională
sau UTM. Dați **codul EPSG**.

De reținut și: înălțimile din aceste fișiere se măsoară de la originea proprie a exportului, nu
de la nivelul mării.

Odată convertiți, pereții sunt desenați în [scena 3D](3d-view.md#pereții-peșterii-selectate)
numai pentru peștera selectată.

---

## Poligonații

Linia topografică a unei peșteri, proiectată pe suprafața hărții și desenată la adâncime în 3D.

**Încarcă poligonație**, sau lăsați ca una să fie **extrasă** dintr-o topografie compilată —
coloana *Sursă* spune care.

Fiecare poligonație își arată **lungimea** și numărul de **trasee**. Una poate fi făcută **forma
implicită** a peșterii, care este ce folosesc alte lucruri când au nevoie de „forma acestei
peșteri".

Pe hartă, poligonațiile sunt stratul scump și au controale proprii de detaliu și buget — vedeți
[Spațiul de lucru al hărții](map-workspace.md#detaliul-poligonațiilor).

O poligonație fără altitudini este desenată **pe suprafață** în 3D, iar scena spune câte sunt
așa — în loc s-o deseneze la adâncimea zero ca și cum peștera ar fi plată.

---

## Surse topografice

Materialul din care au fost făcute topografiile compilate. **Arhivează o sursă**, până la 100 MB:

| Fel | |
|---|---|
| **Sursă Therion** | `.th`, `.th2` |
| **Therion `.thconfig`** | |
| **Jurnal de compilare** | `.log` |
| **Sursă Survex** | `.svx` |
| **Export de aplicație de topografie** | un `.zip` |

Fiecare poartă o mărime, o revizie și o descriere, și poate fi descărcată.

> **Un singur fel este citit, și doar pentru ce spune despre compilare.** Un jurnal de
> compilare este deschis pentru erorile de buclă pe care le raportează (vezi *Închiderea
> topografiei*, mai jos); restul este păstrat ca octeți și nu este interpretat niciodată. Acesta
> este tot rostul: peste zece ani `.lox`-ul poate fi ilizibil, iar `.th`-ul nu va fi. Nimic de
> aici nu este vreodată rescris.

Conținutul fișierului este verificat față de ce pretinde numele lui — un `.svx` care nu este
`.svx` este refuzat, nu arhivat sub o minciună.

Scoaterea unei surse din arhiva peșterii păstrează fișierul stocat.

### Închiderea topografiei

Când un jurnal de compilare este arhivat, instalarea citește ce a tipărit compilatorul despre
acea rulare și arată rezultatul pe pagina peșterii — sub titlurile compilatorului însuși, ca o
cifră de aici și una din jurnalul tău să fie vizibil același număr.

Pentru fiecare buclă închisă raportată: **eroarea relativă** (`REL-ERR`, un raport) și **eroarea
absolută** (`ABS-ERR`, o distanță în metri), lungimea buclei, câte stații străbate, componentele
pe axe și lanțul de stații. **Ambele măsuri sunt arătate împreună și niciuna nu este prezentată
drept cealaltă** — ele nu sunt de acord care buclă este cea mai proastă, iar tabelul spune
întotdeauna după care dintre ele este ordonat în acel moment.

Alături de tabel: compilatorul și versiunea lui, data lui de lansare, când a citit instalarea
jurnalul și ce revizie a fișierului arhivat a fost citită. Un jurnal corectat, încărcat ca o
revizie nouă a aceluiași fișier, este citit din nou, iar cifrele rulării anterioare sunt șterse,
nu lăsate sub numărul noii revizii.

Formulările deosebesc cazuri care seamănă și nu sunt la fel: un jurnal care nu a putut fi
deschis, o compilare oprită la jumătate, o topografie fără bucle închise și un jurnal care nu a
tipărit deloc un tabel de bucle.

Cifrele de închidere urmează înregistrarea topografică de care aparțin: un cititor căruia nu i se
poate spune poziția exactă a unei peșteri nu le vede — nici pe hartă, nici în panou, nici în
istoricul peșterii.

---

## Ce vă spune apoi topografia

Citirea unei topografii în stații și vizări este ce alimentează:

- **Statisticile din topografie** de pe pagina peșterii — lungime, lungime în plan, extindere
  verticală, cea mai mare deschidere, verticalitate, sinuozitate, punctul cel mai înalt și cel mai
  jos, și cifrele calculate față de cele declarate,
- **roza orientării galeriilor** și înclinarea,
- **cea mai mică distanță** dintre două peșteri,
- poligonațiile de pe hartă și din 3D.

Toate acestea sunt pe pagina
[Măsurători și statistici](measurements-and-statistics.md).

---

Înrudite: [Vizualizarea 3D](3d-view.md) · [Peșteri și intrări](caves-and-entrances.md) ·
[Măsurători și statistici](measurements-and-statistics.md)
