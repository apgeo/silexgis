# Survoluri în vizualizarea 3D

🇬🇧 [English](../../features/flybys.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
[Un film al unei ture urmărite](tracking-movie.md)

---

Un survol zboară camera vizualizării 3D în locul tău: până la o peșteră și în jurul ei, de la o
peșteră la următoarea, în jurul unui munte, din planul topografiei până la profilul ei, pe
drumuirea unei peșteri în jos, de-a lungul unui traseu GPS sau prin vederi pe care le salvezi
singur. Alegi despre ce este și ce zbor gata făcut să folosească, schimbi orice parte a lui,
apeși **Redă** și privești.

Este oferit oriunde se află vizualizarea 3D — pagina **Vizualizare 3D**, scena de lângă hartă și
scena dintr-o fereastră proprie — oricui este autentificat.

> **Ce poate face un survol astăzi.** Poate fi redat în vizualizare; poate schimba în timpul
> zborului hărțile de bază, straturile și culorile peșterilor; poate scrie peste imagine un titlu,
> legende și un carton de final; poate fi redat pe un ecran curat ca să îl înregistrezi cu alt
> program; poate fi scris într-un fișier film chiar de SilexGIS; și poate fi păstrat sau transmis
> ca fișier. Încă nefăcută: exagerarea reliefului în timpul zborului.

---

## Deschiderea

Apasă **Survol** între butoanele camerei, pe marginea din dreapta a scenei (pe telefon este în
meniul camerei). În stânga se deschide un panou, iar jos o bară de redare. Apasă din nou butonul
sau ✕ din panou ca să îl închizi.

## În jurul a ce se zboară

| Alegere | Survolul este despre |
|---|---|
| **Ce este selectat** | Peștera, peștera intrării sau zona (un masiv, o zonă carstică, un deal) selectată pe hartă sau în scenă |
| **Peșterile din vedere** | Peșterile pe care scena le desenează acum, bifate în ordinea în care vrei să fie vizitate. **Adaugă o peșteră după nume** găsește orice peșteră pe care o poți citi și o bifează la sfârșitul listei, fie că vederea o arată, fie că nu. **Reîmprospătează lista** citește din nou vederea după ce te-ai mutat; **Traseul cel mai scurt** pune peșterile bifate în ordinea în care camera călătorește cel mai puțin |
| **Peșterile selectate de un filtru salvat** | Peșterile pe care le păstrează unul dintre filtrele tale salvate, **cel mult** numărul pe care îl dai (la început 12), în ordinea în care le sortează filtrul. Sunt oferite doar filtrele care pot fi rulate |
| **Ce arată vederea** | Orice este în centrul scenei când apeși Redă — o creastă, o vale, un grup de peșteri — fără să fie nevoie să selectezi ceva |
| **Un fișier importat** | Un traseu GPX sau o linie dintr-un KML ori alt fișier importat la **Geodate**, ales din lista fișierelor importate; ce urmează *De-a lungul unei linii* |

Un survol despre un filtru salvat păstrează doar filtrul și numărul — niciodată peșterile. Care
peșteri sunt se află abia când survolul este redat, pentru cine îl redă: altcineva care îți
deschide survolul vizitează peșterile pe care filtrul le păstrează pentru *el*, iar un filtru
modificat între timp este urmat. Un filtru care nu păstrează nicio peșteră pe care cel care
redă o poate vedea, sau care nu mai poate fi rulat, este semnalat înainte să se miște ceva.
*Turul peșterilor* unui filtru este scris o singură dată pentru toate peșterile lui — lista
secțiunilor spune „fiecare dintre peșterile din …” — și este parcurs o dată pentru fiecare peșteră.

O peșteră este găsită din ce desenează scena din ea: drumuirile ei când sunt încărcate, altfel
intrările ei. O peșteră care nu este desenată deloc este căutată, camera merge să o privească
pentru ca scena să o încarce, iar zborul este construit din ce este desenat apoi.

## Zborurile gata făcute

| Presetare | Ce face camera |
|---|---|
| **În jurul unei peșteri** | Se apropie, dă ocol peșterii și, dacă i se cere, coboară sub pământ până în punctul ei cel mai de jos și înapoi |
| **Turul peșterilor** | Vizitează pe rând fiecare peșteră — până la ea, în jurul ei — și se încheie deasupra tuturor |
| **În jurul mai multor peșteri** | Un singur ocol, destul de larg cât să le cuprindă pe toate |
| **În jurul unui munte** | Dă ocol zonei de deasupra crestelor, privind spre interior |
| **Din plan în profil** | Pornește de deasupra, ca în planul topografiei, se înclină până la vederea din lateral și se rotește |
| **Coborâre** | Deasupra celui mai înalt punct desenat al peșterii, apoi pe drumuire în jos până la cel mai adânc punct, cu camera puțin în urmă și deasupra punctului urmărit, privind de-a lungul galeriei; o clipă la fund, apoi înapoi sus |
| **De-a lungul unei linii** | Coboară la începutul traseului unui fișier importat și îl urmează la o înălțime deasupra terenului, privind înainte, apoi se ridică pentru a-l arăta întreg |
| **Vederile mele** | Zboară prin vederi pe care le salvezi singur din scenă, în ordinea din listă — vezi [Vederile mele](#vederile-mele) |

Sunt oferite doar presetările care se potrivesc cu ce ai ales: un tur are nevoie de cel puțin
două peșteri, o coborâre de o peșteră, un zbor de-a lungul unei linii de un fișier importat.
*Vederile mele* este oferită mereu.

**Coborârea** urmează drumuirile pe care le desenează scena, găsite când apeși Redă: nimic din
drumuire nu este scris în survol. Merge pe drumul cel mai scurt de-a lungul liniilor, de la cel
mai înalt punct la cel mai adânc. Dacă drumuirea peșterii nu este desenată, nu are adâncimi sau
cel mai înalt și cel mai adânc punct sunt pe linii care nu se întâlnesc — scena desenează doar o
parte din ea ori o parte nu a fost niciodată legată —, camera coboară direct până la cel mai adânc
punct, iar panoul spune asta după ce survolul a fost pregătit.

## Opțiuni

| Opțiune | Face |
|---|---|
| **Ocoluri în jurul fiecăreia** | De câte ori dă camera ocol |
| **Secunde pe ocol** | Cât durează un ocol complet |
| **Sens** | În sensul acelor de ceasornic sau invers, văzut de sus |
| **Coboară sub pământ până în punctul cel mai de jos al peșterii** | Adaugă o coborâre sub suprafață și drumul înapoi (pentru *În jurul unei peșteri* și *Turul peșterilor*) |
| **Oprire la fiecare peșteră timp de** | *Turul peșterilor* se oprește nemișcat deasupra fiecărei peșteri atâtea secunde după ce îi dă ocol (0: fără oprire) |
| **Scrie peste imagine** | Cât timp turul stă la o peșteră: nimic (implicit), numele peșterii sau numele cu lungimea și adâncimea ei. Numele și cifrele sunt citite de pe server când survolul este pregătit, așa cum ai voie să le vezi; nu sunt scrise în survol |
| **Pornește de departe, la scara țării** | Orice zbor gata făcut începe sus de tot — toată țara în imagine — și coboară până unde ar fi început altfel, privind drept în jos cât este departe și înclinându-se doar când este aproape |
| **Viteza întregului survol** | De la ×0,25 la ×4: același zbor în mai mult sau mai puțin timp |
| **Încarcă mai întâi traseul** | Înainte de redare, camera vizitează punctele-cheie ale zborului și așteaptă la fiecare să se încarce harta și peșterile, astfel încât zborul însuși să nu le arate sosind. Durează puțin și merită; oprește opțiunea pentru o privire rapidă |

Deasupra solului camera păstrează o distanță față de teren, așa că un ocol în jurul unei peșteri
de pe un versant nu trece prin coasta dealului. Limita este ridicată doar pentru partea de sub
pământ.

## Secțiuni

Deschide **secțiunile** ca să vezi zborul ca listă — apropiere, ocol, spre peștera următoare,
coborâre în ea, urcare înapoi — fiecare cu durata ei. Fiecare secțiune are **secundele** ei și
**viteza** ei (de la ×0,25 la ×4), așa că o parte poate fi încetinită fără să atingi restul.
Secțiunea zburată este evidențiată cât timp survolul este redat.

Sub fiecare secțiune sunt butoane pentru a **modifica** ce face, a o **muta** mai sus sau mai jos,
a o **dubla** și a o **elimina** (un survol păstrează cel puțin una). La capătul listei alegi un
fel și apeși **Adaugă o secțiune** ca să adaugi una la sfârșit. O secțiune eliminată își ia cu ea
schimbările aspectului și legenda; una mutată le păstrează.

**Modifică** deschide ce face secțiunea. Fiecare valoare este fie aleasă aici, fie lăsată **ca
înainte** — goală —, iar atunci camera păstrează ce i-a lăsat secțiunea dinainte:

| Câmp | Face |
|---|---|
| **Fel** | Cum se numește secțiunea; *spre* (subiectul următor) este zburat în arc, *pe drumuire în jos* și *de-a lungul liniei* urmează o linie, celelalte sunt deplasări |
| **Despre** | Despre care dintre subiectele survolului este camera în timpul ei, sau despre toate împreună |
| **Rotire cu** | Cât se rotește camera, în grade sau în ture; negativ înseamnă invers |
| **Orientare** | Pentru o secțiune care spune încotro să privească, nu cât să se rotească (o vedere salvată) |
| **Unghi sub orizont** | −90 privește drept în jos |
| **Distanță** | În mărimi ale subiectului: 2 înseamnă de două ori mai departe decât este de mare peștera. O secțiune care urmează un traseu își spune distanța în metri |
| **Privește spre** | Mijlocul subiectului, punctul lui cel mai înalt sau cel mai jos |
| **Ridicat cu** | Ridică punctul privit, în jumătăți din înălțimea lui |
| **Deasupra terenului** | Cel puțin atâția metri deasupra terenului, sau **poate intra sub teren** |
| **Scrie peste imagine** | Nimic, numele, sau numele, lungimea și adâncimea subiectului, jos, pe toată durata secțiunii |
| **Se oprește la sfârșitul ei** | Camera se oprește în loc să zboare mai departe în secțiunea următoare |

O valoare în afara limitelor este spusă cu roșu lângă secțiune. Schimbarea subiectului
survolului, a presetării sau a opțiunilor construiește secțiunile din nou, iar panoul spune când
secțiunile de pe ecran sunt unele deschise sau modificate de tine.

## Vederile mele

Alege **Vederile mele** ca presetare pentru un zbor pe care niciunul gata făcut nu îl dă. Adu
scena la o imagine dorită și apasă **Adaugă această vedere**; fă la fel pentru imaginea următoare
și pentru cea de după. Survolul zboară de la fiecare vedere la următoarea ca un singur zbor lin și
se oprește pe ultima.

Fiecare vedere de după prima are **secundele** cât durează zborul până la ea. O vedere poate fi
**luată din nou** din camera de acum, mutată mai sus sau mai jos, ori eliminată. O vedere luată
de sub teren — din interiorul unei peșteri — lasă zborul să intre sub teren ca să ajungă la ea;
între vederi luate deasupra terenului camera rămâne deasupra lui.

O vedere este păstrată ca punctul pe care îl privea și ca distanța și direcția din care stătea
camera, așa că **un survol din vederile tale conține locuri**: panoul spune asta, iar un fișier al
lui spune unde sunt acele locuri. Mijlocul imaginii trebuie să arate terenul când apeși **Adaugă
această vedere**.

## Cum arată scena și ce se scrie peste ea

Sub secțiuni, încă patru părți ale panoului. Niciuna nu îți schimbă setările straturilor: ce arată
un survol este arătat doar în scenă, harta plană de lângă ea nu este atinsă, iar când survolul se
oprește scena este exact cum o aveai.

**Aspectul la început** — cum arată scena când începe survolul. Fiecare setare este fie aleasă
aici, fie lăsată **cum o ai**.

| Setare | Face |
|---|---|
| **Harta de bază** | Harta de bază cu care începe survolul; sunt oferite doar cele pe care vizualizarea 3D le poate desena |
| **Alege cât de puternic se desenează harta de bază** | Sub 100 % terenul se estompează și peșterile de sub el se văd prin el |
| **Culoarea cerului** | Scena nu desenează un cer al ei — deasupra orizontului este negru. Un survol construit dintr-o presetare începe cu un cer albastru deschis; debifează ca să fie negru |
| **Colorează pereții peșterii** | Pereții amestecați cu o culoare, cu o intensitate; umbrirea lor rămâne |
| **Drumuirile într-o singură culoare** | În locul culorilor după adâncime |
| **Terenul peste peșteri** | Desenat peste peșteri sau decupat deasupra peșterii |
| **Alege obiectivul** | Cât de largă este imaginea, de la 10° la 100° pe latura ei mai lungă (al scenei este de 60°). Un obiectiv îngust, de departe, turtește un munte ca un zid în spatele peșterii; unul larg cuprinde o vale de aproape |

**Schimbări gata făcute** — seturi de schimbări în timp, scrise dintr-o apăsare. Adăugat din nou,
un set îl înlocuiește pe cel adăugat înainte, nu îl dublează.

| Set | Face |
|---|---|
| **Hărțile de bază pe rând** | Hărțile de bază pe care le alegi, una după alta, câte atâtea secunde fiecare, cu tăietură sau cu trecere treptată, reluate de la prima până la sfârșitul survolului |
| **Prin teren în timpul ocolului** | În timpul fiecărui ocol harta de bază se estompează până la intensitatea aleasă și revine |
| **Colorează peșterile** | De la începutul secțiunii alese, pereții și drumuirile într-o singură culoare |
| **Hartă veche, hartă nouă** | O hartă georeferențiată apare treptat peste harta de bază în timpul unei secțiuni și dispare din nou |
| **Luminează peștera vizitată** | Toate peșterile încep estompate — pereții și drumuirile în a doua culoare — iar la fiecare oprire a unui tur peștera vizitată se luminează în prima culoare când camera se îndreaptă spre ea și se estompează din nou când camera pleacă. Într-un tur al peșterilor unui filtru salvat, fiecare dintre ele pe rând |
| **Obiecte pe rând** | Straturi de obiecte arătate unul după altul: straturile din filtre aflate în lista hărții și cele ale survolului (mai jos). Toate sunt ascunse când începe survolul |

**Obiecte arătate de survol** — straturi de obiecte pe care survolul le desenează singur, fără
să adaugi mai întâi un strat din filtru pe harta plană. Fiecare este ales **după tip** — unul sau
mai multe tipuri de obiecte din catalog — sau **după un filtru salvat** și are o culoare și un
simbol proprii, din aceeași paletă ca straturile din filtre ale hărții. Un asemenea strat este
desenat doar în scena 3D și doar cât timp survolul rulează; lista ta de straturi nu se schimbă
niciodată, iar când survolul se oprește stratul dispare. Este arătat de la început, dacă aspectul
de la început nu îl ascunde, și poate fi afișat, ascuns și estompat în momentele alese, ca orice
alt strat. Ce arată este cerut acelorași rute ca straturile din filtre ale hărții, cu drepturile
celui care redă survolul. Eliminarea unui strat elimină și schimbările care îl afișau sau îl ascundeau.

La **Straturi**, fiecare strat al acestei instalări — cele ale scenei, suprapunerile de plăci,
hărțile georeferențiate, fișierele importate, straturile din filtre — poate fi **afișat**,
**ascuns** sau lăsat **cum îl ai** când începe survolul, cu o intensitate proprie sau cu a ta.

**Schimbări** — fiecare schimbare pe câte un rând, în cuvinte: ce face, secțiunea căreia îi
aparține, la câte secunde de la începutul secțiunii pornește și cât durează. Corectează numerele
sau șterge o schimbare. **Adaugă o schimbare** scrie una de mână: ce se schimbă (harta de bază,
intensitatea ei, un strat afișat sau ascuns, intensitatea unui strat, culoarea pereților,
culoarea drumuirilor, terenul peste peșteri, cerul, obiectivul — un zoom cu camera pe loc), în ce, în timpul cărei secțiuni, la câte
secunde de la începutul ei și cât durează. O schimbare aparține secțiunii ei, așa că o secțiune făcută mai lentă sau
mai rapidă își mută schimbările cu ea. Drumuirile și terenul peste peșteri se schimbă dintr-o dată,
nu treptat. Două schimbări ale aceluiași lucru în același timp sunt semnalate, iar survolul nu
poate fi redat până când una nu este mutată.

**Texte peste imagine** — un **titlu** (cu un al doilea rând) arătat din primul moment, o
**legendă** pentru orice secțiune, arătată jos cât timp secțiunea este zburată, un **carton de
final** arătat până în ultimul moment, iar imaginea poate apărea din negru la început și se poate
stinge în negru la final. O **săgeată a nordului** în colțul din dreapta sus se rotește odată cu
camera, iar **un rând de text în colț** — numele unui club, o dată — stă în stânga sus pe tot
survolul. Sunt desenate peste scenă proporțional cu mărimea ei, fac parte din ce arată ecranul
curat și sunt în fiecare cadru al unui film.

Trasă înapoi, poziția anulează ce a schimbat survolul după acel moment, exact cum redarea înainte
a schimbat.

## Redarea

| Control | Face |
|---|---|
| **Redă / Pauză** | Pornește survolul sau îl ține unde este |
| **Oprește** | Încheie survolul și pune camera înapoi unde era |
| **Poziția** | Trage-o la orice moment, înainte sau înapoi, în timpul redării sau nu |
| **Repetă** | Redă din nou de la început când se termină |
| **Ecran curat** | Vezi mai jos |
| **Fă un film** | Vezi [Facerea unui film](#facerea-unui-film) |

Cât timp survolul se pregătește, bara spune ce face — *Se caută peșterile*, *Se încarcă
traseul* — iar **Oprește** îl anulează.

**O apăsare sau o derulare pe scenă pune survolul pe pauză**: camera este din nou a ta. **Redă**
o pune înapoi pe traseul ei și continuă.

Cât timp un survol conduce camera:

- butonul de straturi este ținut pe loc, la fel și alegerile din care este construit survolul —
  oprește-l ca să le schimbi;
- harta 2D de lângă scenă nu urmărește camera, iar bara de adrese păstrează poziția pe care o
  aveai înainte, așa că un link copiat în timpul unui survol nu este un link către un moment al
  lui.

Când oprești survolul sau închizi unealta, camera se întoarce unde o aveai.

## Ecran curat — pentru înregistrarea cu alt program

**Ecran curat** arată doar scena: fără butoane, fără panou, fără note — doar mențiunile de sursă
ale hărților, pe care licențele surselor le cer pe orice imagine a plăcilor lor. Vizualizarea
ocupă tot ecranul acolo unde browserul permite. După o numărătoare inversă de trei secunde
survolul este redat de la început, ca să poți porni mai întâi programul de înregistrare.

- **Spațiu** pune pe pauză și reia.
- **Escape** te aduce înapoi la vederea obișnuită, cu survolul ținut unde era.
- O apăsare pe scenă nu face nimic aici, așa că un clic rătăcit nu poate strica o înregistrare.
- Scena este încadrată în forma cadrului de film ales ultima dată la **Fă un film** (16:9 până
  alegi altceva), cu benzi întunecate în jur, ca o înregistrare a ecranului să aibă forma dorită.

## Facerea unui film

**Fă un film** (butonul cu cameră de pe bară) scrie survolul într-un fișier **MP4**, **WebM** sau
**GIF**, în browser — nimic nu este trimis serverului. Panoul survolului lasă locul panoului de
film, iar scena este **încadrată în forma cadrului filmului** lângă el, cu benzi întunecate în
jur: ce vezi este ce va conține fișierul. Poți în continuare să redai survolul și să îi tragi
poziția ca să vezi orice moment.

Fiecare cadru este desenat pentru propriul lui moment al survolului și dat fișierului cu timpul
lui exact, așa că filmul curge lin oricât de încet ar desena calculatorul scena. Un calculator
lent face un export lent, niciodată un film sacadat. Singura excepție este **Înregistrarea în timp
real**, mai jos, pe care o alegi când vrei fișierul în timpul survolului însuși.

| Setare | Face |
|---|---|
| **Fișiere gata pregătite** | *Video HD* (1280 × 720, 30 de cadre pe secundă — MP4 unde browserul îl scrie, altfel WebM), *Reels / TikTok* (MP4 de 1080 × 1920), *Postare pătrată* (MP4 de 1080 × 1080), *Pentru chat* (un GIF mic). Un fișier gata pregătit schimbă doar formatul, mărimea, ritmul și calitatea |
| **Format** | MP4, WebM sau GIF — este oferit doar ce poate scrie acest browser. Un browser fără codor video face doar GIF-uri și spune asta |
| **Dimensiunea cadrului**, **Cadre pe secundă**, **Calitate** | Aceleași mărimi și ritmuri ca la filmul unei ture urmărite. Un GIF are cel mult 800 de pixeli lățime și 600 de cadre |
| **Cum este luat fiecare cadru** | **Rapid**: fiecare cadru este luat imediat ce este desenat, cum l-ar arăta vizualizarea în timp real, cu plăcile hărții încă sosind. **Fin**: înaintea fiecărui cadru scena termină de încărcat ce arată — niciun cadru cu teren pe jumătate încărcat, dar secunde pe cadru pe un calculator fără placă grafică. **Fin, mai mare decât ecranul**: ca Fin, iar scena este desenată la mărimea filmului chiar când aceasta e mai mare decât cadrul de pe ecran — cel mai clar fișier și cel mai lent. Rapid și Fin desenează scena cel mult cât cadrul de pe ecran și măresc imaginea. **Înregistrare în timp real** — oferită doar în browserele cu un înregistrator propriu (Chrome, Edge, Firefox, Safari) — vezi [Înregistrarea în timp real](#înregistrarea-în-timp-real) |
| **Ciornă** | Jumătate din mărime, 8 cadre pe secundă (10 pentru un GIF), luate rapid: ca să verifici ritmul unui survol într-o fracțiune din timp |
| **Ce se filmează** | Tot survolul sau o porțiune din el, de la o secundă a ceasului survolului la alta |
| **Imagine fixă la sfârșit** | Ține ultimul moment atâtea secunde |

Sub setări, panoul spune câte cadre va avea filmul, cât durează, cam cât de mare va fi fișierul
și numele sub care va fi salvat: numele survolului când are unul (`silexgis-<nume>-<dată>`),
altfel `silexgis-flyby-<dată>`.

**Exportă** face fișierul. Panoul arată cât a ajuns și cam cât mai durează; **Anulează exportul**
îl oprește și nu se salvează nimic. Închiderea panoului sau Escape în timp ce se face un film te
întreabă mai întâi dacă vrei să îl oprești. Oricum s-ar încheia exportul, camera și scena sunt
puse înapoi cum erau.

După un export **Fin**, panoul spune câte cadre s-au făcut, cât a durat și câte cadre — dacă au
fost — au trebuit luate înainte ca scena să termine de încărcat, pentru că un server de hărți a
fost prea lent.

Un film video lung (mai mult de aproximativ 256 MB) este scris direct într-un fișier ales de tine,
pe măsură ce se face, în browserele care permit asta (Chrome, Edge); în rest este făcut în
memorie și apoi descărcat.

### Înregistrarea în timp real

**Înregistrare în timp real** redă survolul în timp real și îl filmează chiar înregistratorul
browserului, cu texte și mențiuni de sursă cu tot. Durează exact cât survolul — nu mai mult, oricât
de lent ar fi calculatorul — dar fișierul conține **ce a arătat ecranul, cu viteza cu care l-a
desenat acest calculator**:

- Pe un calculator lent filmul sacadează. Un cadru pe care calculatorul nu l-a putut desena la timp
  pur și simplu lipsește din fișier. După înregistrare panoul spune câte cadre s-au înregistrat și
  câte pe secundă a însemnat asta, și avertizează când e mult sub ritmul ales.
- **Cadre pe secundă** este cel mult cât ia înregistrarea, nu o promisiune.
- În unele browsere (printre ele Chrome) fișierul nu are durată și nici puncte de derulare:
  playerele îl arată fără timp total și s-ar putea să nu te lase să sari prin el.
- Fișierul este MP4 unde browserul înregistrează MP4, altfel WebM. GIF nu este oferit, iar
  **Ciornă** face în locul ei o ciornă cadru cu cadru.
- Scena este desenată cel mult cât cadrul ei de pe ecran, iar imaginea este mărită la mărimea
  filmului.

Pentru un fișier fluent pe orice calculator, folosește **Rapid** sau **Fin**.

- **Mențiunile de sursă ale hărților sunt în fiecare cadru** și nu pot fi oprite: licențele
  surselor de hărți le cer pe orice imagine a plăcilor lor.
- **Filmul arată unde sunt aceste peșteri.** Panoul spune asta lângă Exportă, mereu: fișierul
  trăiește mai mult decât verificarea care ți-a permis să le vezi, așa că trimite-l doar celor
  care au voie să vadă asta.

## Păstrarea unui survol

| Control | Face |
|---|---|
| **Nume** și **Salvează** | Păstrează survolul în acest browser sub acel nume; salvarea sub un nume deja folosit îl înlocuiește. Se păstrează cel mult 50 |
| **Deschide un survol salvat** | Îl aduce înapoi, cu secțiunile, viteza, repetarea, aspectul, schimbările și textele lui |
| **Șterge** | Elimină survolul păstrat al cărui nume este în câmpul de nume |
| **Salvează în fișier** / **Deschide din fișier** | Scrie survolul ca un fișier mic `.flyby.json` și citește unul — ca să muți un survol în alt browser sau să îl trimiți cuiva |

Un survol își numește peșterile, zonele și fișierele prin referință, nu prin poziție — în afară de
vederile salvate pentru *Vederile mele*, care sunt locuri și pe care panoul le semnalează ca atare.
Deschis de altcineva,
zboară în jurul a ceea ce *acea persoană* are voie să vadă: o peșteră pe care nu o poate citi este
raportată ca negăsită, iar o peșteră al cărei loc exact este protejat este survolată în locul
aproximativ care îi este arătat. Survolurile sunt păstrate în browser, nu pe server; ștergerea
datelor browserului pentru acest site le elimină — pentru asta există fișierul.

## De știut

- **Pereții.** Pereții arătați sunt cei pe care scena i-ar arăta oricum: ai peșterii selectate
  sau ai fiecărei peșteri din vedere, dacă asta este aprins în straturi. Un tur al unor peșteri
  pe care nu le-ai selectat le arată drumuirile. Un survol poate arăta sau ascunde pereții,
  scanările și fotografiile din stații, dar nu le poate estompa: apar și dispar dintr-o dată.
- **O culoare pentru o singură peșteră.** *Colorează peșterile* dă tuturor peșterilor din scenă o
  singură culoare; *Luminează peștera vizitată* dă peșterii fiecărei opriri o culoare proprie
  peste aceasta. Schimbările ei sunt listate ca celelalte și pot fi corectate sau șterse; nu se
  scriu de mână.
- **Pereți mari.** Pereții destul de mari încât vederea să întrebe înainte de a-i descărca sunt
  întrebați o singură dată, înainte ca survolul să fie redat, pentru toate peșterile de pe traseu:
  câte și cât de mari. **Descarcă-i** îi aduce; **Rulează fără ei** redă survolul fără ei, iar
  vederea nu mai întreabă cât timp rulează. Doar pereții peșterii selectate așteaptă un da: pereții
  tuturor peșterilor din vedere sunt ținuți într-un buget.
- **Un calculator lent.** Un survol este redat în timp real. Pe un calculator fără placă grafică
  scena desenează doar câteva cadre pe secundă, iar zborul arată ca atare — camera este mereu în
  locul potrivit pentru acel moment, doar că este văzută mai rar.
- **Un tur lung.** Pregătirea vizitează fiecare peșteră care nu este încă desenată, apoi traseul.
  Douăsprezece peșteri cu traseul încărcat pot lua câteva minute înainte de primul cadru.
- **Urmarea unei linii.** O secțiune care urmează o drumuire sau un traseu zboară mai întâi la
  începutul liniei, apoi pornește de-a lungul ei, prinzând viteză, și încetinește până se oprește
  la capăt. Linia este netezită pe o distanță cam cât cea a camerei față de ea, așa că o drumuire
  în zigzag sau un traseu GPS tremurat nu sunt zburate tronson cu tronson. Durata urmăririi este
  a secțiunii: lungește secțiunea pentru un traseu lung. Spre un fișier fără nicio linie se zboară
  direct, iar panoul spune asta.
- **Numele peșterilor peste imagine** sunt scrise doar când le ceri și doar așa cum ai voie să le
  citești; un fișier de survol își numește peșterile prin referință, niciodată prin nume.
