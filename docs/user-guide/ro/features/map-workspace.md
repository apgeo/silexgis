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

Un fundal sau o hartă suprapusă poate fi și un **serviciu de hărți** (WMS sau WMTS) — felul în
care o agenție națională de cartografie își publică de obicei foile — acolo unde instalarea
ta listează unul. Serviciile de hărți se desenează doar pe această hartă și în
[vizualizarea 3D](3d-view.md): niciodată pe harta de ansamblu a [zonelor de lucru](work-areas.md)
și niciodată într-un document, cum ar fi harta din raportul unei ture.

### Straturile hărții (datele tale)

| Strat | Arată |
|---|---|
| **Intrări în peșteri** | Grupate la zoom mic; puncte individuale pe măsură ce apropii |
| **Peșteri** | Fișele de peșteră propriu-zise |
| **Fenomene de suprafață** | Tot restul registrului |
| **Drumuiri peșteri** | Linia topografică proiectată pe suprafață |
| **Fișiere importate** | Câte un strat pentru fiecare fișier de geodate încărcat |
| **Hărți georeferențiate** | Foi raster — vezi [Hărți raster georeferențiate](georeferenced-maps.md) |
| **Fotografii geolocalizate** | Fotografiile care poartă o poziție. Când o imagine cuprinde mai multe decât încap într-un răspuns, rândul stratului spune *„Nu sunt afișate toate fotografiile de aici — apropie harta pentru a le vedea pe celelalte."* |
| **Hartă de căldură (intrări)** | Densitate în loc de puncte individuale |
| **Densitatea peșterilor (grilă)** | Câte peșteri se află în fiecare celulă a unei grile, numărate de server |
| **Indice de carstificare (zone)** | Fiecare zonă carstică și masiv din imagine, colorat după cât de carstificat apare |

### Straturile carstice

Cele două straturi carstice sunt calculate de server peste peșterile pe care le poți citi;
harta nu le deduce niciodată din marcajele pe care le are, care sunt limitate pe imagine. Cât
timp e pornit, fiecare are un rând sub lista de straturi, cu o legendă a culorilor.

- **Densitatea peșterilor (grilă)** arată, celulă cu celulă, **peșteri pe km²**, o densitate
  **netezită** sau **zone fierbinți și reci** (celule clar mai dense sau mai rare decât restul
  imaginii). Rândul spune mărimea celulei folosite: niciodată mai fină decât grila de localizare
  care protejează pozițiile peșterilor, și mai mare când imaginea e largă. Culorile sunt relative
  la imagine — celula cea mai închisă este cea mai densă *aici* — așa că două imagini se compară
  după numerele din legendă, nu după culoare. Ca să compari după culoare, schimbă **Benzi** din
  rând de la **Relative la imagine** la **Fixe**: benzile sunt atunci aceleași peste tot (implicit
  0–1, 1–5, 5–20 și peste 20 de peșteri pe km², stabilite de administrator). Se schimbă doar
  culorile, nu numărătorile. O imagine salvată păstrează alegerea. Dacă imaginea e prea largă
  chiar și pentru celula cea mai mare, rândul o spune: mărește harta.
- **Indice de carstificare (zone)** colorează fiecare zonă carstică și masiv după clasa
  indicelui, de la *foarte scăzut* la *foarte ridicat*; o zonă care nu poate fi măsurată este gri,
  cu margine întreruptă. O zonă carstică din interiorul unui masiv se desenează peste el. Indică
  o zonă pentru numele și clasa ei; un clic o deschide, iar pagina ei are statisticile complete.
  Se desenează doar zonele pe care le poți vedea exact; dacă o imagine cuprinde mai multe decât
  desenează instalarea deodată, legenda spune că unele nu sunt arătate.

Amândouă au opacitate și se păstrează într-o [vedere salvată](saved-views-and-windows.md).
Niciunul nu se desenează în vederea 3D sau pe linkul unei vederi partajate.

### Rărirea etichetelor

**Rărește etichetele aglomerate** face ca numele care s-ar suprapune să se dea unele altora la o
parte. Marcajele se desenează întotdeauna — doar *etichetele* lor concurează pentru spațiu. Util
într-o zonă carstică densă, înșelător dacă uiți că l-ai pornit.

### Detaliul drumuirilor

Drumuirile sunt stratul scump, așa că au controale proprii:

- **Detaliu de la nivelul** — de la ce nivel apare detaliul topografic complet (inclusiv
  radialele). Sub el vezi conturul galeriilor, iar vizările de perete apar pe măsură ce
  apropii.
- **Limită de linii** — o limită a numărului de trasee desenate.

Lasă-le goale ca să urmezi valorile implicite ale instalării. Detaliul mai mare costă mai mult
la desenare. Dacă se rețin drumuiri, ți se spune: *„n drumuiri nu sunt afișate la această
scară — apropie harta pentru a le încărca."* Cu panoul de straturi închis, același număr apare
în colțul din stânga jos al hărții — *„n drumuiri ascunse la această scară — apropie harta sau
deschide panoul de straturi"* — iar apăsarea lui deschide panoul la aceste controale.

Amândouă se păstrează doar în acest browser. Aceleași două numere se află la
[Setări › Avansat](account-and-settings.md#drumuirile-de-pe-hartă), unde se spune și cât de
sus lasă instalarea să urce limita de linii. Ambele locuri primesc același interval, așa că un
număr pus într-unul nu este niciodată în afara intervalului în celălalt.

---

## Selectarea lucrurilor

**Clic** pe un obiect ca să-l selectezi. **Ctrl sau Shift + clic** adaugă la selecție.

**Panoul de detalii** din dreapta are două file:

- **Selecție** — ce ai selectat. Cu mai multe selectate arată câte sunt, care sunt lizibile aici
  și dacă selecția este mixtă.
- **În vedere** — tot ce este încărcat în zona vizibilă.

Acțiuni: **Zoom la selecție**, **Încadrează-le pe toate**, **Golește selecția**.
**Etichetează-le pe toate** apare, dar nu este încă disponibil.

O selecție poate fi umplută și dintr-o listă: **Selectează pe hartă tot ce s-a găsit (n)** din
registrul peșterilor sau din lista de obiecte pune în ea până la 200 dintre rezultatele listei.
Vezi [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă).

În fila **În vedere**, numele din fiecare rând este un buton: **Tab** ajunge la el și **Enter**
îl selectează, ca un clic pe rând.

Panoul însuși este configurabil. Secțiunile lui — Detalii, Etichete, Fișiere, Legături, Istoric,
Permisiuni, Text — pot fi **reordonate** (prin tragere sau cu Alt + săgeți), **ascunse**
individual și readuse. Poți salva **aranjări** denumite, poți stabili **spațierea** și
poți alege dacă panoul **împinge harta lateral** sau **plutește peste hartă**.

Există butoane **înapoi și înainte** pentru deplasarea prin istoricul selecției.

## Grupări

Sub un nivel de zoom ales de instalarea ta, intrările se grupează. Un clic pe o grupare
listează intrările din ea — *„n intrări în această zonă"* — cu **Apropie aici**; o intrare din
listă a cărei poziție nu o poți vedea exact este marcată *aprox.*, iar alegerea uneia o
selectează și mută harta la ea. Cât timp harta este grupată, fila **În vedere** spune *„Apropie
harta pentru a lista intrările individuale."*, în loc să dea o listă înșelătoare.

## Poziții aproximative

O înregistrare a cărei poziție exactă nu ai voie s-o vezi este desenată aproximativ și
etichetată: **„Locație aproximativă — coordonatele exacte sunt protejate"**, sau, pe hartă, forma
scurtă *aprox.* Un obiect a cărui geometrie este reținută complet spune **„Locație
nedivulgată"**.

Aceasta este [protecția locațiilor](../admin/location-protection.md). Aplicația nu ascunde că
înregistrarea există; ascunde unde este.

---

## Desenare și editare

Bara de editare:

| Unealtă | Face |
|---|---|
| **Desenează** | Plasează o geometrie nouă de tipul de obiect ales |
| **Modifică** | Trage vârfuri. Alt+clic șterge unul |
| **Mută geometria** | Translatează toată forma |
| **Aliniere la obiectele vizibile** | Vârfurile se aliniază la ce este desenat |
| **Anulează / Refă** | Istoric complet al sesiunii de editare |
| **Renunță la editări** | Aruncă sesiunea |

În timpul desenării: clic pentru a plasa puncte, dublu-clic pentru a termina o linie sau o zonă.
Bara arată **Încheie** și **Punct** (scoate ultimul punct). Pe ecran tactil, atinge întâi un
vârf și apoi **Șterge vârful**.

**Alege un tip de obiect** înainte de a desena. Tipurile sunt grupate în **Puncte, Linii,
Suprafețe** și *Altele*, iar cele folosite des pot fi **fixate** pe bară.

Există unelte dedicate pentru **Peșteră nouă aici** și **Intrare nouă aici** — clic pe hartă și
formularul se deschide cu coordonatele completate. Formularul de intrare întreabă de care peșteră
aparține, tipul ei și dacă să se **protejeze locația exactă**.

Lucrul nesalvat este numărat în bară: *„n modificări nesalvate"*.

### Măsurare

**Măsoară distanța** și **Măsoară aria**. Măsurătorile sunt geodezice — țin cont de
curbura Pământului, nu măsoară pe proiecția plană.

---

## Meniul contextual

Clic dreapta oriunde pe hartă:

- **Adaugă obiect**
- **Peșteră nouă aici**
- **Intrare nouă aici**
- **Copiază coordonatele**

---

## Alte controale ale hărții

| Control | Face |
|---|---|
| **Arată locația mea** | Folosește geolocalizarea browserului |
| **Compară rasterul cu harta de dedesubt (glisor)** | Un separator glisant pentru a compara un raster suprapus cu harta de bază |
| **Ascunde barele de instrumente ale hărții** | Eliberează ecranul pentru o captură sau un ecran mic |
| **Arată scena 3D lângă hartă** | Împarte spațiul de lucru — vezi [Vizualizarea 3D](3d-view.md) |
| **Cea mai mică distanță** | Măsoară cât de aproape ajung două peșteri — vezi [Măsurători](measurements-and-statistics.md#cea-mai-mică-distanță) |
| **Listează peșterile din vedere** | Deschide registrul peșterilor pe filtrul hărții, restrâns la ce arată harta — vezi [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă) |

## Căutarea

Caseta de căutare a hărții acoperă **obiecte, ture și locuri**: înregistrările tale,
plus nume de locuri de la un serviciu extern de geocodare (Nominatim). Rezultatele sunt grupate —
Peșteri, Obiecte, Jurnale de tură, Tabere, Locuri și potriviri din interiorul documentelor.
Fiecare oferă **Centrează** sau **Deschide**.

Lângă ea, un selector (*Mergi la…*) găsește după nume o peșteră, o intrare, un obiect, un
document, o tură, o vedere salvată, o fotografie sau un filtru salvat și te duce la el; un filtru
salvat ales acolo devine filtrul hărții. Sub ele stau filtrele rapide și **Listează peșterile din
vedere**.

Vezi [Căutare și filtre](search-and-filters.md) și

[Filtre pe hartă](search-and-filters.md#filtre-pe-hartă).

## Panouri și aranjări

Spațiul de lucru este un rând de panouri în jurul hărții: **panoul de straturi** în stânga,
**panoul de detalii** în dreapta și — când le deschizi — **scena 3D** și **modelul topo** al unei
peșteri lângă hartă. Trage de separatoarele subțiri ca să redimensionezi oricare dintre ele, sau
du tastatura pe un separator și folosește săgețile.

**Mutarea unui panou.** Scena 3D și modelul topo au fiecare o bandă sus cu un buton **Mută**: pune
panoul **în stânga hărții**, **în dreapta hărții**, **sub hartă** sau **într-o fereastră
separată** (util pentru un al doilea monitor; panoul se închide aici când se deschide fereastra). O
mutare nu repornește niciodată panoul — scena își păstrează camera, iar modelul vederea.

**Tabelul de sub hartă.** **Tabel sub hartă** (în meniul spațiului de lucru) deschide sub hartă o
bandă cu două file: **În vedere** — intrările și obiectele pe care le arată harta, actualizate când
o miști — și **Alese** — obiectele adunate cu Ctrl- sau Shift-clic. Un clic pe un rând îl selectează
și îl încadrează pe hartă. Cât timp tabelul e deschis, lista *În vedere* stă acolo în loc de panoul de
detalii. Trage de separatorul de deasupra benzii ca s-o faci mai înaltă sau mai joasă.

**Inversează panourile laterale** pune panoul de straturi în dreapta și panoul de detalii în stânga
(și înapoi). Tastele **[** și **]** și butoanele de pe margini urmează părțile: **[** pliază mereu ce
este în stânga.

**Extinderea pe tot spațiul de lucru.** Apasă **M** ca panoul în care lucrezi să ocupe tot spațiul
de lucru și din nou **M** ca toate panourile să revină la mărimea lor. Când tastatura nu e în niciun
panou anume, **M** extinde harta. Scena 3D, modelul topo, tabelul și harta au și un buton pentru
asta, iar un panou de straturi sau de detalii extins arată în colț un buton **Readu**.

**Aranjări.** Meniul spațiului de lucru (butonul cu patru pătrățele de lângă celelalte butoane ale
hărții) oferă aranjări gata făcute, cu o singură apăsare:

| Aranjare | Ce obții |
|---|---|
| **Hartă** | Harta între cele două panouri, nimic altceva — spațiul de lucru ca la livrare |
| **Hartă + 3D** | Scena 3D lângă hartă, în dreapta |
| **Verificarea topografiilor** | Un panou pentru modelul topo lângă hartă; alege o peșteră și deschide acolo unul dintre modelele ei. Panoul de straturi se pliază ca să-i facă loc |
| **Planificarea turei** | Sub hartă, tabelul cu ce e în vedere și scena 3D cu relieful pe care îl traversează apropierea |
| **Două ecrane** | Harta și panourile ei aici, scena 3D într-o fereastră separată |

Aranjarea în vigoare e bifată. **Salvează aranjarea curentă…** păstrează sub un nume panourile,
panourile din jurul hărții și dacă scena 3D e deschisă; aranjările salvate apar în același meniu (și,
ca până acum, în meniul *Aranjări salvate* al panoului de detalii) și îți urmează contul. Aranjările
salvate înainte de aceasta aduc înapoi, ca înainte, secțiunile panourilor.

Aranjarea îți urmează contul în orice browser — mai puțin faptul că scena 3D e deschisă: e un
afișaj greu, așa că se deschide când o ceri (butonul ei sau o aranjare care o include), niciodată de
la sine la încărcarea paginii. Un administrator poate alege cu ce aranjare pornește un cont **nou**
(*Administrare → Mesagerie*, fila *Spațiul de lucru*); se aplică o singură dată, prima dată când acel cont deschide harta.

## De la tastatură

Primul **Tab** pe pagina hărții oferă **Sari la hartă**, care duce tastatura chiar pe hartă,
dincolo de panoul de straturi; panoul rămâne la un **Shift+Tab** în urma ei. Orice altă pagină
oferă în același loc *Sari la conținutul principal*.

Pe pagina hărții, **?** listează toate scurtăturile. **[** și **]** pliază și depliază panourile din
stânga și din dreapta, **Alt+←** și **Alt+→** te duc înapoi și înainte prin ce ai selectat,
**Escape** golește selecția, iar **M** extinde pe tot spațiul de lucru panoul în care te afli (și îl
readuce) — vezi [Panouri și aranjări](#panouri-și-aranjări).

---

## Pe telefon

Spațiul de lucru se adaptează la atingere, inclusiv editarea completă a geometriei cu degetul.
Panoul se pliază; **Arată panoul** / **Ascunde panoul** îl comută. Aplicația se poate și instala
pe ecranul de pornire.

Telefonul arată un singur panou, harta, cu cele două panouri laterale ca sertare: aranjările, tabelul
de sub hartă și scena 3D de lângă ea sunt aranjări pentru calculator. Lista *În vedere* rămâne în
sertarul de detalii.

---

Înrudite: [Vederi salvate, panouri și ferestre multiple](saved-views-and-windows.md) ·
[Fenomene de suprafață](surface-features.md) ·
[Hărți raster georeferențiate](georeferenced-maps.md)
