# Analize carstice: zone, tipare și rețele de galerii

🇬🇧 [English](../../features/karst-analyses.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Măsurători și statistici](measurements-and-statistics.md) ·
[Spațiul de lucru al hărții](map-workspace.md) · [Fenomene de suprafață](surface-features.md)

---

Trei carduri pe pagina unei zone și două panouri pe pagina unei peșteri răspund la întrebări
despre carst în ansamblu: cât de des stau peșterile într-o zonă, dacă sunt împrăștiate sau
grupate, cât de adânc merge un sistem și ce formă fac galeriile unei peșteri. Ele respectă regula
comună a aplicației: **fiecare cifră este numărată pe ce ai voie să vezi, iar o cifră care nu se
poate măsura rămâne goală și spune de ce.** Două conturi pot citi, pe bună dreptate, totaluri
diferite pentru aceeași zonă.

Aceste cifre sunt pentru conturile autentificate. Ele nu fac parte din linkul unei vederi
partajate.

---

## Statistici pe zona carstică

Pe pagina unei **zone carstice**, a unui **masiv** sau a unui **deal**: **Statistici pe zona
carstică**.

Cardul începe cu temeiul lui, pentru că fără el e ușor de înțeles greșit:

> Numărat pe peșterile declarate ca fiind în această zonă

O peșteră aparține zonei pentru că cineva a încadrat-o acolo, nu pentru că poziția ei cade în
interiorul conturului. Cele două pot să nu coincidă, așa că cardul spune câte peșteri pe care le
poți localiza cad în interiorul conturului fără să fie încadrate în zonă. Numărul este un indiciu
despre cât s-a îndepărtat încadrarea de hartă. Nu face parte din nicio cifră.

| Cifra | Ce este |
|---|---|
| **Peșteri**, **Intrări** | Numărate pe peșterile încadrate în zonă pe care le poți citi |
| **Suprafață (km²)** | Suprafața conturului |
| **Peșteri pe km²** | Peșterile raportate la această suprafață |
| **Galerii topografiate (m)**, **Metri topografiați pe km²** | Lungimea topografiată a acestor peșteri și lungimea pe kilometru pătrat |
| **Peșteri cu lungime topografiată** | *n din m*: peșterile care au o lungime |
| **Depresiuni cartate**, **Acoperire cu depresiuni (%)** | Dolinele desenate ca contururi în zonă și ponderea terenului pe care o acoperă |
| **Indice de carstificare** | O singură citire, de la *foarte scăzut* la *foarte ridicat*, vezi mai jos |
| **Cele mai adânci peșteri**, **Cele mai lungi peșteri** | Capul fiecărei liste |
| **Tip de rocă** | Cum se împart peșterile zonei pe tipuri de rocă, cu **Neînregistrat** ca rând propriu |

Cardul apare doar pentru aceste trei feluri de obiecte. O clădire sau un marcaj are și el un
contur, dar peșterile pe kilometru pătrat al unei clădiri nu este o citire pe care o vrea cineva.

### Indicele de carstificare

Indicele este media a două citiri, fiecare așezată pe o scară de la zero la unu față de o
referință declarată:

- **Densitatea peșterilor** față de cinci peșteri pe kilometru pătrat. Peste această valoare o
  zonă primește unu, în loc să fie clasată față de alte zone dense.
- **Acoperirea cu depresiuni** față de o zecime din teren acoperită de depresiuni cartate.

Cardul arată fiecare componentă ca *„valoare față de o referință de …”*, așa că două zone se pot
compara și pe componente, nu doar pe clasă. **O citire lipsă este lăsată în afara mediei, nu
socotită zero**, iar cardul o spune (*„neînregistrat, deci este exclus din indice în loc să fie
socotit zero”*). Majoritatea registrelor au peșteri cu mult înainte să aibă doline desenate: o zonă
fără doline cartate, socotită ca și cum n-ar avea niciuna, ar ajunge sub o zonă care chiar nu are.
O zonă care nu se poate măsura deloc arată **nemăsurabil** și este desenată gri pe hartă.

Aceleași clase colorează zonele din stratul **Indice de carstificare** de pe
[hartă](map-workspace.md#straturile-carstice); un clic pe o zonă de acolo deschide acest card.

### Ce nu face niciodată

- Nu numără niciodată o peșteră pe care nu ai voie s-o citești, deci nu-ți spune că lipsește una.
- Nu înlocuiește încadrarea cu conturul: peșterile din interiorul conturului, dar încadrate în
  altă parte, sunt raportate ca o deviație, nu adăugate la totaluri.
- Nu socotește niciodată zero o lungime sau un contur de depresiune neînregistrat.

---

## Densitate, spațiere și aliniere

Pe aceleași pagini: **Densitate, spațiere și aliniere**. Întreabă cum sunt aranjate intrările
zonei, nu câte sunt.

Alegi **dimensiunea celulei** dintr-un rând scurt de variante. Cea mai fină este **grila de
protecție a locației** a acestei instalări, pentru că o numărătoare în celule mai mici decât
rotunjirea care protejează pozițiile peșterilor ar arăta rotunjirea, nu peșterile. Cardul spune ce
dimensiune este folosită și câte intrări a numărat grila.

| Citirea | Ce îți spune |
|---|---|
| **Indice Clark–Evans** | Sub unu, intrările stau mai aproape unele de altele decât ar face-o hazardul; peste unu, sunt mai uniform distanțate; unu nu se distinge de hazard. *z* și *p* sunt date alături. |
| **L al lui Ripley** | Aceeași întrebare pusă la multe distanțe, desenată cu o **bandă** făcută din împrăștieri aleatoare ale aceluiași număr de intrări peste aceeași zonă. Unde linia iese din bandă, dispunerea diferă de hazard la acea distanță. Numărul de împrăștieri și sămânța lor sunt afișate, ca imaginea să poată fi reprodusă. |
| **Aliniere** | Direcțiile busolei între perechi de intrări aflate la distanțe dintr-un interval declarat, ca trandafir al vânturilor. O direcție puternică urmează adesea o diaclază sau o falie, dar cardul desenează direcțiile și nu propune nimic. |
| **Zone calde și reci** și **Indicele I al lui Moran** | Dacă celulele dense stau lângă cele dense. Citite peste celulele deja desenate, deci nu pot arăta un detaliu pe care grila nu îl are. |

Propozițiile pe care le afișează cardul fac muncă adevărată:

- **Pozițiile protejate sunt numărate, dar nu folosite la spațiere.** Grila numără toate intrările
  pe care ai voie să le vezi, pe grila de protecție. Spațierea și alinierea folosesc **doar
  intrările pe care le poți localiza exact**: o spațiere măsurată între coordonate rotunjite
  măsoară rotunjirea. Cardul spune câte sunt de fiecare fel. Când prea puține pot fi localizate
  exact, spune că sunt *„prea puține pentru ca o spațiere să însemne ceva”* și nu desenează nimic.
- **„Întâmplător” nu înseamnă „nu e nimic acolo”.** *„Aceasta este o nereușită de a găsi o
  structură, nu o dovadă că nu există una.”*
- **O celulă izbitoare nu este o descoperire.** Scorul de zonă caldă și rece este luat în fiecare
  celulă deodată și nu este corectat pentru asta, deci câteva celule izbitoare pe o fereastră mare
  sunt exact ce produce singură întâmplarea. *„Citește nuanțarea, nu celula individuală.”*
- **Fără simulare, fără bandă.** Dacă nu s-a rulat nicio împrăștiere aleatoare, cardul spune că nu
  s-a testat nicio semnificație, în loc să deseneze o bandă goală.

Stratul **Densitatea peșterilor (grilă)** de pe hartă este aceeași grilă, desenată peste ce te uiți;
vezi [Spațiul de lucru al hărții](map-workspace.md#straturile-carstice).

---

## Adâncimea și înălțimea sistemului

Pe pagina unui **sistem de peșteri**, precum și pe paginile zonei carstice, masivului și dealului:
**Adâncimea și înălțimea sistemului**. Este setul de cifre din
[Adâncime și înălțime între intrări](measurements-and-statistics.md#adâncime-și-înălțime-între-intrări),
luat peste peșterile membre încadrate în obiectul paginii.

| Cifra | Ce este |
|---|---|
| **Peșteri membre măsurate** și câte dintre ele au o ridicare cu altitudini | Populația din spatele fiecărei cifre de mai jos |
| **Cea mai înaltă intrare a sistemului**, **Cea mai joasă intrare a sistemului**, **Diferența de nivel dintre intrările sistemului** | Peste toate intrările tuturor peșterilor membre |
| **Adâncimea sistemului sub cea mai înaltă intrare** | De la cea mai înaltă intrare a oricărui membru până la cel mai de jos punct atins de ridicarea oricărui membru |
| **Înălțime deasupra celei mai joase intrări** | De la cea mai joasă intrare până la cel mai înalt punct ridicat |
| **Nivelul exurgenței din această zonă** și **Adâncime potențială până la nivelul exurgenței** | Cea mai joasă intrare de peșteră-izvor din zonă pe care o poți localiza, și cea mai înaltă intrare minus acel nivel |

Apartenența este încadrarea, ca la statisticile zonei. Trei lucruri sunt spuse pe card, nu lăsate
să fie observate:

- *„O peșteră închisă pentru tine nu adaugă nimic aici, nici măcar la numărătoare.”* O intrare sau
  o topografie pe care nu o poți localiza exact nu participă, iar pe card nu se vede că una a fost
  lăsată deoparte. Dacă niciun membru nu poate fi localizat, cardul spune că nu există ce să se
  măsoare.
- Un membru desenat în plan, fără altitudini, nu adaugă nimic la adâncime sau înălțime. Dacă
  niciunul nu are altitudini, cardul o spune și arată o liniuță, niciodată un zero.
- Dacă o adâncime provine dintr-o reducere pe baza formei unui traseu desenat, nu din vize, cardul
  numește cifrele **aproximative**. Dacă sunt încadrate mai multe peșteri decât măsoară un singur
  răspuns, spune că cifrele descriu primele dintre ele.

---

## Rețeaua de galerii

Pe pagina unei peșteri: **Rețeaua de galerii**. Numără topografia peșterii ca pe o rețea de puncte
și vize de galerie, așa cum se face în studiile carstice, ca un labirint și o peșteră ramificată să
se deosebească prin cifre.

| Grupul | Cifre |
|---|---|
| **Rețeaua așa cum a fost cartată** | **Puncte topografice din rețea**; **Vizări de galerie** (o galerie cartată de două ori se numără o dată); **Bucăți separate**; **Intersecții** (se întâlnesc trei sau mai multe galerii); **Fundături**; **Bucle independente**, adică numărul de trasee cu adevărat diferite prin rețea, zero pentru o peșteră atinsă pe un singur drum |
| **Cu tronsoanele drepte contractate** | **Intersecții și fundături**; **Legături între ele**, una pe fiecare tronson de galerie; și cele trei rapoarte **Gradul de buclare (alfa)**, **Legături per intersecție (beta)** și **Gradul de conectare (gama)**. Un beta sub unu este o peșteră ramificată; peste unu se închide în ea însăși |
| **Cum se întâlnesc galeriile** | **Numărul mediu de galerii** la o intersecție sau fundătură; împrăștierea acestei valori; și dacă intersecțiile se leagă de intersecții |
| **Tronsoane de galerie** | Numărul de **tronsoane**, câte dintre ele **se întorc de unde au plecat**, și lungimea medie, cea mai scurtă și cea mai lungă |
| **Cum se desfășoară rețeaua** | **Uniformitatea lungimilor tronsoanelor** și **a direcțiilor galeriilor** (de la zero la unu), **Sinuozitate** (lungimea unui tronson față de linia dreaptă dintre capetele lui), **Dependența de o singură intersecție** și **Galerii care se închid între ele** |

O buclă care se întoarce de unde a plecat și mai multe tronsoane între aceleași două locuri sunt
numărate fiecare drept legături proprii. Sugestia de tipar de mai jos numără buclele și
intersecțiile în același fel, deci cele două panouri nu se contrazic niciodată asupra numărului de
bucle al unei peșteri.

Panoul se încheie cu cât de completă a fost citirea:

- *„Toate vizările din fișierul de cartare s-au legat de rețeaua de puncte și nu au existat două
  puncte în aceeași poziție. Valorile de mai sus acoperă întregul fișier.”*
- Altfel, spune câte vizări nu au putut fi legate și câte puncte au fost contopite, și că valorile
  au fost calculate pe ce a rămas.
- Dacă nu este consemnat ce a lăsat deoparte citirea, spune că valorile ar putea acoperi mai puțin
  decât conține fișierul.

O peșteră poate avea mai multe cartări încărcate, iar panoul o numește pe cea din care a măsurat
(*„Măsurat din cartarea …”*). Valorile luate din două cartări diferite nu sunt aceeași
măsurătoare. Panoul nu apare pentru o peșteră pe care o poți citi, dar nu o poți localiza, pentru
că rețeaua este făcută din punctele proprii ale peșterii.

---

## Ce sugerează forma

Sub celelalte statistici de pe pagina unei peșteri: **Ce sugerează forma**. Propune unul dintre
patru tipare pe baza măsurătorilor: **Rețea ramificată vadoasă**, **Peșteră la nivelul pânzei
freatice**, **Bucle freatice** sau **Labirint unghiular**. Alte două rezultate sunt răspunsuri
cinstite, nu eșecuri: **Prea puțin măsurat pentru a încerca** și **Niciun tipar nu s-a detașat**.

Regulile citesc cât de des se închide rețeaua asupra ei însăși, cât din ea sunt funduri de sac,
dacă galeriile care pleacă dintr-o intersecție sunt deja unite, câte direcții urmează galeriile,
cât de abruptă sau orizontală este galeria, cum urcă și coboară profilul și dacă secțiunea este
mai lată decât înaltă.

> Tiparul de mai jos este citit din măsurători de reguli ale căror praguri sunt convenții alese,
> nu măsurători. Este o propunere despre această topografie, nu o afirmație despre peșteră.

De aceea **fiecare regulă este arătată, cu ce a făcut**: *S-a declanșat*, *Nu s-a declanșat* sau
*Nimic de citit*, cifra citită, tiparul la care contribuie și ponderea ei. O regulă care a privit și
a tăcut este arătată ca atare, pentru că spune la fel de mult ca una care s-a declanșat. Panoul
arată și cât a punctat fiecare tipar și câte dintre regulile evaluabile s-au declanșat.

Avertismentele sunt scrise acolo unde se aplică, printre ele: cifrele au fost aproximate din
traseul stocat, nu din marcajele proprii ale topografului; citirea topografiei a pierdut vize sau a
contopit stații; traseul nu conține altitudini, deci regulile despre profil nu au avut ce citi;
nicio stație nu a măsurat o secțiune completă; sau rețeaua de galerii nu a fost niciodată măsurată.

**Ce nu face niciodată:** nu afirmă că peștera *este* un anumit tipar și nu apare pentru o peșteră
al cărei răspuns îți este refuzat, cum este una pe care o poți citi, dar nu o poți localiza exact.

---

## Tot registrul

Ecranele pentru întregul registru de sub **Statistici** — distribuții, corelații, regiuni și
categorii — sunt descrise în
[Tot registrul, explorat](measurements-and-statistics.md#tot-registrul-explorat).

## Unde ar putea fi peșteri

Tot la **Statistici**: **Potențial speologic**, unde un model cântărește forma terenului, roca și
ce a fost înregistrat într-o hartă a terenului mai mult sau mai puțin favorabil dintr-o zonă — după
judecata autorului lui, sau numărând unde se află intrările cunoscute. Are pagina lui:
[Potențial speologic](cave-potential.md).

---

Înrudite: [Măsurători și statistici](measurements-and-statistics.md) ·
[Spațiul de lucru al hărții](map-workspace.md) · [Relief](terrain.md) ·
[Peșteri și intrări](caves-and-entrances.md) · [Potențial speologic](cave-potential.md)
