# Măsurători și statistici

🇬🇧 [English](../../features/measurements-and-statistics.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Topografii și modele](surveys-and-models.md) · [Ture](trips.md)

---

Aplicația calculează destul de multe și este neobișnuit de atentă să spună *din ce* este
calculată o cifră și când refuză cu totul să calculeze una. Refuzul este partea utilă: un gol
este o afirmație că nimic nu a spus, iar un zero ar fi o afirmație că ceva a spus.

---

## Ce măsoară o topografie

Pe pagina unei peșteri, **Statistici din cartare** — măsurate din linia topografică.

| Cifră | |
|---|---|
| **Lungime cartată** | Totalul |
| **Lungime în plan** | Proiectată la orizontală |
| **Extindere verticală** | |
| **Cea mai mare deschidere** | Cea mai mare distanță în linie dreaptă |
| **Verticalitate / Orizontalitate** | |
| **Liniaritate / Sinuozitate** | Cât de mult șerpuiesc galeriile |
| **Segmente / Trasee** | |
| **Punctul cel mai înalt / cel mai jos** | |
| **Raport lungime–adâncime** | |

Plus **sinuozitatea celor mai lungi galerii**, fiecare cu lungimea ei și cu distanța în linie
dreaptă.

### De unde vin cifrele, declarat

Fiecare set de cifre își declară baza:

- *„Măsurat din cartarea compilată, folosind marcajele topografului pentru a decide care vize
  sunt galerie. Aceasta este definiția acestor valori."*
- *„Aproximat din traseul stocat, pentru că această peșteră nu are o cartare compilată cu marcaje
  pe fiecare viză. Forma traseului ține locul marcajelor topografului."*
- *„Măsurat din cartarea „X”"* — pentru că o peșteră poate avea mai multe topografii încărcate,
  iar cifre luate din două topografii diferite nu sunt aceeași măsurătoare.

### Fără altitudini înseamnă fără cifre verticale

*„Aceste trasee nu au altitudini — planul unei peșteri nu înseamnă o peșteră
orizontală."* Fiecare cifră verticală este lăsată goală, nu afișată ca zero.

### Calculat față de declarat

Secțiunea **Morfometrie** a peșterii conține cifre tastate de cineva. Statisticile le arată pe
amândouă:

- *„n calculat; nimic scris în fișă cu care să fie comparat."*
- *„n calculat, m în fișă, o diferență de d."*

Iar când nu se potrivesc, o notă: **niciuna nu este automat cea greșită.** O morfometrie tastată
este adesea mai veche decât topografia; o topografie poate fi și ea parțială. Aplicația
semnalează dezacordul și lasă judecata în seama ta.

---

## Orientarea galeriilor

O **diagramă-roză** a direcțiilor galeriilor pe sectoare, cu nordul în sus, ponderată fie **după
lungime**, fie **după numărul de vizări**. Fiecare direcție este desenată de ambele părți ale
diagramei, pentru că o galerie care merge într-un sens merge și în celălalt. Raportează
**direcția medie**.

Alături, **înclinarea** — panta vizărilor topografice, cu axă de numărare liniară sau
logaritmică, și o **pantă medie față de orizontală** care ignoră dacă o vizare urcă sau coboară.

Două refuzuri de reținut:

- **Înclinarea nu poate fi calculată** dacă linia topografică nu poartă altitudini. Desenarea ei
  oricum ar arăta peștera drept orizontală peste tot, ceea ce este o afirmație pe care n-a
  făcut-o nimeni.
- **Nu se poate calcula nicio direcție** dacă fiecare vizare fie coboară vertical, fie urcă
  vertical, fie este marcată drept galerie deja cartată pe altă linie.

---

## Formă măsurată (morfometrie)

Pentru un obiect desenat ca un contur — o dolină, o depresiune carstică, o zonă de lucru:

**Suprafață · Perimetru · Circularitate · Axa lungă · Axa scurtă · Alungire · Direcția axei
lungi · Centru**

Două observații pe care le face chiar panoul:

- Cifrele sunt **măsurate în metri, în sistemul de coordonate de lucru al instalării**, nu în
  gradele în care este stocat conturul.
- **Direcția axei lungi este o aliniere, nu o direcție**: merge de la 0° la 180°, așa că 170° și
  350° sunt aceeași aliniere.

Dacă conturul se autointersectează, nu are suprafață sau formă măsurabilă, și ți se spune să-l
redesenezi în loc să primești o cifră.

---

## Cea mai mică distanță

Cât de aproape ajung două peșteri una de alta. Alege a doua peșteră din pagina primei.

Primești **cea mai scurtă distanță**, componentele ei **orizontală** și **verticală**, și un
**azimut**. Linia este desenată pe hartă (în plan) și în vizualizarea 3D (trecând prin rocă, la
adâncimile la care i-au fost ridicate capetele).

> Cifrele orizontală și verticală sunt **cele două componente ale aceleiași linii** — nu cea mai
> scurtă distanță pe orizontală și cea mai scurtă pe verticală, care sunt perechi diferite de
> puncte.

Refuză în trei cazuri, fiecare numit:

- **Nu ai voie să localizezi ambele peșteri.** O distanță între două peșteri le localizează pe
  fiecare pornind de la cealaltă, așa că se dă doar unui cititor care poate vedea exact unde sunt
  amândouă.
- **Cel puțin una nu are linie topografică** de măsurat.
- **Cel puțin una a fost desenată în plan, fără adâncimi înregistrate**, deci nu există o distanță
  onestă în trei dimensiuni.

---

## Roca de deasupra

Câtă rocă se află deasupra galeriilor unei peșteri, de-a lungul lor. Pe pagina peșterii.

Graficul are **distanța de-a lungul galeriei** pe orizontală și **grosimea rocii de deasupra** pe
verticală, cu roca hașurată între ele. Apasă un punct de pe curbă și locul unde a fost luată
citirea este marcat — pe hartă și în vizualizarea 3D — și numit sub grafic: distanța, grosimea și
segmentul traseului care a răspuns.

> **Distanța de-a lungul nu este un drum de la intrare.** O topografie este o rețea, nu
> un traseu. Axa este lungimea cumulată, în ordinea în care o înregistrează traseul.

**O întrerupere a curbei este un loc pentru care nimeni nu a măsurat terenul**, nu un loc în care
galeria ajunge la suprafață. Unde datele de elevație nu acoperă galeria, nu există linie — iar
cifrele de sub grafic (**Cea mai subțire**, **Cea mai groasă**, **Medie**) sunt calculate doar pe
citirile acoperite, cu numărul lor afișat, astfel încât o peșteră acoperită parțial să nu fie
raportată ca mai puțin adâncă decât este.

Spune asta direct, în loc să nu deseneze nimic, în patru cazuri diferite: peștera nu are traseu;
topografia a fost desenată în plan, fără adâncimi, deci nu există din ce să se scadă; pe această
instalare nu au fost pregătite deloc date de elevație; sau au fost, dar niciuna nu ajunge la
această peșteră. Acestea sunt probleme diferite, pentru oameni diferiți.

O peșteră lungă este eșantionată mai rar, nu răspunsă mai încet: citirile sunt distribuite la un
interval egal, niciodată mai apropiate de 2 m și niciodată mai mult de 400.

Ai nevoie de dreptul de a citi datele de elevație, pe lângă dreptul de a vedea exact unde este
peștera. Cere-l unui administrator dacă panoul nu apare.

---

## Adâncime și înălțime între intrări

Pe pagina peșterii, înaintea înălțimilor galeriilor.

O peșteră cu mai multe intrări se măsoară între ele: **cea mai înaltă** și **cea mai joasă
intrare**, **diferența de nivel** dintre ele, **adâncimea** de la cea mai înaltă intrare până la
cel mai de jos punct atins de topografie și **înălțimea** de la cea mai joasă intrare până la cel
mai înalt punct. **Nivelul exurgenței** este cea mai joasă intrare a unei peșteri-izvor din zona
care conține peștera, iar **adâncimea potențială** este cea mai înaltă intrare minus acel nivel —
cât de adâncă ar putea fi peștera dacă ar fi urmărită până unde iese apa din masiv. Dacă în fișa
peșterii este scrisă o adâncime potențială, ea apare lângă cea calculată, cu o notă când diferă.

O topografie desenată în plan nu conține altitudini, deci adâncimea și înălțimea arată o liniuță
și o propoziție care spune de ce — niciodată un zero, care ar descrie o peșteră fără adâncime.

O intrare pe care nu o poți localiza nu este folosită și nici numărată; din cifre nu poți ghici că
a fost lăsată deoparte. Panoul nu apare deloc pentru o peșteră pe care o poți citi, dar nu o poți
localiza.

---

## Altitudinile intrărilor față de teren

O altitudine înregistrată care diferă de înălțimea terenului cu zeci de metri înseamnă aproape
întotdeauna o coordonată greșită. Se vede în două locuri.

- **Pe pagina peșterii**, un rând pe intrare: altitudinea înregistrată, înălțimea dată de modelul
  de elevație sub ea, diferența și un rezultat — *concordă*, *nu concordă* (la peste 20 m
  distanță), *altitudine neînregistrată* sau *fără înălțime a terenului aici*.
- **Pe pagina unei zone**, intrările de sub ea, cele cu cea mai mare diferență primele.

Pentru o intrare **fără altitudine înregistrată**, pagina peșterii oferă **Completează din
teren**. Scrie înălțimea modelului, cu o zecime de metru, și numai acolo unde nu e înregistrat
nimic. O altitudine măsurată de cineva **nu este niciodată înlocuită de model**; ca s-o schimbi,
editează intrarea și scrie numărul pe care îl vrei. Ai nevoie de dreptul de a edita peștera.

Numărătoarea și ordinea se calculează peste intrările pe care le poți localiza și peste niciuna
alta, deci clasamentul este același cu sau fără o intrare protejată. Ai nevoie de dreptul de a citi
datele de elevație, iar fără el panoul nu apare. Nimic de aici nu este publicat sau exportat; este
doar pentru conturile autentificate.

---

## Profil de adâncime

În pagina peșterii, după adâncimea și înălțimea între intrări.

Profilul de adâncime desenează **altitudinea galeriilor în funcție de distanța de-a lungul lor**,
cu punctul cel mai înalt, cel mai jos, diferența dintre ele și lungimea galeriilor deasupra.
Distanța este lungimea galeriilor dinaintea fiecărui punct **în ordinea în care o înregistrează
releveul**, nu un traseu de la intrare, așa că o ramură care urcă din nou apare ca o creștere.
Imaginea nu conține nicio poziție.

Un releveu desenat în plan nu conține altitudini, așa că profilul arată o propoziție care spune de
ce și nicio curbă — niciodată una plată. Panoul nu apare pentru o peșteră pe care o poți citi, dar
nu o poți localiza. Nu citește date de elevație, deci nu cere dreptul respectiv.

---

## Profilul reliefului de-a lungul unei linii

Pe hartă, apasă butonul **Profil de relief** din marginea stângă (apare pentru cei care pot citi
elevația), fă clic pe punctele liniei și dublu clic pentru a termina. Profilul se deschide lângă
buton, ca înălțime în funcție de distanță, cu lungimea, urcarea și coborârea; **Închide** scoate
profilul și linia. Un profil al suprafeței de-a lungul unei linii desenate de cineva, pentru planificarea accesului
și a echipării. Serverul reeșantionează linia la puncte la distanțe egale (niciodată mai aproape de
5 m, niciodată mai mult de 400) și citește modelul de elevație în fiecare, răspunzând cu distanța,
înălțimea, urcarea și coborârea totală. Un punct pe care modelul nu îl acoperă este un **gol**,
niciodată o înălțime de zero, iar urcarea și coborârea nu se adună peste un gol.

Nu știe nimic despre peșteri: linia este doar coordonatele pe care le dai, deci nu poate fi
folosită pentru a găsi sau a urmări una. Cere dreptul de a citi datele de elevație.

---

## Intrări și doline față de relief

În pagina unei zone. Pentru o zonă cu contur: **clasa de pantă**, **direcția spre care privește
terenul** și **poziția pe relief** (adâncitură, plat sau ridicătură) pentru fiecare intrare și
dolină, fiecare clasă pusă lângă ponderea terenului zonei în aceeași clasă — desenate ca bare perechi
pentru pantă și poziție și ca o roză pentru orientare, cu un tabel dedesubt — cu **raportul** dintre
ele. Un raport peste 1 înseamnă că peșterile se află acolo mai des decât ar sugera terenul singur —
un loc de căutat în continuare; sub 1, mai rar.

Citește imaginile pe care un administrator le-a calculat deja din pagina de relief (panta în
grade, orientarea, indicele de poziție). O măsurătoare fără imaginea ei spune asta și nu arată
niciun tabel, așa că o imagine lipsă nu se citește niciodată ca „nicio peșteră pe pantă abruptă".
Orientarea se numără doar unde panta este de cel puțin 2°, pentru că terenul plat nu privește
nicăieri. Terenul zonei este eșantionat într-o grilă regulată peste contur, deci ponderile sunt
estimări, bune până la aproximativ un procent.

Se numără doar intrările și dolinele pe care le poți localiza, deci cifrele nu se schimbă când se
adaugă una protejată. Cere dreptul de a citi datele de elevație. Nimic de aici nu se publică și nu
se exportă.

---

## Distribuții carstice

Peste peșterile de pe o pagină de listă: **Distribuții**.

| Vedere | |
|---|---|
| **Lungime** | Histogramă a lungimii cartate |
| **Lungime față de adâncime** | Nor de puncte, cu o ajustare care raportează panta și R² |
| **Rang–mărime** | Cu ajustare de tip lege de putere și exponentul ei |
| **Pe tipuri** | Numărători |

Nota despre domeniu este partea importantă: *„Calculat din cele n din m peșteri de pe această
pagină care au o lungime măsurată. **Peșterile fără lungime lipsesc, nu sunt numărate ca zero.**"*

---

## Tot registrul, explorat

La **Statistici** în bara laterală, patru ecrane pun întrebări registrului însuși, nu unei
singure peșteri sau unei pagini de peșteri.

| Ecran | La ce răspunde |
|---|---|
| **Distribuții** | Cum se repartizează o măsurătoare — intervale, domeniu, percentile și două ajustări |
| **Corelații** | Cum se mișcă împreună două măsurători — panta, R² și corelația însăși |
| **Regiuni** | Câte peșteri stau sub fiecare regiune |
| **Categorii** | Cum se compară o măsurătoare între tipurile de peșteră sau între tipurile de rocă |

Primele trei acceptă aceleași restrângeri — tip de peșteră, tip de rocă, regiune — iar
**Categorii**, care este grupat chiar după tip, acceptă regiunea. Întrebarea întreagă stă în
adresă, așa că un link reproduce exact ce vedeai. Primele trei salvează ca foaie de calcul
exact ce arată în acel moment; fișierul este același răspuns, nu un al doilea.

**Categorii** desenează câte o cutie pentru fiecare grupă: *„Fiecare cutie merge de la percentila
25 la 75, cu mediana în interior și mustăți până la percentilele 10 și 90."* Nu cea mai mică și
cea mai mare valoare — acelea sunt cifrele unor peșteri anume. Fiecare grupă este listată în tabel
cu câte peșteri are și câte dintre ele înregistrează măsurătoarea, inclusiv peșterile fără tip
înregistrat, la **Neînregistrat**. O grupă măsurată pe mai puțin de șase peșteri își păstrează
numărătorile și nu are cutie — *Prea puține peșteri măsurate pentru o cutie* — pentru că
percentilele ei ar fi aproape cifrele propriilor peșteri, gata de citit. Este păstrată, nu omisă,
ca numărătorile să se adune tot la peșterile din domeniu.

Citește frazele pe care le tipăresc aceste ecrane, pentru că fac o treabă reală:

- **Un interval unit spune că este unit.** Acolo unde prea puține peșteri cad într-un interval ca
  să fie arătat singur, el este *unit cu vecinul* în loc să fie ascuns — iar axa spune care au fost
  unite, cu o numărătoare dedesubt. O bară lată este o bară unită, nu o trăsătură reală a
  distribuției.
- **O relație care lipsește este spusă, nu desenată.** Dacă sunt prea puține perechi ca să se
  ajusteze ceva, sau nu există un domeniu peste care să se ajusteze, primești o frază care spune
  care dintre acestea a fost și niciun grafic. O dreaptă prin două puncte nu este o relație.
- **Graficul de corelație desenează o ajustare și nicio peșteră.** Nicio peșteră nu este trasată
  individual; dreapta este trasată peste domeniul măsurătorii orizontale, care poate fi mai larg
  decât perechile folosite efectiv. Pagina o spune acolo unde se vede.
- **Defalcarea pe regiuni nu se adună, intenționat.** Totalul numără peșterile pe care le poți
  citi; rândurile numără peșterile pe care le poți și *localiza*. Când rândurile rămân în urmă,
  o frază spune cu cât — iar sub ce regiuni stau acele peșteri este exact ceea ce se reține. Nu
  există rând „în altă parte" și nici coloană de procente, pentru că oricare te-ar invita să
  închizi o diferență menită să rămână deschisă.
- **Numărătorile călătoresc cu cifrele.** O percentilă peste unsprezece peșteri și una peste o mie
  o sută arată identic pe un grafic, așa că fiecare ecran tipărește peste ce a fost calculată.

Sortarea se face doar după numele regiunii. Ordonarea tabelului după numărători ar dezvălui
mărimile relative tocmai ale lucrului pe care răspunsul a refuzat să îl spună.

---

## Statistici din ture

O persoană, o peșteră, un grup de speologie și o tabără își primesc fiecare totalurile:

**Ture · Persoane · Locuri · Primele vizite · Ore în subteran · Cartat · Coardă · Stații
topografice · Ture cu incident · Fotografii · Ture urmărite · Ore în subteran, din urmărire**

Citește avertismentele pe care le tipărește:

- **„Numărate peste turele pe care le poți citi."** Cineva cu alt acces vede totaluri diferite
  pentru același subiect, **și amândoi au dreptate**.
- **„Orele acoperă n din m participări în care au fost notate ora intrării și ora
  ieșirii."**
- **„Orele din urmărire sunt o a doua numărătoare … Nu se adună la orele în subteran de mai
  sus."** Apare de îndată ce o tură numărată a fost [urmărită](live-tracking.md).

**Cele două cifre de ore sunt două surse și nimeni nu le adună.** *Ore în subteran* vine din
orele de intrare și de ieșire scrise pe tură și pe oamenii ei. *Ore în subteran, din urmărire*
vine din jurnalul de urmărire: raportul *a intrat* al fiecărei persoane, pus în pereche cu
raportul *a ieșit* care i-a urmat, și peste miezul nopții dacă a durat atât. Unde o tură le are
pe amândouă, ele descriu aceleași ore — așa că suma lor nu este o cifră. O intrare pe care nu a
închis-o nicio ieșire nu adaugă **nimic**: nici zero, nici „încă în subteran". Unde niciun
jurnal nu are o ședere încheiată, caseta arată o liniuță, nu *0 h*, iar propoziția spune pe
câte participări se sprijină cifra. *Ture urmărite* numără turele care au ceva, orice, în
jurnalul lor.

Nimic nu este stocat. Orele se calculează din orele înregistrate, peste câte zile a durat efectiv
tura. O **primă vizită** este pur și simplu cea mai veche tură care a dus pe cineva undeva — așa
că tastarea unei ture mai vechi din arhivă *corectează* cifrele, în loc să lase în urmă un
indicator învechit.

**Nu orice subiect le arată pe toate douăsprezece:**

| Ale cui sunt totalurile | Ce lipsește | De ce |
|---|---|---|
| Ale unei persoane | *Persoane* | Câți oameni au fost în turele cuiva este un fapt despre cei cu care a mers, nu despre persoana însăși |
| Ale unei peșteri | *Locuri* · *Primele vizite* | Orice tură numărată pentru o peșteră a fost în acea peșteră — așa că *Locuri* ar putea arăta doar unu, iar *Primele vizite* ar repeta doar *Persoane*. O cifră care nu poate diferi de vecina ei se citește totuși ca un al doilea fapt |
| Ale unui grup de speologie, ale unei tabere | nimic | |

Oricare dintre ele poate fi **salvată ca foaie de calcul**, care poartă exact ce a purtat
ecranul — fără aceleași cifre — și spune ale cui sunt totalurile, pentru că un fișier se
redirecționează și se citește luni mai târziu.

> **Nu poți cere turele unei persoane numind-o.** Aceasta este o întrebare despre o persoană,
> asamblată din înregistrări pe care cel care întreabă s-ar putea să nu aibă niciodată voie să le
> citească. Turele proprii sunt la *Jurnale de tură → Turele mele*, calculate din cine este
> autentificat.

### Sinteza turelor

**Sinteză**, deasupra listei de ture, adună turele pe care le arată lista — ia cu ea filtrul
listei. Patru grafice: *Ture pe an*, *Cu ce scop*, *Unde s-a mers* și *Cine a fost*. Titlul
fiecărui grafic spune peste câte ture a fost desenat, iar **Filtrul curent · Toate turele**
comută între filtrul cu care ai venit și tot ce poți citi, astfel încât o vedere restrânsă
să aibă cu ce fi comparată.

Primul grafic desenează o linie peste bare: câte zone distincte atinseseră turele numărate
până la sfârșitul fiecărui an. *„Dacă urcă în continuare, se descoperă teren nou; dacă se
aplatizează sub bare care nu se aplatizează, se revine în aceleași locuri."*

> **„Nou" înseamnă nou față de turele numărate, nu față de întreaga arhivă.** O zonă în care
> ajunsese deja o tură din afara lor se numără ca nouă prima dată când ajunge acolo una dintre
> ele. Așa că restrângerea listei la un singur an pornește linia din nou de la zero, iar
> restrângerea la un singur scop le atribuie turelor de cartare teren în care o tură de
> explorare ajunsese mai întâi. Pagina spune acest lucru sub linie și numește turele la care
> se referă — aceleași pe care le numește titlul graficului.

Ca orice total de aici, și acestea sunt numărate peste turele pe care le poți citi.

---

Înrudite: [Topografii și modele](surveys-and-models.md) · [Ture](trips.md) ·
[Peșteri și intrări](caves-and-entrances.md)
