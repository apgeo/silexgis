# Topografii, drumuiri și modele 3D

🇬🇧 [English](../../features/surveys-and-models.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
[Măsurători și statistici](measurements-and-statistics.md)

---

SilexGIS afișează topografii; nu le compilează. Aduci ce produc Therion sau Survex, iar
aplicația le desenează, le măsoară, le proiectează pe hartă și păstrează sursele din care le-ai
făcut.

Tot ce urmează trăiește pe **pagina unei peșteri**.

---

## Modele topo 3D

**Încarcă model.** Acceptate: **Therion `.lox`**, **Survex `.3d`**, sau **pereți de peșteră ca
`.stl` binar** — până la 100 MB.

Stare: *Așteaptă la rând → În curs → Gata*, sau *Nu a putut fi procesat*.

Un model este desenat în **vizualizatorul topografic** (CaveView.js) și, pentru pereți, în
[scena 3D](3d-view.md). Pereții sunt fie un `.stl` pe care îl încarci, fie — acolo unde
topografia însăși conține destul ca să fie construiți —
[făcuți din `.lox` sau `.3d`](#pereți-dintr-un-lox-sau-3d).

**Vizualizatorul se așază după mărimea la care este arătat**, adică după panoul în care stă, nu
după ecranul tău. Într-un panou mai îngust de aproximativ 1000 de pixeli, busola și
cadranul de înclinare sunt desenate la jumătate din mărime, iar pe telefon la un sfert, ca să nu
stea peste peștera pe care trebuie să o orienteze. **Fotografiile unei stații** stau într-o
coloană la marginea stângă a modelului, la jumătate din mărimea pe care o aveau lângă stație,
unde nu acoperă nicio galerie; descrierea unei fotografii apare când o indici și în
vizualizatorul în care se deschide. Când mai mulți oameni dintr-o tură urmărită sunt la stații
apropiate pe ecran, etichetele cu numele lor sunt puse una sub alta, nu una peste alta.

**Locul în care au fost făcute fotografiile este marcat peste model.** O fotografie nu are în
subteran o poziție proprie — într-o peșteră nu există semnal de satelit — așa că locul ei este
stația de care este legată: leagă o fotografie de o stație (clic pe stație, apoi **Leagă asta…**)
și deasupra acelei stații apare un mic aparat foto, cu numărul de fotografii acolo unde sunt mai
multe. Apasă aparatul pentru a arăta fotografiile acelei stații; apasă una dintre ele pentru
a o deschide. O fotografie legată de o galerie sau de o topografie întreagă, nu de o stație, nu
este marcată, pentru că nu are un singur loc.

**Mărimea numelor de stații o alegi tu.** Împreună cu bara de unelte a
vizualizatorului apare, în colțul din dreapta sus al modelului, un mic control **Aa**: 40 %, 60 %,
80 %, 100 %, 125 % sau 150 % din mărimea vizualizatorului. O topografie numește fiecare stație cu
toată calea ei, iar o galerie aglomerată la mărime întreagă este un zid de text; micșoreaz-o până
nu mai este. Alegerea se păstrează în browserul tău și se aplică oricărui model deschis.

### Modelul curent

O peșteră păstrează fiecare model încărcat vreodată — încărcările nu se suprascriu, așa că un
export corectat este un al doilea model lângă primul. Unul dintre ele poartă marcajul **Curent**,
pe fel: **modelul de drumuire** curent (`.lox` / `.3d`) este cel citit de drumuirea extrasă pe hartă și
de [măsurători](measurements-and-statistics.md); **pereții** curenți (`.stl`) sunt cei desenați de
scena 3D — iar acolo unde o peșteră nu are un `.stl` curent, scena desenează în locul lor pereții
construiți din modelul ei de drumuire curent. **Primul** model de un fel este curent și rămâne așa până
alegi altul — o a doua încărcare poate fi un export corectat sau topografia unei singure galerii
laterale, și numai tu știi care — așa că folosește **Setează ca model curent** pe
modelul care trebuie să preia, după ce s-a terminat procesarea. Pentru un model de drumuire, alegerea
face și din drumuirea lui forma peșterii pe hartă, iar setarea drumuirii unei topografii ca
implicită face același lucru din cealaltă parte: harta și cifrele descriu întotdeauna aceeași
topografie. Ștergerea modelului curent trece marcajul la cel mai nou rămas.

**Alegerea lui la încărcare.** Când peștera are deja un model curent de felul pe care îl încarci,
dialogul de încărcare oferă să facă noul fișier cel curent **după ce a fost citit** (pentru pereți,
după ce au fost convertiți) — *Fă-l modelul de drumuire curent după ce a fost citit, în locul lui „…”*.
Până atunci modelul pe care îl are peștera rămâne curent — dintr-un fișier care nu a fost citit nu
se poate măsura și desena nimic — iar lista mută singură marcajul când citirea se încheie. Două
lucruri lasă marcajul unde este: un fișier care nu poate fi citit și cineva care alege alt model
cât timp al tău era citit, pentru că alegerea lui este cea mai recentă. Citirea din nou,
mai târziu, a unui model nu îl face din nou curent; pentru asta folosește **Setează ca model
curent**.

### Citirea unei topografii în rânduri

O topografie compilată nu este lăsată ca un fișier pe care îl desenează browserul. Este **citită
în stațiile și vizările ei**, ceea ce face posibile
[statisticile](measurements-and-statistics.md). Graficul poate fi privit în timp ce asta se
întâmplă; lista se actualizează singură când citirea s-a terminat.

Un model este citit o singură dată, la sosire. **Citește din nou**, pe rândul lui, cere ca fișierul
păstrat să fie citit a doua oară, iar ce a produs ultima citire — stațiile, vizările, drumuirea
extrasă, pereții construiți din grafic — este înlocuit cu ce produce aceasta. Nu se încarcă nimic,
modelul rămâne același model, iar turele urmărite pe el rămân pe el. Există pentru două situații:
o citire care **a eșuat** dintr-un motiv care între timp a dispărut și o **actualizare** care
citește topografiile mai bine decât versiunea care le-a citit pe ale tale. Trebuie să
poți modifica peștera; acțiunea nu este oferită cât timp o citire așteaptă sau este în curs. Un
administrator poate cere același lucru pentru toate modelele de drumuire deodată — vezi
[Operațiuni de întreținere](../admin/maintenance.md).

Un model care a fost citit rămâne **Gata** cât este citit din nou, cu **Se citește din nou** alături:
peștera își păstrează cifrele, iar o tură urmărită pe model își păstrează desenul, din citirea
anterioară, până când cea nouă o înlocuiește într-un singur pas. Dacă noua citire eșuează, modelul
păstrează citirea pe care o avea, iar cine a cerut-o este înștiințat de ce. Doar un model care nu
conține nimic — unul a cărui citire a eșuat — se întoarce la **Așteaptă la rând**. Pereții (`.stl`) pot fi
convertiți din nou în același fel, iar conversia anterioară este înlocuită.

**Stațiile cărora fișierul nu le dă nume.** O topografie compilată poate conține o stație pentru
care fișierul nu a scris niciun nume — doar un număr, locul pe care stația îl are în acel fișier.
Este o stație ca oricare alta și este citită ca oricare alta. Cum se numește urmează desenul, astfel
încât o stație apăsată pe model și aceeași stație dintre rânduri să poarte un singur nume:

| Topografie | O stație fără nume se numește | Pe desen |
| --- | --- | --- |
| Therion (`.lox`) | numărul ei între paranteze drepte — `[42]`, după partea de topografie din care face parte | Da, sub aceeași etichetă; poate fi apăsată și se poate raporta la ea |
| Survex (`.3d`) | numărul ei după un diez — `#42` | Nu; desenul lasă deoparte o stație fără etichetă, așa că numele există doar printre rânduri |

Aceasta nu este capătul îndepărtat al unei vizări trase la peretele galeriei, pe care o topografie
îl scrie `-` sau `.`: acela nu este deloc o stație și nu se păstrează nimic pentru el.

Din faptul că numărul este **al fișierului și nu al peșterii** — următorul export al topografiei
îl dă altei stații — decurg două lucruri:

- [Ce înseamnă o adâncime](live-tracking.md#ce-înseamnă-adâncimile-unei-peșteri) se declară pentru
  peșteră și trăiește mai mult decât orice model, așa că **o adâncime nu poate fi declarată la o
  stație fără nume**; iar o adâncime raportată nu este pusă niciodată pe o stație pe care desenul nu
  o poate arăta.
- Un model Therion citit de o versiune mai veche decât această denumire își ține stațiile fără nume
  sub altă scriere, pe care nicio apăsare pe desen nu o potrivește. **Citește din nou** îndreaptă
  asta; un raport refuzat din acest motiv o spune. O poziție deja înregistrată la o asemenea stație
  sub vechea scriere nu este rescrisă: rămâne în jurnalul turei sub numele cu care a fost
  înregistrată, căruia nu îi mai răspunde nicio stație, așa că nu este desenată. Corecteaz-o pe
  rândul ei din fila de urmărire a turei, dacă are importanță. Fiecare stație căreia fișierul îi dă
  nume își păstrează numele, iar pozițiile înregistrate la acelea rămân neatinse.

### Unde stă o topografie în lume

Aici greșesc oamenii, așa că formularele sunt explicite.

- **Un fișier `.lox` nu are câmp pentru un sistem de coordonate**, deci nu spune niciodată unde
  se află. Trebuie să spui ce înseamnă numerele lui, altfel topografia nu poate fi plasată.
- **Un fișier `.3d` își declară sistemul propriu** doar dacă topografia a fost compilată cu unul.
  Dacă îl declară, acela este folosit și ce spui tu este ignorat.

### Sistemele de coordonate vin din instalare, nu de pe internet

Când dai un cod EPSG — pentru un `.stl` sau pentru o topografie al cărei fișier nu-și declară
sistemul — definiția este rezolvată **din baza de date de sisteme de coordonate care vine cu
aplicația**, nu adusă de la un serviciu web public.

Rezultă două lucruri:

- **O instalare fără ieșire la internet georeferențiază corect topografiile.** Nimic nu face o
  cerere externă în momentul citirii unei topografii.
- **Grilele naționale funcționează.** Stereo70 (EPSG:31700) este cazul evident: nu este
  precodificat în vizualizator, iar pe o instalare care ar fi trebuit să aducă definiții și-ar
  pierde georeferențierea sau n-ar mai putea fi încărcat deloc. Este plasat cu transformarea
  românească în șapte parametri — aceeași pe care o folosesc harta și
  [formatele de coordonate](coordinates.md) — așa că o topografie în Stereo 70 stă în vizualizator
  acolo unde harta pune peștera, nu la vreo sută de metri alături.

Dacă ești obișnuit ca topografiile să-și piardă tăcut poziția pe un sistem găzduit propriu,
iată de ce aici nu se întâmplă.

### Precizie pierdută înainte de sosire

Dacă coordonatele dintr-un fișier au fost prea mari pentru numerele în care le stochează,
topografia ajunge mai grosieră decât a fost ridicată. Aplicația detectează asta și o spune: *„
Fișierul a pierdut precizie înainte să ajungă aici"*. Reexportarea în jurul unei origini locale o
recuperează.

### Compararea a două topografii

O peșteră topografiată de două ori păstrează ambele modele de drumuire, iar vizualizatorul topografic le
poate arăta împreună. Deschide unul dintre ele — **Vezi în 3D**, lângă hartă sau într-o fereastră
proprie — și, dacă peștera mai are un `.lox` sau `.3d`, deasupra modelului apare **Compară cu…**.
Alege cealaltă topografie și felul în care sunt arătate cele două. Poți trece de la un fel la
celălalt în timp ce compari, iar **Oprește compararea** te întoarce la topografia pe care ai
deschis-o.

- **Suprapuse** desenează ambele topografii într-o singură vedere, fiecare în culoarea ei —
  albastru pentru topografia deschisă, portocaliu pentru cea cu care este comparată — sub o
  legendă care spune care este care. O galerie retopografiată stă peste ea însăși, cea de dinainte,
  așa că iese în evidență, într-o singură culoare, ce s-a adăugat sau s-a mutat. Acolo unde cele
  două coincid exact se poate vedea o singură culoare, și anume portocaliul topografiei cu care
  compari, desenată peste cea deschisă: ascunde acea parte a ei ca să vezi albastrul de
  dedesubt.
- **Alăturate** dă fiecărei topografii o vedere proprie, sub numele ei — una deasupra celeilalte
  acolo unde nu este loc pe lățime. **Leagă vederile**, pornit de la început, face ca rotirea,
  înclinarea, apropierea sau mutarea unui model să facă același lucru cu celălalt; oprește-l ca să
  le privești pe fiecare separat.

**Arătarea și ascunderea părților.** Sub modele este, pentru fiecare topografie, o listă a
ridicărilor denumite din care este alcătuită, cu câte o bifă și cu **Arată tot**. Cu
**Sincronizare** pornită, bifarea sau debifarea unei părți face același lucru cu partea cu același
nume din cealaltă topografie; o parte pe care o are doar una dintre ele este arătată sau ascunsă
singură. Cu ea oprită, fiecare listă lucrează doar asupra topografiei ei. Merge la fel suprapuse
și alăturate, iar ce ai ascuns rămâne ascuns când treci de la un fel la celălalt. Lista este
primul nivel din structura topografiei care oferă o alegere — o topografie exportată dintr-o
singură bucată apare ca acea singură bucată.

**Două topografii în coordonate diferite pot fi comparate doar alăturate.** Vizualizatorul
desenează fiecare fișier acolo unde îl pun propriile lui numere. Două topografii măsurate față de
același punct fix, sau exportate în aceeași proiecție, cad una peste alta. Dacă una este în metri
față de un punct propriu, iar cealaltă într-o proiecție națională — sau fiecare față de alt punct —
sunt desenate la sute de kilometri una de alta, iar o asemenea imagine nu compară nimic. De aceea,
înainte să le suprapună, aplicația verifică dacă cele două se află în același loc; dacă nu, o
spune și le oferă alăturate, unde fiecare topografie este încadrată separat și nimic nu trebuie să
se potrivească. Verificarea nu poate vedea o deplasare mică: două topografii măsurate față de
puncte aflate la câțiva metri unul de altul sunt suprapuse și sunt decalate cu acei câțiva metri.
Pentru o suprapunere în care să ai încredere, exportă-le pe amândouă față de același punct fix
sau în același sistem de coordonate.

Compararea este un fel de a privi. Nimic din ea nu se salvează și nu schimbă modelul curent.

### Hărți scanate lângă model

Planul sau profilul desenat al unei topografii — o scanare sau o fotografie a planșei, păstrată ca
[document](documents-and-cabinets.md) imagine — poate fi arătat lângă modelul ei. În fereastra în
care se deschide un model, banda de deasupra vizualizatorului are o filă **3D** și câte o filă
pentru fiecare hartă. **Adaugă hartă**, butonul **+** de la capătul benzii, declară una: alege
**Documentul cu imaginea hărții** și **Vedere** — **Plan**, **Profil** sau **Altă vedere**.
**Setările hărții** schimbă vederea, sau **Elimină harta de pe acest model**: dispare doar
declararea, iar documentul și punctele lui rămân.

Imaginea nu are nevoie de coordonate. Ce o leagă de topografie sunt **punctele** ei. Apasă
**Definește puncte**, apoi apasă o stație în vederea 3D sau caut-o după nume — banda spune
*Se plasează A12 — apasă punctul ei pe hartă* — și dă clic acolo unde este desenată acea stație
pe planșă. O stație care are deja un punct întreabă înainte să primească un al doilea (**Mută-l
aici**); apasă un punct pentru **Mută** sau **Șterge**, iar la sfârșit **Gata**. Un
punct aparține exact scanării pe care a fost pus: după ce se încarcă o scanare mai nouă a aceleiași
hărți, punctele vechi nu sunt desenate pe noua imagine — fila spune *Puncte plasate pe o scanare mai
veche a acestei hărți: N*, iar cât definești puncte sunt listate, fiecare cu **Replasează**.
Schimbarea unei hărți cere dreptul de a modifica documentul ei, iar un punct făcut printr-o
legătură pe care nu o poți edita spune *Nu poți corecta acest punct*.

**O tură urmărită folosește punctele.** Pe panoul de urmărire al unei ture și pe pagina unei ture
publicate, fiecare planșă declarată este o filă lângă vederea 3D, iar echipa este desenată pe ea la
fiecare stație care are un punct, în aceeași reluare. Cineva aflat la o stație fără punct pe acea
planșă nu este pus pe ea prin ghicire: fișa lui spune *Fără punct pe această hartă*, iar vederea 3D
îl arată în continuare.

O foaie georeferențiată — una care are o poziție pe Pământ — este altceva și se desenează pe harta
însăși: vezi [Hărți raster georeferențiate](georeferenced-maps.md).

### Un al doilea vizualizator, de probă

Fereastra în care se deschide un model (**Vezi în 3D**) are, lângă fila **3D**, o filă
**LochViewer (de probă)**: același fișier, arătat de un al doilea vizualizator topografic care este
încercat. Se încarcă doar când deschizi acea filă și este o aplicație separată — setările,
măsurătorile și partajarea lui sunt ale lui, iar nimic din ce faci în el nu se salvează aici.
Dacă fila rămâne goală, instalarea nu permite ca el să fie arătat în interiorul paginii;
**Deschide într-o fereastră nouă** îl arată separat.

---

## Pereții peșterii (`.stl`)

Un fișier `.stl` este triunghiuri goale. Nimic din el nu spune în ce sistem de coordonate sunt
numerele — citit ca fusul vecin, ar ateriza la kilometri distanță. Așa că îl declari:

**Metri față de un punct** — exportul obișnuit. Numerele sunt metri est, nord și sus față de un
punct. Dă **longitudinea și latitudinea** acelui punct și **altitudinea nivelului zero al
fișierului**.

> Formularul precompletează originea din **intrarea principală a acestei peșteri**, de unde
> pornește de obicei un export local. Dacă nu este înregistrată nicio poziție de intrare, sau
> dacă poziția arătată *contului tău* este deliberat aproximativă pentru că locația
> peșterii este protejată, formularul o spune și îți cere punctul adevărat.

**Un sistem de coordonate proiectat** — numerele sunt coordonate est/nord într-o grilă națională
sau UTM. Dă **codul EPSG**.

De reținut și: înălțimile din aceste fișiere se măsoară de la originea proprie a exportului, nu
de la nivelul mării.

Odată convertiți, pereții sunt desenați în [scena 3D](3d-view.md#pereții-peșterii-selectate)
numai pentru peștera selectată. Lista de modele arată mărimea rețelei convertite în megaocteți
lângă numărul de triunghiuri, iar scena spune mărimea cât timp o descarcă (iar peste o anumită
mărime [întreabă înainte să descarce](3d-view.md#pereții-peșterii-selectate)), așa că o rețea de
cincizeci de megaocteți pe o conexiune contorizată este o alegere, nu o surpriză.

---

## Pereți dintr-un `.lox` sau `.3d`

Nu trebuie să exporți un `.stl` ca să vezi pereți. Când un model de drumuire este citit, pentru
același model se construiesc pereți din ce conține fișierul însuși, și din nimic altceva:

- **Un `.lox` cu schițe (scraps)** — suprafețele de perete pe care Therion le-a modelat din
  desenele tale — este desenat exact cu acele suprafețe.
- **Un `.lox` fără schițe, sau un `.3d`**, primește un tub de-a lungul fiecărei vize ale cărei
  **dimensiuni de galerie** (stânga, dreapta, sus, jos) au fost măsurate la ambele capete. Fiecare
  stație măsurată devine un inel prin cele patru puncte măsurate — stânga și dreapta așezate
  orizontal, de-a curmezișul galeriei, sus și jos la verticala stației — iar inelele consecutive
  sunt unite. Pe o viză mai înclinată de 60° inelul este așezat de-a curmezișul vizei, ca un puț
  să nu fie desenat ca o panglică.

**O viză pe care nu a măsurat-o nimeni nu primește pereți.** Nu există o mărime implicită de
galerie. O viză fără dimensiuni, sau cu dimensiuni la un singur capăt, rămâne o linie simplă; o
singură distanță care nu a fost luată este desenată ca nicio distanță, nu ghicită. Nici vizele
laterale nu primesc tub — ele sunt măsurători *ale* peretelui, nu galerii. O topografie fără
schițe și fără dimensiuni pur și simplu nu are pereți: nu este o eroare, iar modelul este tot
**Gata**.

Lista de modele arată numărul de triunghiuri și mărimea pe rândul modelului de drumuire, cu nota *„Pereți
construiți din schițele și dimensiunile de galerie ale acestei ridicări"*. Un sistem foarte mare
este desenat cu inele cu patru laturi în loc de opt; unul prea mare chiar și pentru atât — peste
două milioane de triunghiuri — nu primește pereți construiți, iar atunci un `.stl` exportat este
calea de a-l arăta.

**Ce pereți desenează scena 3D.** Un `.stl` încărcat și marcat **Curent** câștigă în continuare:
sunt pereți făcuți de cineva cu intenție. Fără el, scena desenează pereții **modelului de
drumuire curent**; în lipsa lor, cel mai nou model al peșterii care are pereți.

---

## Drumuiri

Linia topografică a unei peșteri, proiectată pe suprafața hărții și desenată la adâncime în 3D.

**Încarcă drumuire**, sau lasă ca una să fie **extrasă** dintr-o topografie compilată —
coloana *Sursă* spune care.

Fiecare drumuire își arată **lungimea** și numărul de **trasee**. Lungimea unei drumuiri
încărcate este măsurată de-a lungul galeriei, nu de-a lungul vizelor trase la pereți, care într-un
export topografic pot însuma de multe ori lungimea galeriei; una încărcată înainte ca lungimea să
fie măsurată așa își păstrează lungimea primită până când este încărcată din nou. Una poate fi făcută **forma
implicită** a peșterii, care este ce folosesc alte lucruri când au nevoie de „forma acestei
peșteri".

Pe hartă, drumuirile sunt stratul scump și au controale proprii de detaliu și buget — vezi
[Spațiul de lucru al hărții](map-workspace.md#detaliul-drumuirilor).

O drumuire fără altitudini este desenată **pe suprafață** în 3D, iar scena spune câte sunt
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

## Ce îți spune apoi topografia

Citirea unei topografii în stații și vizări este ce alimentează:

- **Statisticile din topografie** de pe pagina peșterii — lungime, lungime în plan, extindere
  verticală, cea mai mare deschidere, verticalitate, sinuozitate, punctul cel mai înalt și cel mai
  jos, și cifrele calculate față de cele declarate,
- **roza orientării galeriilor** și înclinarea,
- **cea mai mică distanță** dintre două peșteri,
- drumuirile de pe hartă și din 3D.


Toate acestea sunt pe pagina
[Măsurători și statistici](measurements-and-statistics.md).

---

Înrudite: [Vizualizarea 3D](3d-view.md) · [Peșteri și intrări](caves-and-entrances.md) ·
[Măsurători și statistici](measurements-and-statistics.md) ·
[Un film al unei ture urmărite](tracking-movie.md) — butonul cu cameră de pe rândul unei ridicări
