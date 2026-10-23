# Căutare și filtre

🇬🇧 [English](../../features/search-and-filters.md) · 🇷🇴 **Română**

[← Referință](README.md)

---

## Caseta de căutare

În bara de sus și pe hartă. O casetă, mai multe feluri de răspuns, grupate:

| Grup | |
|---|---|
| **Obiecte** | Peșteri, intrări și orice alt obiect — alegerea unuia îi deschide pagina |
| **Jurnale de tură** | Alegerea unuia deschide tura |
| **Tabere** | Alegerea uneia deschide tabăra |
| **În textul documentelor** | Ce scrie *în interiorul* documentelor tale — alegerea unuia deschide documentul la pagina găsită |
| **Locuri** | Nume de locuri de la un serviciu extern de geocodare (Nominatim) — alegerea unuia mută harta acolo |

Sub un nume, un rând mai discret spune **ce este rezultatul și de ce ține**, ca două lucruri cu
același nume să poată fi deosebite: o peșteră își arată tipul și regiunea; orice alt obiect își
arată tipul, plus numele și regiunea peșterii de care ține — peștera fiind numită doar dacă
ai voie s-o citești; un document își arată tipul și dulapurile în care este clasat. Rândul nu
conține niciodată o coordonată, o altitudine, o adresă sau o notă de localizare.

### Căutarea în interiorul documentelor

Partea distinctivă. Un rezultat de document **citează propoziția care s-a potrivit** și deschide
pagina documentului **la pagina pe care s-a găsit expresia** (sau foaia, sau diapozitivul).

- **Insensibilă la diacritice în ambele sensuri**: `pestera` găsește `peșteră`, iar rezultatul
  este citat înapoi scris așa cum l-a scris autorul.
- Cuvintele sunt **lematizate în limba în care este scris documentul** — română și engleză din
  start.
- *„Se afișează n din m documente găsite"* când sunt mai multe.
- Dacă nu s-a potrivit nimic: *„Niciun text de document nu se potrivește. Paginile scanate nu
  conțin text care să poată fi căutat până când nu sunt transcrise."* — care de obicei este
  răspunsul real.
- O **versiune înlocuită** este marcată *„versiunea n, înlocuită"* și este găsibilă doar de cei
  care ar fi putut-o înlocui.

**Găsești doar ce ai voie să citești.**

---

## Filtre

Un filtru este o **întrebare** despre înregistrările tale: la ce feluri de obiecte se
referă, ce condiții trebuie să îndeplinească și cum este sortat răspunsul. Îl construiești pe un
ecran de listă — astăzi [registrul peșterilor](#filtrul-registrului-peșterilor),
[lista de obiecte](#filtrarea-pe-paginile-de-listă) și
[galeria](photographs.md#condiții-și-vizualizări-salvate-ale-galeriei) — și îl poți păstra ca
[filtru salvat](#filtre-salvate).

**Unui filtru i se răspunde întotdeauna cu ce ai tu dreptul să vezi.** El
restrânge ce ai voie să citești; nu îl lărgește niciodată.

### Feluri de obiecte

Un filtru acoperă unul sau mai multe **feluri de obiecte**: **Obiecte · Jurnale de tură ·
Vizualizări de hartă · Documente · Hărți georeferențiate · Dulapuri · Modele topografice ·
Fotografii · Fișiere de geodate**. Fiecare apare o singură dată, și există o limită câte acoperă
un filtru.

### Câmpuri după care poți pune condiții

Câmpurile oferite depind de felul obiectului. Printre ele:

- pentru toate felurile: **Creat · Ultima modificare**, și fie un **Titlu** (jurnale de tură,
  documente și fotografii), fie un **Nume** (toate celelalte);
- pentru toate felurile, în afară de dulapuri și modele topografice: **Proprietar · Club ·
  Vizibilitate**;
- pentru obiecte (**Obiecte**) și peșteri: **Fel · Categorie · Tip · Etichetă · Locație
  protejată · Regiune · Tip de rocă · Lungime cartată (m) · Denivelare (m) · Tip de peșteră · Alte
  denumiri**, **Poziție** (vezi [Condiții de poziție](#condiții-de-poziție)), plus
  [proprietățile tipizate](#condiții-pe-proprietăți) pe care le declară un tip de obiect;
- pentru ture: **Tip de tură · Data turei · A avut incident · Stare · Club organizator**;
- pentru documente: **Tip · Limbă**;
- pentru fotografii: **Etichetă · Descriere · Nume de loc · Fotografiat la · Datată (fotografiată,
  altfel încărcată)**;
- pentru modele topografice: **Format topografie · Topografiat la · Model curent**;
- pentru hărți georeferențiate: **Tip de hartă**; pentru fișiere de geodate: **Format fișier**.

Un câmp pe care nu ai dreptul să-l folosești nu este oferit. Dacă un filtru îl numește totuși —
unul partajat cu tine sau scris de mână — este refuzat exact ca și cum câmpul nu ar
exista: refuzul nu îți spune niciodată ce se află acolo.

### Sortare

**Adăugat · Modificat · Nume · Proprietar · Dată · Cel mai apropiat**

Dacă nimic din ce se caută nu poate fi sortat într-un anumit fel, îți spune, în loc să sorteze
arbitrar. **Cel mai apropiat** este oferit doar acolo unde există un loc de la care să se
măsoare — astăzi, filtrul avansat al registrului peșterilor; vezi
[Ordonarea după cel mai apropiat](#ordonarea-după-cel-mai-apropiat).

### Limite, declarate

Un filtru este mărginit, iar fiecare margine are mesajul ei: prea multe părți (*„Încearcă să îl
împarți"*), imbricare prea adâncă, prea multe valori într-o condiție, text prea lung într-o
condiție, cel puțin un fel de obiect obligatoriu.

### Selectorul

Acolo unde alegi valori, selectorul spune ce face:

- *„Scrie cel puțin n caractere"*,
- *„Nu se potrivește nimic"* / *„Nimic ales"* / *„n alese"*,
- și, important, **„n neafișate — dispărute sau nu îți sunt vizibile"**.

Unele valori sunt **localizabile** — *„Ți se poate arăta unde este"* — și oferă un *Mergi la…*
care mută harta acolo.

Același selector este folosit oriunde alegi un obiect, o peșteră sau o tură dintr-o căutare:
părintele unui obiect, *Adaugă părinte*, ținta unei legături, membrii unui set de obiecte,
obiectul unei reguli de permisiune, cealaltă peșteră din *Cea mai mică distanță*, peștera unei
intrări noi, a unei fotografii din bibliotecă sau a unei hărți georeferențiate, tura și peștera
unui import de înregistrare și peșterile pe care le poartă un telefon. Peșterile se găsesc după
orice parte a numelui, fără a ține seama de diacritice. Doar lista de ture a importului de
înregistrare arată cele mai noi ture înainte să tastezi.

De la tastatură: **Jos** din caseta de căutare ajunge la primul rezultat, **Sus / Jos** trec de
la un rezultat la altul, **Enter** sau **Spațiu** alege. Acolo unde o alegere poate fi retrasă,
*Renunță la alegere* apare după ce ai ales ceva.

---

## Filtre pe hartă

Harta citește aceleași filtre. Un filtru salvat — ales în
[selectorul de lângă caseta de căutare](#selectorul-de-lângă-caseta-de-căutare-a-hărții), un
filtru rapid sau o vedere salvată — ori un filtru adus din registrul peșterilor sau din lista de
obiecte cu **Arată-le pe hartă** ([Între o listă și hartă](#între-o-listă-și-hartă)) restrânge
intrările de peșteri, fenomenele de suprafață, liniile de topografie și — în
vederea 3D — pereții peșterilor. Un filtru despre peșteri păstrează intrările acelor peșteri; un
filtru despre intrări păstrează acele intrări. Nu face decât să scoată din ce poți vedea deja.

- **În adresă.** Adresa hărții poartă filtrul ei la fel ca adresa registrului de peșteri, așa că
  o hartă filtrată este un link pe care îl poți trimite. Un link al cărui filtru nu poate fi
  citit — sau pe care cel care îl deschide nu îl poate rula, de pildă filtrul tău salvat
  privat — deschide harta nefiltrată și spune *„Filtrul din acest link nu a putut fi citit…"*.
- **O notificare îl numește**: *„Hartă filtrată după …"*, cu un buton care îl golește. Un filtru
  care nu mai merge cât privești (șters, nemaipartajat) golește straturile pe care le restrângea
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
  tuturor celor cu care este partajată. Salvarea uneia spune asta; măsoară în schimb de la un
  obiect sau de la o zonă numită, ori aplică un filtru salvat.

### Straturi din filtre salvate

**Adaugă un strat dintr-un filtru salvat…** în panoul de straturi desenează peste hartă încă o
întrebare: intrările și obiectele pe care le păstrează filtrul salvat, într-o culoare și un
simbol proprii, schimbate din rândul stratului. Cel mult șase deodată. Se aprind, se reordonează
și se fac transparente ca orice alt strat și se păstrează în vederile salvate. Un strat al cărui
filtru a dispărut sau nu mai poate fi rulat rămâne în listă marcat **Ilizibil** și nu desenează
nimic. Marcajele acestor straturi sunt doar de privit; selecția rămâne la straturile principale.
Linkurile partajate nu le arată.

### Filtre rapide

Fixează până la șase filtre salvate — ale tale sau partajate cu tine — din
**Filtre salvate** (*„Fixează ca filtru rapid"*). Apar ca butoane de un clic pe hartă, în bara
registrului de peșteri și pe lista de obiecte, în orice browser în care te autentifici. Un
buton aplică filtrul; apăsat din nou, îl ridică. Un buton al cărui filtru nu mai există sau nu
poate fi rulat apare estompat și spune asta, până îl scoți, din buton sau din **Filtre
salvate**.

---

## Filtrul registrului peșterilor

**Peșteri** deschide registrul peșterilor. Deasupra listei stau câmpurile lui rapide:
**Caută denumire sau toponim…**, **Toate tipurile**, **Regiune**, **Lungime (m)**
și **Filtrează după etichetă…**. Ele formează un singur filtru, iar adresa paginii îl poartă,
așa că un link copiat deschide aceeași întrebare pentru oricine îl urmează (cu răspunsul pe
care *acela* are dreptul să-l vadă).

**Filtru avansat…** deschide [editorul de condiții](#editorul-de-condiții) pe același filtru,
pentru tot ce câmpurile nu pot spune — un SAU, un NU, un interval. Când îl
aplici, părțile pe care câmpurile le pot arăta se întorc în câmpuri; restul este păstrat
alături: *„Acest filtru mai are condiții pe care aceste câmpuri nu le arată (n). Ele restrâng
lista în continuare."* — cu **Editează-le** și **Elimină-le**.

Lângă câmpuri:

- **Salvează ca filtru…** păstrează întrebarea curentă ca [filtru salvat](#filtre-salvate);
- **Aplică un filtru salvat…** alege unul pe care îl poți folosi — al tău sau unul
  partajat cu tine;
- **Filtre salvate** deschide lista lor.

Cât timp este aplicat un filtru salvat, pagina spune *„Filtru salvat: nume"*, iar *„modificat
între timp"* după ce schimbi câmpurile. **Nu mai numi acest filtru salvat** păstrează
întrebarea și uită numele.

**Salvează ca filtru…** nu este disponibil, și spune de ce când îl indici, cât timp întrebarea
conține un punct introdus, un poligon lipit sau un loc de la care se măsoară *Cel mai apropiat* —
un filtru salvat nu păstrează niciodată așa ceva. Vezi
[Condiții de poziție](#condiții-de-poziție).

Dacă filtrul dintr-un link nu poate fi citit, pagina o spune și arată toate peșterile în loc să
ghicească; cere celui care l-a trimis un link nou.

---

## Între o listă și hartă

Registrul peșterilor, lista de obiecte și harta își pasează întrebarea una alteia, ca să poți
privi același set în ambele feluri.

| Control | Unde | Ce face |
|---|---|---|
| **Arată-le pe hartă** | Registrul peșterilor, lista de obiecte | Deschide harta pe filtrul listei, așa că desenează rândurile listate |
| **Selectează pe hartă tot ce s-a găsit (n)** | Registrul peșterilor, lista de obiecte | Pune tot rezultatul listei — nu doar pagina de pe ecran — în selecția hărții și deschide harta pe el |
| **Listează peșterile din vedere** | Harta, pe rândul de sub caseta de căutare, lângă filtrele rapide | Deschide registrul peșterilor pe filtrul hărții, restrâns la ce arată harta |

**Selectează … tot ce s-a găsit** ia cel mult **200**. Când lista a găsit mai multe, o spune cu
ambele numere — *„Au fost selectate primele 200 din 1.234 pe hartă — atâtea încap într-o
selecție."* — iar selecția continuă să spună *„Primele 200 din 1.234 găsite de listă."* Se
numără doar rândurile pe care ai voie să le citești, în ordinea în care le arată lista. Cât
timp selecția este exact acel rezultat, oferă **Salvează ca filtru…**, care salvează *filtrul
care a găsit-o*, nu obiectele: rulat mai târziu, găsește ce se potrivește atunci. Nu este oferit
pentru un rezultat restrâns la vederea hărții, pentru că un filtru salvat nu păstrează niciodată
un loc.

**Listează peșterile din vedere** marchează registrul *„Doar peșterile din vederea hărții"*, cu
**Arată peșterile de peste tot** ca să ridici restrângerea. Ce se află în vedere decide
serverul, după fereastra hărții, nu după marcajele pe care harta se întâmplă să le aibă.
Exporturile registrului și panoul Distribuții primesc aceeași fereastră; exportul **KarstLink
(JSON-LD)** nu, așa că nu este disponibil până nu ridici fereastra — *„Nu cât timp lista este
restrânsă la fereastra hărții…"*. Dacă filtrul hărții este un filtru salvat care nu poate fi
citit, harta o spune și nu deschide nimic; un link care numește o fereastră ce nu este o zonă
validă deschide registrul pe peșterile de peste tot și o spune.

**Rândurile și harta se indică unele pe altele.** Cu harta deschisă lângă listă — un registru
desprins într-o fereastră proprie sau o a doua fereastră de browser — indicarea unui rând
încercuiește pe hartă intrarea acelei peșteri sau acel obiect, iar alegerea a ceva pe hartă
marchează rândul lui și îl aduce în vedere. Harta încercuiește doar ce a desenat deja, în poziția
în care l-a desenat, și niciodată o grupare, așa că nu se dezvăluie nimic ce nu ți s-a arătat
deja.

---

## Editorul de condiții

Același editor servește registrul peșterilor (**Filtru avansat**) și galeria (**Condiții pentru
fotografii**). Fiecare rând este o condiție: un **Câmp**, o **Comparație** și o **Valoare**.

- **Adaugă condiție** adaugă un rând; **Adaugă grup** adaugă un grup în interiorul celui curent.
- Fiecare grup spune cum se combină rândurile lui: **Toate acestea (ȘI)** sau **Oricare dintre
  acestea (SAU)**.
- **Nu** transformă un rând sau un grup în opusul lui.
- Grupurile se imbrică până la o adâncime fixă; dincolo de ea editorul spune *„Grupurile pot fi
  imbricate pe cel mult n niveluri."*
- **Rezultate: n** se numără pe loc, înainte să aplici, cu ce ai dreptul să vezi.
- **Aplică** pune filtrul editat pe listă.

### Comparații

Comparațiile oferite de un câmp depind de ce conține:

| Comparație | Pentru |
|---|---|
| **este** · **este unul dintre** | o alegere dintr-o listă: un tip, o etichetă, o persoană, un grup de speologie |
| **conține** · **începe cu** | text |
| **mai mic decât** · **cel mult** · **mai mare decât** · **cel puțin** · **între** | numere și date |
| **este gol** · **nu este gol** | orice poate rămâne necompletat |

**cel puțin** și **cel mult** includ valoarea însăși: *Lungime cartată cel puțin 500* găsește o
peșteră de exact 500 m; *mai mare decât 500* nu o găsește. **între** include ambele capete.

### Scrierea unei comparații

Un număr sau o dată se pot scrie cu un semn, în loc să alegi comparația din listă:

| Scris | Înseamnă |
|---|---|
| `>300` | mai mare decât 300 |
| `>=300` · `≥300` | cel puțin 300 |
| `<30` | mai mic decât 30 |
| `<=30` · `≤30` | cel mult 30 |
| `=300` | egal cu 300 |
| `!=300` · `≠300` | diferit de 300 — se bifează **Nu** pe rând, așa că găsește și ce nu are valoare înregistrată |
| `300..500` · `300-500` | între 300 și 500, ambele incluse |
| `300` | comparația deja aleasă pentru rând |

Un număr poate folosi virgula sau punctul pentru zecimale (`2,5`) și poate fi urmat de `m`
(`300 m`); virgula nu este citită niciodată ca separator de mii. O zi se scrie `2024-05-01` sau
`1.5.2024`; un interval de zile se scrie doar cu două puncte (`2024-05-01..2024-06-30`), pentru
că ziua are deja liniuțe, iar `=2024-05-01` înseamnă toată acea zi. Lângă câmp editorul spune
ce a înțeles — *„Înțeles ca: cel puțin 300 m"* — înainte de a aplica ceva. Un text pe care nu îl
poate citi, sau o comparație pe care câmpul nu o oferă, este semnalat pe rând și nu aplică
nimic. Câmpul **Lungime (m)** al registrului citește aceleași semne; acolo, un număr simplu
înseamnă *cel puțin*.

**Datele sunt zile.** Când alegi o zi, este ziua din propriul tău calendar: *Data
turei cel puțin 5 octombrie* găsește turele din 5 octombrie și de după, oriunde te-ai afla când
întrebi. La fel pentru o dată pe care o înregistrează o proprietate a unui tip de obiect și
pentru intervalul de date al galeriei. Un câmp care ține un moment, nu o zi — **Creat**,
**Fotografiat la** — numără ziua de la miezul nopții la miezul nopții acolo unde te afli. Un
filtru salvat păstrează ziua aleasă de autorul lui și o arată ca acea zi.

### Condiții pe proprietăți

Obiectele de pe hartă au un tip, iar unele tipuri își declară proprietăți tipizate proprii — un
număr, un text, o dată, un da/nu. Acolo unde se filtrează obiecte, editorul o oferă pe fiecare
ca **Proprietate: nume**, cu comparațiile pe care felul ei le permite.

O proprietate aparține tipului ei, așa că se cere **doar împreună cu tipul său de obiect, în
același grup ȘI** — editorul o spune sub rând. O proprietate a unui tip pe care nu ai dreptul
să-l citești nu este oferită și este refuzată ca un câmp care nu există. Peșterile însele nu au
un tip de obiect, așa că o condiție pe tip sau pe proprietate nu găsește niciodată o peșteră.

### Condiții de poziție

Acolo unde se filtrează peșteri și obiecte, editorul oferă **Poziție**, cu două comparații:

| Comparație | Cere |
|---|---|
| **în raza** | **Distanța în metri** (de la 1 la 200.000), apoi locul de la care se măsoară — **Măsurat de la**: **un obiect** ales cu [selectorul](#selectorul), **un punct** scris ca **Longitudine** și **Latitudine**, sau **rezultatele unui filtru salvat** |
| **în interiorul** | Un **Contur**: **o zonă numită** aleasă cu selectorul, sau **un poligon lipit** — un Polygon GeoJSON sau un Feature care conține unul, de cel mult 2.000 de poziții |

**O condiție de poziție primește răspuns după pozițiile care îți sunt arătate.** O înregistrare
pe care o poți vedea exact este măsurată acolo unde este; un punct protejat pe care nu îl
poți vedea exact este măsurat în poziția rotunjită pe care ți-o arată harta; o linie sau un
contur protejat pe care nu îl poți vedea exact nu se potrivește niciodată. Așa că acea condiție
nu îți spune niciodată despre locul unui lucru mai mult decât harta.

- Un obiect pe care nu îl poți vedea exact nu poate fi folosit ca punct de măsurare. Este
  refuzat cu aceeași propoziție ca unul care nu există — *„Locul de la care măsoară această
  condiție nu îți este disponibil."* — ca refuzul să nu îți spună care dintre ele este.
- **rezultatele unui filtru salvat** listează doar filtre salvate despre același fel de obiect,
  gata de rulat. Unul care măsoară el însuși de la un filtru salvat nu poate fi folosit, iar unul
  care găsește peste 1.000 de obiecte este refuzat ca prea mare.
- Un poligon lipit care nu este un singur poligon valid este explicat sub casetă înainte să se
  trimită ceva.

**Un punct introdus, un poligon lipit sau locul de la care se ordonează „cel mai apropiat” nu se
salvează niciodată** — nici într-un filtru salvat, nici într-o vedere salvată. *„Folosește în
schimb un obiect sau o zonă numită."* O condiție măsurată de la un obiect sau în interiorul unei
zone numite se poate salva.

### Ordonarea după cel mai apropiat

În filtrul avansat al registrului peșterilor, **Ordine** alege cum este sortat răspunsul:
**Modificat** sau **Cel mai apropiat**. **Cel mai apropiat** cere un loc de la care să măsoare —
un punct sau un obiect — iar numărătoarea și **Aplică** așteaptă până îl are: *„„Cel mai
apropiat” are nevoie de un loc de la care să măsoare: introdu un punct sau alege un
obiect."*

Doar peșterile pe care le poți vedea exact sunt ordonate după distanță. Toate celelalte peșteri
pe care le păstrează filtrul vin după ele, cele modificate cel mai recent întâi, așa că o peșteră
protejată nu este niciodată localizată după locul în care cade în listă. Ordinea se păstrează
când întorci paginile și este purtată în adresa paginii — dar locul de la care se măsoară nu
trece în exporturi și nici în *Salvează ca filtru…*.

### Când un filtru este refuzat

Dacă serverul nu poate rula un filtru, spune de ce, **pe rândul la care se referă** — de exemplu
*„Acest câmp nu admite această comparație."*, *„Intervalul se termină înainte să înceapă."* sau
*„O valoare are mai mult de 200 caractere."* Problemele care nu țin de un singur rând — prea
multe condiții, grupuri imbricate prea adânc — sunt arătate pentru filtru în ansamblu.
**Aplică** rămâne indisponibil până când filtrul poate fi rulat.

---

## Filtre salvate

Un filtru salvat este **o întrebare pe care o păstrezi**. **Filtre salvate**, din meniul
contului, le arată pe cele pe care le poți folosi: ale tale, cele partajate cu un
grup de speologie din care faci parte și cele partajate cu toți cei autentificați în această
instalare.

| Coloană | Arată |
|---|---|
| **Nume** | numele și descrierea lui |
| **Caută** | condițiile, în cuvinte |
| **Cine îl poate folosi** | **Doar eu**, **Membrii** unui grup de speologie sau **Toți cei autentificați** |
| **Salvat de** | **Tu**, autorul așa cum ți-l arată profilul lui, sau **Un membru al clubului** |
| **Stare** | **Pregătit**, **Învechit** (un tip sau o etichetă numită de el a fost ștearsă între timp, așa că acea condiție nu mai găsește nimic) sau **Nu poate fi citit** |

**Deschide în registrul peșterilor** aplică acolo un filtru despre peșteri. O vizualizare
salvată a galeriei apare și ea aici și se aplică din galerie, cu **Aplică o vizualizare salvată
a galeriei…**.

### Cu cine este partajat un filtru nou

Oriunde salvezi un filtru, fereastra arată **cine îl poate folosi** și spune într-un rând ce
înseamnă — *„Doar tu vezi acest filtru."*, *„Membrii … îl pot folosi"* sau *„Toți
cei autentificați îl pot folosi"*. Pornește de la **alegerea ta implicită**, care este
**Doar eu** până o schimbi în **Setări → Filtre salvate** (legătura **Schimbă alegerea
implicită** din fereastră duce acolo). Poți alege altfel pentru fiecare filtru salvat și îl
poți schimba mai târziu din listă. Nimeni fără cont nu vede vreodată un filtru salvat.

### Partajarea nu acordă nimic

Partajarea unui filtru — cu un grup de speologie sau cu toți cei autentificați — le dă
**doar întrebarea**. Fiecăruia i se
răspunde cu ce are el însuși dreptul să vadă; un filtru nu împrumută nimănui drepturile
autorului său. Din același motiv, un filtru salvat **nu păstrează niciodată un loc de la care se
măsoară** — *cel mai apropiat de aici* se întreabă din nou de fiecare dată.

### Cine îl poate modifica

Doar **autorul lui** sau un **administrator deplin** poate redenumi, descrie, partaja sau șterge
un filtru salvat. Lista oferă **Redenumește, descrie sau partajează** și **Șterge** exact pe
filtrele pe care le poți modifica. Ștergerea unui filtru partajat îl ia tuturor celor cu care a
fost partajat.

Un filtru salvat care întreabă ceva ce nu poți întreba — un câmp pe care nu îl poți folosi
sau o formă pe care această versiune nu o poate citi — apare ca **Nu poate fi citit** și nu poate
fi aplicat.

---

## Selectorul de lângă caseta de căutare a hărții

Lângă caseta de căutare a hărții stă un [selector](#selectorul) pentru *„pe care dintre acestea
îl am în minte”*, cu câte un buton pentru fiecare fel de lucru printre care poate căuta:

| Buton | Scris la început | Alegerea unuia |
|---|---|---|
| **Peșteri** | `#` | Îl selectează și mută harta la el |
| **Intrări** · **Toate obiectele** | | Îl selectează și mută harta la el |
| **Documente** | `@` | Deschide documentul |
| **Ture** | `!` | Deschide tura |
| **Vizualizări** | | Aplică vederea salvată |
| **Fotografii** | | Deschide fotografia |
| **Filtre salvate** | | Îl face [filtrul hărții](#filtre-pe-hartă) |

Un caracter scris la început — `#`, `@`, `!` — restrânge la acel fel, și doar chiar la început,
așa că un nume care conține unul este găsit în continuare. Un obiect pe care harta nu îl poate
așeza pentru tine — neplasat niciodată sau unul a cărui poziție nu ai voie s-o vezi
— își deschide în schimb pagina, care spune care dintre ele este. Un filtru salvat care nu
întreabă nimic despre obiecte, sau pe care nu îl poți rula, nu este aplicat: *„Filtrul salvat
nu se referă la obiecte sau nu poate fi rulat cu accesul tău, așa că nu a fost aplicat
hărții."* Rândul unui filtru salvat îi arată descrierea sub nume.

---

## Filtrarea pe paginile de listă

Fiecare pagină de listă are și filtrele ei rapide — căutare după nume, filtrare după tip, fel sau
categorie, filtrare după etichetă. Lista de peșteri, cea de obiecte, de ture, de evenimente, de
tabere și listele de documente funcționează toate așa, cu paginare și sortare pe server.

**Câmpurile listei de obiecte formează și ele un singur filtru** — **Caută după denumire…**,
**Toate felurile**, **Toate tipurile**, **Toate categoriile** și **Filtrează după etichetă…**. Ca
ale registrului peșterilor, sunt păstrate în adresa paginii, iar fiecare export din listă poartă
aceeași întrebare, așa că fișierul conține ce arată tabelul. Lista are aceleași controale pentru
filtre salvate — **Aplică un filtru salvat…**, **Salvează ca filtru…**, **Filtre salvate** și
filtrele rapide — dar nu are editor de condiții: un filtru salvat cu condiții pe care aceste
câmpuri nu le pot arăta le păstrează, o spune și oferă **Elimină-le**. Un filtru salvat care
nu se referă la obiecte este refuzat: *„Filtrul salvat nu se referă la obiecte."*

**De la tastatură**, numele din fiecare rând al registrului peșterilor, al listei de obiecte, al

listei **În vedere** de pe hartă, al listelor de ture, de evenimente, de tabere și al tabelului
din calendar este o legătură sau un buton: **Tab** ajunge la el și **Enter** îl deschide.

---

Înrudite:
[Documente și dulapuri](documents-and-cabinets.md#căutarea-în-interiorul-documentelor) ·
[Spațiul de lucru al hărții](map-workspace.md#căutarea) ·
[Peșteri și intrări](caves-and-entrances.md#lista-de-peșteri) ·
[Fenomene de suprafață](surface-features.md#lista-de-obiecte)
