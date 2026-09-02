# Spațiul de lucru al hărții

🇬🇧 [English](../../features/map-workspace.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
[Vederi salvate și ferestre](saved-views-and-windows.md)

---

Harta este locul de unde încep majoritatea sesiunilor. Este o suprafață de lucru, nu o poză:
tot ce se află pe ea este selectabil, mare parte este editabilă, și este sincronizată cu
panourile de alături.

---

## Arborele de straturi

Straturile vin în grupuri.

### Hărți de bază

Fundalul. Ce se oferă depinde de ce a configurat instalarea — de obicei OpenStreetMap, un strat
satelitar și o umbrire de relief. **Schimbă harta de bază** comută între ele, iar fiecare are
propriul cursor de **opacitate**.

### Hărți suprapuse

Straturi suplimentare de dale configurate de instalare, desenate peste fundal.

### Straturile hărții (datele dumneavoastră)

| Strat | Arată |
|---|---|
| **Intrări în peșteri** | Grupate la zoom mic; puncte individuale pe măsură ce apropiați |
| **Peșteri** | Fișele de peșteră propriu-zise |
| **Fenomene de suprafață** | Tot restul cadastrului |
| **Poligonații** | Linia topografică proiectată pe suprafață |
| **Fișiere importate** | Câte un strat pentru fiecare fișier de geodate încărcat |
| **Hărți georeferențiate** | Foi raster — vedeți [Hărți raster georeferențiate](georeferenced-maps.md) |
| **Fotografii geolocalizate** | Fotografiile care poartă o poziție |
| **Hartă de densitate a intrărilor** | Densitate în loc de puncte individuale |

### Rărirea etichetelor

**Rărește etichetele aglomerate** face ca numele care s-ar suprapune să se dea unele altora la o
parte. Marcajele se desenează întotdeauna — doar *etichetele* lor concurează pentru spațiu. Util
într-o zonă carstică densă, înșelător dacă uitați că l-ați pornit.

### Detaliul poligonațiilor

Poligonațiile sunt stratul scump, așa că au controale proprii:

- **Detaliu de la zoom-ul** — de la ce nivel apare detaliul topografic complet (inclusiv
  radialele). Sub el vedeți conturul galeriilor, iar vizările de perete apar pe măsură ce
  apropiați.
- **Buget de linii** — o limită a numărului de trasee desenate.

Lăsați-le goale ca să urmați valorile implicite ale instalării. Detaliul mai mare costă mai mult
la desenare. Dacă se rețin poligonații, vi se spune: *„încă n poligonații neafișate la acest
zoom"*.

---

## Selectarea lucrurilor

**Clic** pe un element ca să-l selectați. **Ctrl sau Shift + clic** adaugă la selecție.

**Panoul de detalii** din dreapta are două file:

- **Selecție** — ce ați selectat. Cu mai multe selectate arată câte sunt, care sunt lizibile aici
  și dacă selecția este mixtă.
- **În vizor** — tot ce este încărcat în zona vizibilă.

Acțiuni: **Apropie la selecție**, **Apropie la tot**, **Golește selecția**, **Etichetează tot**.

Panoul însuși este configurabil. Secțiunile lui — Detalii, Etichete, Fișiere, Legături, Istoric,
Permisiuni, Text — pot fi **reordonate** (prin tragere sau cu Alt + săgeți), **ascunse**
individual și readuse. Puteți salva **aranjamente** denumite, puteți stabili **spațierea** și
puteți alege dacă panoul **împinge harta la o parte** sau **plutește peste ea**.

Există butoane **înapoi și înainte** pentru deplasarea prin istoricul selecției.

## Grupări

La zoom mic, intrările se grupează. Un clic pe o grupare vă spune câte intrări sunt în acea zonă
și oferă **Apropie aici**. Vi se spune *„Apropiați ca să listați intrările individuale"*, în loc
să primiți o listă înșelătoare.

## Poziții aproximative

O înregistrare a cărei poziție exactă nu aveți voie s-o vedeți este desenată aproximativ și
etichetată: **„Locație aproximativă — coordonatele exacte sunt protejate"**, sau, pe hartă, forma
scurtă *aprox.* Un element a cărui geometrie este reținută complet spune **„Locație
nedivulgată"**.

Aceasta este [protecția locațiilor](../admin/location-protection.md). Aplicația nu ascunde că
înregistrarea există; ascunde unde este.

---

## Desenare și editare

Bara de editare:

| Unealtă | Face |
|---|---|
| **Desenează** | Plasează o geometrie nouă de tipul de element ales |
| **Modifică** | Trageți vârfuri. Alt+clic șterge unul |
| **Mută geometria** | Translatează toată forma |
| **Aliniere la elementele vizibile** | Vârfurile se aliniază la ce este desenat |
| **Anulează / Refă** | Istoric complet al sesiunii de editare |
| **Renunță la modificări** | Aruncă sesiunea |

În timpul desenării: clic pentru a plasa puncte, dublu-clic pentru a termina o linie sau o zonă.
Bara arată **Termină** și **Punct** (scoate ultimul punct). Pe ecran tactil, atingeți întâi un
vârf și apoi **Șterge vârful**.

**Alegeți un tip de element** înainte de a desena. Tipurile sunt grupate în **Puncte, Linii,
Zone** și *Altele*, iar cele folosite des pot fi **fixate** pe bară.

Există unelte dedicate pentru **Peșteră nouă aici** și **Intrare nouă aici** — clic pe hartă și
formularul se deschide cu coordonatele completate. Formularul de intrare întreabă de care peșteră
aparține, tipul ei și dacă să se **protejeze locația exactă**.

Lucrul nesalvat este numărat în bară: *„n modificări nesalvate"*.

### Măsurare

**Măsoară distanța** și **Măsoară suprafața**. Măsurătorile sunt geodezice — țin cont de
curbura Pământului, nu măsoară pe proiecția plană.

---

## Meniul contextual

Clic dreapta oriunde pe hartă:

- **Adaugă element**
- **Peșteră nouă aici**
- **Intrare nouă aici**
- **Copiază coordonatele**

---

## Alte controale ale hărții

| Control | Face |
|---|---|
| **Arată locația mea** | Folosește geolocalizarea browserului |
| **Compară rasterul cu harta de dedesubt (glisare)** | Un separator glisant pentru comparație |
| **Ascunde barele hărții** | Eliberează ecranul pentru o captură sau un ecran mic |
| **Arată scena 3D lângă hartă** | Împarte spațiul de lucru — vedeți [Vizualizarea 3D](3d-view.md) |
| **Cea mai mică distanță** | Măsoară cât de aproape ajung două peșteri — vedeți [Măsurători](measurements-and-statistics.md#cea-mai-mică-distanță) |

## Căutarea

Caseta de căutare a hărții acoperă **elemente, ture și locuri**: înregistrările dumneavoastră,
plus nume de locuri de la un serviciu extern de geocodare (Nominatim). Rezultatele sunt grupate —
Peșteri, Elemente, Jurnale de tură, Tabere, Locuri și potriviri din interiorul documentelor.
Fiecare oferă **Apropie la** sau **Deschide**.

Vedeți [Căutare și filtre](search-and-filters.md).

---

## Pe telefon

Spațiul de lucru se adaptează la atingere, inclusiv editarea completă a geometriei cu degetul.
Panoul se pliază; **Arată panoul** / **Ascunde panoul** îl comută. Aplicația se poate și instala
pe ecranul de pornire.

---

Înrudite: [Vederi salvate, panouri și ferestre multiple](saved-views-and-windows.md) ·
[Fenomene de suprafață](surface-features.md) ·
[Hărți raster georeferențiate](georeferenced-maps.md)
