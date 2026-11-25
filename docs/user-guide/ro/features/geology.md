# Geologie: unități, măsurători structurale și stereograma

🇬🇧 [English](../../features/geology.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Fenomene de suprafață](surface-features.md) ·
[Măsurători și statistici](measurements-and-statistics.md) ·
[Protecția locațiilor](../admin/location-protection.md)

---

Două feluri de înregistrări geologice pe care aplicația le păstrează pe hartă, și ce calculează din
ele:

| Înregistrare | Ce este | Ce alimentează |
|---|---|---|
| **Unitate geologică** | Conturul unui singur fel de rocă, așa cum îl desenează o hartă geologică | Împărțirea **Geologia din interiorul zonei** din statisticile unei zone carstice |
| **Măsurătoare structurală** | O citire luată într-un punct — stratificație, o diaclază, un plan de falie — ca direcție de înclinare și înclinare | O **stereogramă** a măsurătorilor și testul **Watson U²** al galeriilor unei peșteri față de direcțiile lor |

Amândouă sunt [obiecte](surface-features.md) obișnuite: le desenezi, le editezi, le stabilești
vizibilitatea și le protejezi poziția la fel ca pentru orice alt obiect. Aici e descris doar ce au
ele în plus.

Numele tipurilor de obiect vin din instalare, nu din traducerea interfeței: implicit apar în
engleză, ca **Geological unit** și **Structural measurement**, până când un administrator le
redenumește.

---

## Unități geologice

O **unitate geologică** este un tip de obiect din categoria *Areal*: un contur (unul sau mai multe
poligoane) al unui teren format dintr-o singură clasă de rocă.

| Câmp | |
|---|---|
| **Litologie** | Clasa de rocă, aleasă dintr-o listă fixă (mai jos) |
| **Formațiune** | Text liber: numele formațiunii, așa cum îl dă harta ta geologică |
| **Vârstă** | Text liber |
| **Sursă** | Text liber: din ce hartă sau publicație vine conturul |

Etichetele câmpurilor *Formațiune*, *Vârstă* și *Sursă* sunt luate din formularul tipului și pot
apărea în engleză.

### Lista litologiilor

Lista este fixată de aplicație, nu de instalarea ta. Fiecare clasă este marcată **carstificabil**
sau nu: dacă roca se dizolvă destul de ușor ca să găzduiască peșteri.

| Litologie | Carstificabil |
|---|---|
| Calcar | da |
| Dolomit | da |
| Marmură | da |
| Gips / anhidrit | da |
| Sare gemă | da |
| Conglomerat (calcaros) | da |
| Gresie | nu |
| Șist argilos / marnă | nu |
| Granit | nu |
| Șist cristalin | nu |
| Rocă vulcanică | nu |
| Depozite neconsolidate | nu |
| Altele | nu |

**Altele nu este carstificabil, intenționat.** O statistică ce nu poate spune ce rocă este nu își
adaugă terenul la terenul în care sunt așteptate peșteri.

Un administrator poate edita formularul oricărui tip de obiect, din
[Vocabularele clubului](../admin/vocabularies.md), inclusiv al acestuia. Asta schimbă ce oferă formularul, dar
**nu** schimbă ce clase contează drept carstificabile. O unitate cu o valoare din afara listei apare
cu valoarea așa cum a fost salvată, marcată **în afara vocabularului**, și nu este socotită niciodată
teren carstificabil. O unitate fără litologie apare ca **Neclasificat**.

### Litologia nu este tipul de rocă al peșterii

Fișa unei peșteri are propriul câmp **Tip de rocă** (la *Geologie*). Acela este ce a declarat cineva
despre peșteră; o unitate geologică este ce spune o hartă despre teren. Cele două sunt ținute
separat și niciunul nu se completează din celălalt. Statisticile zonei carstice le arată pe
amândouă: tipurile de rocă declarate pe peșterile zonei și geologia cartată sub zonă.

### Pe hartă

Fiecare unitate este colorată după litologia ei. Clasele carstificabile primesc nuanțe de albastru,
violet și turcoaz și au conturul mai gros; celelalte primesc nuanțe de pământ și gri, așa că terenul
carstificabil iese în evidență ca o singură familie înainte de a deosebi vreo clasă anume. O unitate
fără litologie rămâne necolorată, cu un contur gri întrerupt, iar una a cărei valoare nu este în
listă are un contur magenta punctat — a doua este o înregistrare care trebuie corectată. Cât timp pe
hartă sunt încărcate unități, panoul de straturi arată o legendă, **Unități geologice**, cu clasele
desenate.

O unitate este [protejată](../admin/location-protection.md) ca orice alt contur: cine nu are voie
să-i vadă conturul exact nu primește deloc conturul pe hartă — un contur nu poate fi aliniat la o
grilă așa cum se aliniază un punct.

### Importul unei hărți geologice

O hartă geologică deja digitală — shapefile, GeoPackage, GeoJSON sau KML cu poligoane și o coloană
de tip de rocă — intră prin [importul vectorial](geodata.md#importuri). În revizuire:

1. La **Contururi (zone)**, alege **Importă fiecare ca obiect de tip zonă** și tipul **Unitate
   geologică**. Fiecare contur din fișier este atunci propus ca unitate, așa că pot fi bifate toate
   deodată.
2. La **Care câmp ce este**, indică coloana de tip de rocă a fișierului drept **Tip de rocă
   (unități geologice)**. Lăsat pe *Determină automat*, se folosește o coloană numită `lithology`,
   `litologie`, `rock` sau asemănător.
3. Cardul **Tipurile de rocă din fișier** listează fiecare valoare distinctă din acea coloană, câte
   contururi o au și în ce clasă devine. O valoare care este numele unei clase — în română sau
   engleză, fără a ține cont de majuscule și diacritice, ca `Calcar`, `LIMESTONE` sau `gresii` —
   este **Recunoscută**. Orice altă valoare (`Calcar jurasic cu silexuri`, `Fliș`) este **Fără
   corespondență** și apare prima: alege-i clasa sau las-o, iar acele unități vor fi create
   **Neclasificate**. Poți schimba și clasa unei valori recunoscute.

Nimic nu este ghicit dintr-o parte a valorii: o denumire mixtă sau cu calificative o clasifici tu,
pentru că o clasă carstificabilă greșită schimbă fiecare densitate calculată pe acel teren. Se pot
alege doar clase din listă.

---

## Geologia din interiorul unei zone carstice

Pe pagina unei **zone carstice**, a unui **masiv** sau a unui **deal**, cardul *Statistici pe zona
carstică* are o secțiune **Geologia din interiorul zonei**: conturul zonei împărțit după unitățile
geologice cartate peste el.

| Coloană | |
|---|---|
| **Litologie** | Clasa, cu eticheta **carstificabil** acolo unde este |
| **Afloriment (km²)** | Cât din contur acoperă clasa |
| **Pondere din zonă** | Aflorimentul ca parte din întregul contur |
| **Intrări** | Intrările aflate pe acel afloriment |
| **Intrări pe km²** | Acele intrări raportate la aflorimentul clasei |

Sub tabel:

| Cifră | |
|---|---|
| **Afloriment carstificabil (km²)** | Aflorimentele claselor carstificabile, adunate |
| **Intrări pe km² de afloriment carstificabil** | Intrările de pe teren carstificabil raportate doar la acel teren |
| **Teren fără unitate cartată (km²)** | Partea din contur pe care nu o acoperă nicio unitate |
| **Intrări pe teren necartat** | Intrările aflate acolo |

**De ce contează densitatea pe teren carstificabil.** O densitate pe întregul contur favorizează
zona care are mai puțină gresie în ea. Împărțind la aflorimentul carstificabil, două zone se
compară după terenul în care chiar pot fi peșteri.

### Măsurată pe hartă, nu declarată

Toate celelalte cifre de pe card numără peșterile *declarate* ca fiind în zonă. Împărțirea aceasta
este altfel: o hartă geologică nu este o ierarhie, așa că se măsoară pe hartă, după ce contururi se
suprapun. Cardul o spune:

> *Măsurată pe hartă, peste unitățile geologice și intrările pe care le poți localiza exact — de
> aceea numărul de intrări nu este totalul declarat de mai sus.*

Ultima parte este literală: o unitate al cărei contur exact nu ai voie să-l vezi nu influențează
împărțirea, iar o intrare pe care nu o poți localiza nu este numărată pe niciun teren. Doi cititori
cu drepturi diferite pot vedea cifre diferite, iar numărul de intrări nu dă, în general, totalul
declarat al zonei.

Două unități din **aceeași** clasă care se suprapun sunt unite întâi, ca terenul lor să nu fie
numărat de două ori. Două unități din clase **diferite** care se suprapun sunt o greșeală de
cartare și fiecare este numărată — aplicația nu decide care dintre ele are dreptate.

Acolo unde statisticile zonei sunt scoase ca tabel (CSV), aceeași împărțire vine cu ele, împreună cu
baza pe care a fost măsurată.

---

## Măsurători structurale

O **măsurătoare structurală** este un tip de obiect punctual: o citire luată cu busola-clinometru,
la un afloriment sau într-o peșteră.

| Câmp | |
|---|---|
| **Planul măsurat** | Stratificație · Diaclază · Plan de falie · Clivaj · Fractură · Altul — obligatoriu |
| **Direcția de înclinare (°)** | Încotro înclină planul, în sensul acelor de ceasornic de la nord, de la 0 până sub 360 — obligatoriu |
| **Înclinare (°)** | Unghiul planului sub orizontală, de la 0 la 90 — obligatoriu |
| **Notă** | Text liber |
| **Măsurat la (AAAA-LL-ZZ)** | Ziua citirii |

### Direcție și înclinare după regula mâinii drepte

Se salvează direcția de înclinare și înclinarea. Dacă ai luat citirea ca direcție a stratului și
înclinare, comută **Orientare introdusă ca** pe **Direcție și înclinare (regula mâinii drepte)** și
introdu direcția stratului:

> *Direcția stratului (°, regula mâinii drepte: planul înclină spre dreapta ei)*

Astfel, un plan cu direcția 030° care înclină spre sud-est este salvat cu direcția de înclinare
120°. Comutarea între cele două moduri șterge numărul introdus, pentru că același număr înseamnă alt
plan în fiecare mod. O direcție de înclinare de 360° este salvată ca 0°.

### Măsurători din subteran și protecția

O măsurătoare nu are nevoie de părinte: cele mai multe se iau la aflorimente. O măsurătoare luată
într-o peșteră poate fi pusă **în peșteră** (cu peștera ca părinte) și atunci moștenește
[protecția](../admin/location-protection.md) peșterii, ca orice altceva conține peștera. În rest,
poziția unei măsurători este protejată exact ca a oricărui punct.

### Pe hartă

O măsurătoare este desenată cu semnul geologic de direcție și înclinare: o bară lungă de-a lungul
direcției, o liniuță scurtă spre înclinare și valoarea înclinării alături. Semnul se rotește odată
cu harta, așa că arată mereu în direcția corectă. O măsurătoare cu orientarea incompletă este
desenată ca un punct simplu al tipului ei.

Orientarea spune cum stă un plan, nu unde se află, așa că este desenată oriunde este desenat punctul
— aliniat la grilă sau nu.

---

## Stereograma

O **stereogramă** pune multe măsurători pe același desen, ca o familie de diaclaze sau poziția
stratificației să apară ca un grup.

| Unde | Ce măsurători |
|---|---|
| Pagina unei peșteri, în **Galeriile față de structura cartată** | Măsurătorile din apropierea peșterii — implicit un kilometru, aceeași rază ca în comparația cu liniile de fractură — ca *n măsurători pe o rază de r m de peșteră* |
| Pagina oricărui contur (o zonă carstică, un masiv, o zonă de lucru…), ca **Măsurătorile structurale din această zonă** | Măsurătorile **din** acea zonă — puse în ea ca părinte sau în ceva ce conține ea. Cardul lipsește cu totul când zona nu are niciuna |

**Măsurători de tip** alege un singur fel de plan sau **toate tipurile**. Stratificația amestecată
cu diaclazele pune două întrebări diferite pe aceeași rețea; de obicei vrei un singur tip.

### Cum se citește

Descrierea aplicației:

> *Rețea echiarială, emisfera inferioară: fiecare punct este polul unui plan (dreapta
> perpendiculară pe el, luată în jos). Un plan cu înclinare mică apare lângă centru; unul abrupt
> lângă margine, de partea opusă înclinării.*

Dacă ții cursorul pe un punct, vezi ce măsurătoare este: tipul, înclinarea și direcția de
înclinare.

Umbrirea este densitatea polilor, după **metoda Kamb**: o bandă la fiecare două abateri standard,
unde măsurători împrăștiate la întâmplare ar da trei peste tot. Legenda de sub rețea dă valoarea
locului celui mai dens — un grup care dă 9 este cu șase abateri standard peste ce ar pune acolo
întâmplarea.

Două alegeri din spatele desenului, ambele obișnuite în geologia structurală:

- **Echiarială, nu echiunghiulară.** Pe o rețea echiarială, bucăți egale din sferă sunt desenate la
  aceeași mărime, așa că un grup de lângă margine nu pare mai rar decât unul de lângă centru.
- **Kamb, nu un cerc fix de 1 %.** Cercul de numărare este dimensionat după numărul de măsurători,
  ca aceeași rocă să nu pară grupată sau uniformă doar după câte citiri s-au făcut.

---

## Galeriile față de direcțiile măsurate (Watson U²)

Pe pagina unei peșteri, cardul **Galeriile față de structura cartată** compară direcțiile
galeriilor cu structura în două feluri:

| Față de | Cum |
|---|---|
| **Liniile** de fractură și de falie cartate | Rozetă cu rozetă, după lungime |
| **Măsurătorile** structurale de tipul ales | Direcțiile lor, cu testul **Watson U²** |

Al doilea răspunde la o întrebare de statistică: *pot proveni direcțiile galeriilor și direcțiile
măsurate din aceeași distribuție?* Arată:

| | |
|---|---|
| **U²** | Statistica testului: zero când cele două sunt împrăștiate la fel pe cerc |
| **Probabilitate** | Cât de probabil ar fi un U² atât de mare dacă ar proveni din aceeași distribuție |
| Un verdict | Sub 0,05: *„Este puțin probabil ca … să provină din aceeași distribuție”*; altfel *„Nimic de aici nu arată că direcțiile galeriilor diferă de direcțiile măsurate”* |
| **Direcțiile măsurătorilor** | O rozetă a direcțiilor care au intrat în test |

Cum se calculează și de ce contează pentru cum îl citești:

- **Direcțiile sunt axe.** O viză de 030° și una de 210° merg de-a lungul aceleiași galerii; un plan
  cu direcția 030° are și direcția 210°. Ambele părți sunt tratate așa.
- **Fiecare viză și fiecare măsurătoare contează o dată.** Nimic nu este ponderat după lungime.
  Cardul avertizează: *„Vizele de-a lungul aceleiași galerii nu sunt independente, deci o
  probabilitate mică spune mai puțin decât pare.”*
- **Se compară toată împrăștierea, nu doar media.** O galerie dreaptă față de direcții împrăștiate
  cu aceeași medie tot diferă.
- **O măsurătoare orizontală nu are direcție** și este lăsată deoparte, în loc să i se dea una
  oarecare.
- **Cel puțin cinci de fiecare parte.** Cu mai puține vize sau mai puține măsurători cu direcție,
  cardul spune câte sunt și nu dă nicio cifră.

Măsurătorile sunt adunate din aceeași rază ca liniile de fractură, iar **Măsurători de tip** de pe
card alege tipul de plan folosit atât de test, cât și de stereogramă.

---

## Cine ce vede

- **Doar ce poți localiza exact.** O măsurătoare, o unitate sau o intrare a cărei poziție exactă îți
  este ascunsă nu intră în niciun pol, nicio densitate, nicio numărătoare, niciun afloriment și
  niciun test. Cifrele arătate sunt ce ai voie să vezi, niciodată câte înregistrări există.
- **O peșteră protejată pe care nu o poți localiza** nu arată deloc cardul *Galeriile față de
  structura cartată*: măsurătorile din jurul ei i-ar contura poziția.
- **Doar autentificat.** Stereograma, comparația cu direcțiile și împărțirea geologică nu sunt
  oferite pe un link partajat sau pe o pagină publică.

## Ce nu face niciodată

- Nu socotește carstificabilă o litologie din afara propriei liste, orice ar spune formularul.
- Nu completează **Tipul de rocă** al unei peșteri din harta geologică și nici invers.
- Nu ghicește direcția unei măsurători orizontale.
- Nu decide care dintre două unități suprapuse, de roci diferite, are dreptate.

---

Înrudite: [Fenomene de suprafață](surface-features.md) ·
[Măsurători și statistici](measurements-and-statistics.md) ·
[Fișiere de geodate](geodata.md) ·
[Potențial speologic](cave-potential.md) ·
[Protecția locațiilor](../admin/location-protection.md) ·
[Vocabularele clubului](../admin/vocabularies.md)
