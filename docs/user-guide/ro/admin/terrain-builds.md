# Generarea reliefului

🇬🇧 [English](../../admin/terrain-builds.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Relief](../features/terrain.md) ·
[Vizualizarea 3D](../features/3d-view.md)

---

**Administrare → Relief.** Transformarea datelor de elevație în terenul pe care îl desenează
[scena 3D](../features/3d-view.md).

> *„Trasează un dreptunghi, spune de unde vin datele de altitudine, iar instalarea generează
> terenul pe care îl desenează scena 3D."*

Complet opțional. Fără el globul este o sferă netedă, ceea ce nu are nevoie de niciun server de
elevație și nu descarcă nimic.

---

## Înainte de a începe: serviciul suplimentar

**Transformarea rasterelor în dale are nevoie de un serviciu propriu, pe care o instalare
obișnuită nu îl rulează.**

Obținerea datelor de elevație și pregătirea lor funcționează ca atare. Generarea dalelor nu. Dacă
instalarea ta nu rulează acel serviciu, o generare **se oprește la pasul de generare**,
iar pagina Relief îți arată exact ce să faci — singurul rând de adăugat în fișierul de mediu de
lângă fișierele de instalare, și comanda de rulat din acel dosar.

Observă și ce mai spune: tot ce a fost înainte de acel pas **s-a întâmplat totuși**, iar ce a
obținut și a pregătit generarea este încă pe disc sub ea. După ce serviciul rulează, apasă
**Rulează din nou** pe generarea oprită: continuă de la generarea dalelor, fără să mai obțină sau să
mai pregătească nimic a doua oară. Nimic nu o repune singur la coadă — doar un om știe că serviciul
este acum acolo.

---

## Realizarea unei generări

### 1. Desenează dreptunghiul

**Trasează dreptunghi**, trage-l pe hartă. Raportează extinderea (vest, sud, est, nord) și
suprafața în grade pătrate.

Există un maxim. Un dreptunghi mai mare decât generează instalarea dintr-o dată este refuzat —
generează-l ca mai multe zone mai mici.

### 2. Spune de unde vin datele de elevație

Trei surse, care se pot combina:

**Obține acoperirea pentru acest dreptunghi.** Date de elevație de treizeci de metri care acoperă
toată suprafața uscatului, aduse de aplicație. *„Marea nu întoarce nimic, ceea ce este un
răspuns normal, nu o eroare."*

**Rastre de pe acest calculator.** Lasă rastere de elevație înăuntru, până la 512 MB fiecare.
Orice mai mare aparține unui director de pe server. Fiecare fișier este trimis în clipa în care îl
alegi, iar formularul nu te lasă să pornești o generare cât timp încărcările sunt încă în drum —
numește fișierele care nu au sosit încă. Un raster trimis și apoi lăsat în afara oricărei generări
este șters după o zi (instalarea poate stabili alt interval); unul pe care îl numește o generare —
la coadă, în curs, încheiată sau oprită — se păstrează cât timp există acea generare.

**Director pe server.** Unul pe care operatorul l-a listat ca lizibil, sau unul de sub el. Se
citește tot ce din el arată a raster de elevație. Dacă nu este configurat niciunul, o spune. Doar un
**Administrator deplin** poate numi unul.

Rasterele dintr-un director de pe server sunt **citite acolo unde sunt, niciodată copiate și
niciodată scrise** — o măsurătoare națională aflată deja pe mașină nu merită ținută de două ori, iar
nimic din ce face generarea, nici chiar ștergerea ei, nu atinge acel director. Generarea păstrează
în schimb o listă a ce a găsit acolo prima dată. Dacă este rulată din nou mai târziu și între timp
un raster a fost adăugat, scos sau schimbat, se oprește în loc să continue peste rastere din care nu
a fost făcută: pune directorul înapoi cum era și ruleaz-o din nou, sau pornește o generare nouă,
care citește directorul așa cum este acum.

### 3. Sursa datelor

**Cui aparțin datele.**

> *„Se scrie în suprafața publicată, așa că o generare nu trebuie să poarte niciodată o mențiune
> care nu îi aparține."*

Obligatoriu dacă ai furnizat chiar tu rastere. Înregistrează și **licența**.

### 4. Adâncimea

Cât de fin merge piramida de dale. Formularul descrie fiecare bandă, ca să nu ghicești:

| Bandă | |
|---|---|
| **Grosieră** | O formă largă a peisajului. Rapidă, mică pe disc, dar un versant se citește ca o singură pantă |
| **Acoperire** (≈ nivelul 13) | Atâta detaliu cât conține efectiv acoperirea de treizeci de metri. **Valoarea de lucru implicită** |
| **Fină** | Mai fină decât acoperirea obținută. Merită cerută doar acolo unde rastere de câțiva metri acoperă dreptunghiul |
| **Topografică** | Ce merită date topografice de jumătate de metru |

> **Fiecare nivel aproximativ împătrește și dalele scrise, și timpul necesar.**

Dacă ceri mai mult decât conțin sursele, formularul spune **„Nimic de aici nu poate umple acele
niveluri"** — nivelurile mai adânci nu sunt nici refuzate, nici eliminate tacit; pur și simplu
adaugă spațiu pe disc fără detaliu.

**Ce ai obținut se raportează după aceea.** Generatorul de dale se oprește acolo unde sursele
rămân fără detaliu, indiferent ce adâncime ai cerut — acoperirea de treizeci de metri se termină
în jurul nivelului 13 chiar dacă ai cerut 16. După ce o generare a fost verificată, coloana
**Adâncime** din lista Generări arată cel mai adânc nivel pe care îl ating cu adevărat dalele, iar
când acesta este mai mic decât cel cerut, cererea apare alături (*13 (cerut: 16)*). O generare
care nu a fost încă verificată arată adâncimea cerută.

### 5. Înălțimile

**Altitudinile sunt măsurate de la:** *Deasupra nivelului mării* (ortometrice) sau *Deasupra
elipsoidului*.

> **O alegere greșită ridică sau coboară întreaga suprafață cu zeci de metri.**

Pentru înălțimi elipsoidale, dă **înălțimea geoidului** — cât de sus stă acolo suprafața locală
a nivelului mării față de elipsoid.

### 6. Pornirea

**Pornește generarea.** Se pune la coadă.

Lângă buton, o notă spune ce hotărăște cât de fin poate ieși rezultatul:

> *„Memoria hotărăște cât de fin poate fi relieful. Unealta care face dalele folosește jumătate din
> memoria propriului container — 8 GB, dacă instalarea nu setează SILEXGIS_TERRAIN_WORKER_MEMORY —
> iar când nu îi ajunge nu eșuează: face un relief mai grosier decât s-a cerut. O generare care a
> rămas fără memorie se oprește și spune asta, iar setarea poate fi apoi mărită."*

---

## Adăugarea unui raster la o generare pe care o ai deja

O topografie nouă peste un teren deja copt nu trebuie să coste din nou toată regiunea. Când pornești
o generare, poți numi o **generare terminată de extins**. Noua generare pornește de la o copie a
dalelor acelei generări, utilitarul adaugă rasterele tale noi la copie fără a reface ce
există deja, iar rezultatul este verificat și publicat ca o generare de sine stătătoare.

**Cât costă.** Dalele rasterelor noi și o copie a piramidei de bază pe disc. Pentru o insulă fină
peste o regiune coaptă înseamnă minute, nu ore: prețul urmează ce adaugi, nu mărimea regiunii.

**Adâncimea trebuie să fie a bazei.** Adăugarea face dale numai pentru rasterele noi; nu poate
reface dalele existente la altă adâncime, așa că o extensie cerută la altă adâncime este refuzată,
nu făcută pe jumătate. Înălțimile se măsoară la fel ca ale bazei, din același motiv. Acoperirea nu
se mai obține încă o dată decât dacă o ceri — baza o are deja — așa că o extensie trebuie să
numească cel puțin un raster sau să ceară acoperire pentru teren nou.

**Baza se păstrează.** Rămâne în listă, desenabilă și neschimbată; extensia este o generare
separată, la o adresă separată, care poartă sursele și creditul bazei ca ale ei împreună cu ce ai
adăugat. Poți șterge baza după ce extensia s-a terminat — nu înainte, pentru că până atunci
extensia încă îi citește dalele.

**O bază trebuie să fie întreagă.** Numai o generare care și-a terminat verificarea și ale cărei
dale sunt încă acolo de unde se servește terenul poate fi extinsă; una care încă rulează, s-a oprit
înaintea verificării sau i s-au șters dalele este refuzată, iar mesajul spune care este cazul.

---

## Urmărirea unei generări

Stare: **La coadă · În curs · Încheiată · Eșuată** — iar pentru o generare pe care cineva a
oprit-o, **Se anulează** până se oprește și **Anulată** după aceea, nu un eșec roșu.

Faze, în ordine:

| Fază | |
|---|---|
| *Așteaptă să îi vină rândul* | Nimic nu a început |
| **Obținere** | Se aduce acoperirea |
| **Pregătire** | Se convertesc rasterele |
| **Generare** | Se fac dalele — pasul care are nevoie de serviciul suplimentar |
| **Verificare** | Se recitesc dalele și se verifică dacă sunt întregi |
| **Publicare** | |

Pagina de detaliu a unei generări arată din ce a fost făcută (**Făcută din**) și **Ce a spus
unealta** — jurnalul, unde o eroare se explică singură.

### Oprirea unei generări și rularea ei din nou

**Oprește**, pe o generare la coadă sau în curs, întreabă printr-o confirmare: *„Anulezi această
generare? Ce a obținut și a pregătit deja se păstrează, așa că poate fi rulată din nou mai târziu, de unde s-a oprit."* O
generare la coadă se oprește imediat. Una în curs se oprește la primul punct la care poate —
între două fișiere sau, cât timp se fac dalele, după ce unealta care face dalele a confirmat că
s-a oprit — și apare ca *Se anulează* până atunci. Dalele pe care le scrisese acea generare sunt
aruncate, ca nimic făcut pe jumătate să nu fie luat drept o suprafață terminată. O generare care a
început publicarea se termină; o generare oprită nu este publicată niciodată.

**Rulează din nou**, pe orice generare oprită — anulată sau oprită din alt motiv — o pune înapoi la
coadă și continuă de la primul pas a cărui muncă nu este deja pe disc: o generare oprită la
generarea dalelor face dale și nimic altceva. O generare a dalelor care a mers prost și-a aruncat
dalele, așa că se face din nou. **Rulează din nou** este refuzat, cu o propoziție care spune de ce,
cât timp o altă generare a aceluiași dreptunghi nu s-a terminat, pentru o generare care a extins o
generare ștearsă între timp și — pentru scurt timp — pentru o generare oprită în timp ce făcea dale,
a cărei unealtă nu a confirmat încă oprirea; răspunsul vine imediat ce serviciul de dale rulează.

Ambele au nevoie de același drept care pornește o generare: **execuție** asupra reliefului.

### Două limite pe care le poate atinge generarea dalelor

**Memoria.** Când unealta care face dalele rămâne fără memorie nu eșuează — face un relief mai
grosier decât adâncimea aleasă, lucru pe care nimic de pe hartă nu l-ar semnala vreodată. Aplicația
citește raportul uneltei după fiecare generare, așa că o asemenea generare nu este publicată: se
oprește cu *Unealta care face dalele a rămas fără memorie, așa că relieful ar fi ieșit mai grosier
decât s-a cerut*, dalele îi sunt aruncate, iar pagina dă singurul rând de adăugat în fișierul de
mediu și comanda care îl aplică. Apoi apasă **Rulează din nou**.

**Timpul.** O generare a dalelor poate rula douăsprezece ore, dacă instalarea nu permite mai mult.
Una care atinge limita — una care nu mai înainta, sau o zonă mare care avea pur și simplu nevoie de
mai mult — este oprită, dalele îi sunt aruncate, iar generarea spune cât a rulat și față de ce
limită: *Limita de timp a fost atinsă după 12 h (limita: 12 h)*, cu setarea care o mărește. Setarea
este în secunde; semnul întrebării de lângă ea — *Cât înseamnă asta în secunde?* — listează duratele
obișnuite:

| | Secunde |
|---|---|
| 1 oră | 3600 |
| 12 ore (valoarea implicită) | 43200 |
| 1 zi | 86400 |
| 2 zile | 172800 |
| 1 săptămână (cel mai lung permis) | 604800 |

Seteaz-o, repornește aplicația și apasă **Rulează din nou**; ce a obținut și a pregătit generarea
se păstrează.

---

## Administrarea generărilor

Lista **Generări** arată starea, care **Se desenează**, suprafața, adâncimea, când a fost
trimisă și mărimea **Pe disc** — *„tot ce a lăsat în urmă generarea pe disc: dalele publicate și
rasterele convertite păstrate lângă ele."* Sub această cifră lista arată **doar dalele**
(*1,1 MB de dale*), singurul lucru servit unui vizualizator; diferența este ce costă rasterele
convertite și fișierele de lucru, păstrate ca generarea să poată fi rulată din nou. Ștergerea
generării eliberează întreaga mărime de pe disc, dalele incluse. Generările verificate înainte ca
mărimea doar a dalelor să fie înregistrată o arată doar pe cea mare.

| Acțiune | |
|---|---|
| **Desenează aceasta** | Scena desenează acum această generare |
| **Nu mai desena** | Scena desenează din nou teren gol |
| **Oprește** | Oprește o generare la coadă sau în curs, păstrând ce a obținut și a pregătit |
| **Rulează din nou** | Pune o generare oprită înapoi la coadă, continuând de unde s-a oprit |
| **Șterge** | Elimină generarea și spațiul pe care îl ține |

Două refuzuri de reținut:

- **Nu poți șterge generarea pe care o desenează scena.** Alege alt relief sau oprește
  desenarea ei mai întâi.
- **Nu poți șterge o generare în curs.** Fișierele ei se scriu chiar acum; poate fi eliminată
  după ce se oprește, indiferent cum se oprește — **Oprește** o oprește.

O stare de reținut, pentru că nimic nu o refuză: **dacă configurația instalării numește o adresă
de relief proprie** (`SILEXGIS__Terrain__Url`), acea adresă are prioritate față de orice generare
aleasă aici. Generarea este înregistrată ca fiind desenată, iar scena desenează în schimb piramida
configurată. Pagina Relief arată atunci un avertisment deasupra listei, care numește generarea
suprascrisă și setarea; generarea aleasă se desenează după ce setarea este scoasă și aplicația
repornită.

**Pagina avertizează și atunci când relieful desenat nu poartă nicio mențiune a sursei.** Datele
de altitudine trebuie aproape întotdeauna creditate — Copernicus și majoritatea celorlalte modele
gratuite o cer — iar scena arată mențiunea pe care o poartă relieful desenat, deci un relief fără
niciuna se desenează fără nimic alături și nimic nu refuză acest lucru. Pentru o adresă de relief
numită în configurație, avertismentul numește setarea care precizează mențiunea
(`SILEXGIS__Terrain__Attribution`); adaug-o și repornește. Pentru o generare, mențiunea vine de
la sursele din care a fost făcută, fiecare declarând una atunci când se pornește generarea:
aplicația refuză o sursă fără mențiune, deci o generare făcută aici are întotdeauna una, iar
avertismentul apare doar pentru o generare înregistrată pe altă cale. Mențiunea nu poate fi
adăugată unei generări încheiate — pornește una peste același teren, cu sursele creditate, și
alege-o pe aceea.

---

## Imagini derivate ale terenului

Altitudinile unei generări încheiate pot fi transformate și în **imagini ale terenului** — relief
umbrit, pantă, orientare, asperitate, poziție topografică, rugozitate, un relief colorat sau curbe
de nivel. Sunt calculate o singură dată de instalare, pe același lucrător care face generările, iar
apoi oricine are dreptul de a citi terenul le vede în lista de straturi a hărții, la **Imagini ale
terenului**: activează una și se desenează sub peșteri.

### Cum se cere una

Cardul **Imagini derivate** de pe pagina Relief, sub generări. Cererea are nevoie de dreptul de
**execuție** asupra reliefului — același drept care pornește o generare. Fără el, cardul se vede în
continuare, dezactivat, cu un rând care spune ce drept lipsește; registrul de sub el rămâne de
citit.

| Câmp | |
|---|---|
| **Generare** | Din ce generare încheiată se desenează. Se oferă doar generările care au mers până la capăt, iar formularul se deschide pe cea pe care o desenează scena — o imagine a oricărui alt teren ar fi depășită din clipa în care s-ar termina |
| **Imagine** | Ce fel. Fiecare fel arată doar setările pe care le citește |
| **Nume** | Cum o numește lista de straturi. Urmează felul imaginii până scrii unul al tău |

Setările, pe feluri:

- **Relief umbrit**: o lumină sau patru. Sub o singură lumină, orice versant întors de la ea este
  negru complet, iar o dolină din umbra aceea este invizibilă; patru lumini o păstrează lizibilă.
  Pentru o lumină, de unde vine — în sensul acelor de ceasornic de la nord, cu nord-vestul drept
  convenție — și cât de sus stă. **Exagerarea înălțimilor** înmulțește mai întâi fiecare înălțime;
  dincolo de câteva ori, un peisaj blând devine un perete de pixeli saturați, iar formularul refuză
  peste o sută.
- **Pantă** și **orientare**: calculul pantei (Horn, pentru altitudini reale, cu zgomot, sau
  Zevenbergen–Thorne, mai clar pe suprafețe curate) și, pentru pantă, grade sau procente. O pantă desenată în procente se colorează între 0 și 200
la sută (100 la sută este o pantă de 45 de grade; terenul mai abrupt se desenează la capătul
rampei), iar una desenată în grade, între 0 și 90.
- **Asperitate**: definiția lui Riley, față de care sunt enunțate clasele de asperitate publicate,
  sau a lui Wilson, care dă alte numere — cele două nu sunt niciodată comparabile.
- **Relief colorat**: o culoare la fiecare înălțime, în metri. Cel puțin două trepte, niciodată
  două la aceeași înălțime; terenul dintre două înălțimi se pictează cu amestecul lor.
- **Curbe de nivel**: câți metri de înălțime sunt între o curbă și următoarea — de la 1 la 1000,
  și 20 dacă nu spui altfel — numărați de la nivelul mării, fiecare a cincea curbă fiind
  desenată mai gros. Curbele sunt o imagine ca toate celelalte, transparentă între linii, și nu
  date: de-a lungul lor nu se poate măsura, nu pot fi selectate sau exportate și nicio curbă nu
  își poartă înălțimea. Imaginea este desenată de patru ori mai fin decât altitudinile (cam șapte
  metri pe punct din date de o secundă de arc), așa că liniile rămân netede până când harta este
  apropiată dincolo de circa 1:4.000; mai aproape de atât își arată treptele. O imagine calculată
  înainte de 2026-10-21 a fost desenată de două ori mai fin — cere-o din nou pentru cea mai
  netedă.
- Orice fel în afară de curbele de nivel: dacă celulele de la margine se calculează din vecinii pe
  care îi au. Oprit, fiecare rastru al generării se întâlnește cu vecinul lui printr-o linie goală
  prin imagine.

**Cere imaginea** o pune la coadă. Cererea din nou a unei imagini pe care instalarea o are deja —
aceeași generare, același fel, aceleași setări — răspunde cu cea existentă în loc să o calculeze de
două ori.

### Cum se calculează panta și orientarea

Ambele sunt măsurate **pe teren, nu pe grila în grade în care sunt ținute altitudinile**: un grad
de longitudine este mai îngust decât un grad de latitudine — cu aproape o treime la latitudinile
Carpaților — așa că fiecare rând al unui rastru se calculează cu cele două distanțe pe care le
acoperă cu adevărat un grad la latitudinea acelui rând, iar un versant orientat spre est iese la
fel de abrupt ca același versant orientat spre nord. Nimic nu este reproiectat sau reeșantionat pe
drum, deci imaginea stă exact peste altitudinile din care a fost desenată, iar relieful umbrit
este luminat folosind aceleași două distanțe.

### Registrul

**Registrul imaginilor derivate** listează fiecare imagine pe care o are instalarea: numele și
felul, **parametrii** cu care a fost calculată (doar cei pe care felul ei îi citește), **generarea**
din care vine, **starea** (*La coadă · În calcul · Gata · Eșuată* — un eșec se explică singur, în
cuvintele lucrătorului), cât ține **pe disc** și când a fost cerută. Lista se recitește singură cât
timp ceva este încă în calcul.

Fiecare imagine aparține generării din care a fost desenată. **Când activezi o altă generare, orice
imagine desenată din cea veche este marcată „Depășită"** — în registru și lângă numele ei în lista
de straturi. Rămâne acolo și se desenează în continuare — un relief umbrit depășit este adesea mai
bun decât niciunul — dar se vede dintr-o privire că terenul de sub ea a fost înlocuit, ceea ce
altfel ar părea o problemă a datelor despre peșteri.

O imagine este marcată la fel și atunci când **modul în care se calculează felul ei a fost între
timp corectat**: relieful umbrit, panta și orientarea calculate înainte de a fi măsurate pe teren
sunt în această situație. Indiciul de sub etichetă spune care dintre cele două motive este. Pentru
acesta, soluția este să **ceri din nou aceeași imagine** — aceeași generare, același fel,
aceleași setări — ceea ce o calculează de la capăt, în locul celei vechi.

**Șterge**, în spatele unei confirmări care spune ce eliberează, elimină imaginea și rasterele ei.
Are nevoie de dreptul de **ștergere** asupra reliefului. Fișierele stau sub generarea însăși, deci
**ștergerea unei generări îi șterge și imaginile**.

**Unele lucruri nu pot fi calculate aici și nu sunt oferite sub un nume împrumutat.** Geomorfonii,
curbura și direcția scurgerii, acumularea scurgerii și indicele de umiditate nu sunt produse de
biblioteca de altitudini folosită de această instalare, deci lipsesc în loc să fie aproximate prin
ceva care le seamănă.

---

## Probleme frecvente

| Mesaj | Înseamnă |
|---|---|
| *Zona respectivă este deja în lucru.* | Așteapt-o |
| *O generare trebuie făcută din ceva* | Obține acoperirea sau furnizează rastere |
| *Fișierul nu este un rastru de altitudine pe care această instalare îl citește.* | Format greșit |
| *O dală de altitudine spune unde se află doar prin numele ei* | Un `.hgt` trebuie numit ca `N45E024.hgt`, pentru pătratul al cărui colț este 45°N 24°E |
| *Dreptunghiul acela nu este un dreptunghi pe Pământ.* | Trasează-l din nou |
| *Generarea nu are nicio suprafață de desenat.* | Dalele ei nu au fost recitite și găsite întregi, sau nu mai sunt de unde se servește relieful |
| *Obținerea celulei de altitudine … a eșuat: conexiunea … nu a putut fi făcută sau s-a pierdut* (sau *nu a terminat de răspuns în timpul îngăduit*) | Serverul nu a putut ajunge la datele de altitudine, ori legătura a căzut sau s-a blocat. Verifică accesul spre exterior, apoi **Rulează din nou**; celulele deja obținute se păstrează |
| *O extensie se construiește la adâncimea generării pe care o extinde* | Cere nivelul bazei sau pornește o generare nouă |
| *O altă generare care o extinde pe aceasta nu s-a terminat și încă îi citește dalele* | Așteaptă ca acea generare să se oprească, indiferent cum, înainte s-o ștergi pe aceasta |
| *Generarea s-a oprit deja, așa că nu mai este nimic de anulat* | S-a încheiat sau a eșuat singură, mai înainte |
| *Doar o generare oprită poate fi rulată din nou* | Este încă la coadă sau în curs, ori s-a încheiat |
| *Generarea a fost anulată în timp ce i se făceau dalele, iar unealta care face dalele nu a confirmat încă oprirea* | Pornește serviciul de dale; răspunde imediat, iar apoi generarea poate fi rulată din nou |

---

## Din linia de comandă

Totul se poate face și dintr-un singur pas documentat în linia de comandă, fără pagina Relief a
aplicației. Vezi documentația de instalare.


---

Înrudite: [Relief](../features/terrain.md) · [Vizualizarea 3D](../features/3d-view.md)
