# Un film al unei ture urmărite

🇬🇧 [English](../../features/tracking-movie.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Urmărirea în direct și turele publicate](live-tracking.md) ·
[Topografii, drumuiri și modele 3D](surveys-and-models.md) · [Ture](trips.md)

---

O tură urmărită lasă în urmă un jurnal: cine a fost raportat unde și când. **Fă un film**
transformă acel jurnal într-un film scurt — ridicarea peșterii rotindu-se încet, fiecare om un
marcaj care se mută din stație în stație așa cum a fost raportat — salvat ca fișier GIF, WebM
sau MP4 pe calculatorul tău.

Filmul este făcut **în browserul tău**. Nimic nu se încarcă pe server, instalarea nu
păstrează nicio copie și nu există sunet. Este oferit doar cuiva care este autentificat; paginile
publice ale unei ture publicate nu au un asemenea buton.

> **Un film este o copie a ceea ce ai putut vedea, și circulă.** Dialogul o spune deasupra
> butoanelor sale: *„Filmul arată topografia peșterii și unde a fost echipa. Trimite-l doar celor
> care au voie să le vadă pe amândouă."* Fișierul nu poartă nicio protecție proprie — nici a
> peșterii, nici a turei. [Ce poate purta fișierul](#ce-poate-purta-fișierul) spune ce se află în
> el și cum se face unul care spune mai puțin.

---

## Deschiderea

| De unde | Ce se deschide |
|---|---|
| Fila **Urmărire** a unei ture, butonul **Fă un film** de pe panoul ridicării | Dialogul pentru ridicarea pe care este urmărită tura, cu acea tură deja bifată |
| Pagina unei peșteri, butonul cu cameră de pe rândul unei ridicări, sub **Modele topo 3D** | Dialogul pentru acea ridicare, fără nicio tură bifată încă |

Dialogul are **previzualizarea** într-o parte și setările în cealaltă. Previzualizarea este
desenată de un vizualizator propriu, așa că panoul ridicării de dedesubt — marcajele lui, camera
lui, o reluare în curs — este exact cum l-ai lăsat când dialogul se închide.

Un film se face pe **o singură ridicare**. Turele urmărite pe altă ridicare a aceleiași peșteri
nu sunt oferite în același film.

**Butonul răspunde întotdeauna.** Realizarea filmului se aduce doar când o cere cineva, așa că pe o
conexiune lentă dialogul spune întâi *Se deschide realizarea filmului…*. Dacă nu se poate deschide,
un dialog separat spune *Realizarea filmului nu s-a putut deschide.*, cu motivul dat de browser —
sau, când aplicația a fost actualizată după ce pagina a fost deschisă, că pagina trebuie
reîncărcată ca să folosească versiunea nouă. Pagina turei din spate rămâne cum era, iar o nouă
apăsare pe buton încearcă din nou.

**Previzualizarea spune cât a sosit din model.** Citește același fișier de ridicare ca
vizualizatorul aflat deja pe pagină — o singură descărcare, nu două — și îl numără pe măsură ce
sosește: *Se încarcă modelul pentru previzualizare… 12.4 MB din 31.0 MB*. Dacă timp de 45 de
secunde nu mai sosește nimic, descărcarea este abandonată, cu un mesaj care spune asta și un buton
**Încearcă din nou**.

---

## Alegerea turelor

**Ture** listează turele urmărite pe această ridicare pe care ai voie să le citești, fiecare cu
datele ei și *Rapoarte: N*. Bifează-le pe cele pe care le arată filmul.

- **Fiecare tură își păstrează culoarea în ordinea în care le bifezi**, deci debifează și bifează
  din nou pentru a schimba ordinea culorilor.
- O tură a cărei urmărire nu acoperă niciun interval de timp nu poate fi bifată: *„Nu este nimic
  de reluat"*.
- O tură bifată care nu a putut fi citită o spune sub numele ei, iar filmul nu poate fi exportat
  până când nu este debifată sau citită din nou.
- **O tură încă în desfășurare poate fi filmată.** Rândul ei spune *„Încă în desfășurare ·
  rapoarte noi de la deschidere: N"*, iar numărul se schimbă pe măsură ce alți oameni
  înregistrează rapoarte cât timp dialogul stă deschis. Filmul se termină în clipa în care apeși
  **Exportă**, nu în clipa în care a fost deschis dialogul: jurnalul turei este citit din nou în
  acea clipă, așa că rapoartele sosite între timp sunt în fișier. Dacă jurnalul nu poate fi citit
  atunci, nu se exportă nimic și dialogul spune *„Jurnalul nu a putut fi citit"*; apasă din nou **Exportă**.

### Cine apare

Sub fiecare tură bifată, după ce a fost citită, un rând spune *„Cine apare: N din M"*. Apasă-l
pentru a desface lista participanților turei, toți bifați, și debifează oamenii pe care acest film
nu trebuie să îi arate.

- Cine este debifat **nu are marcaj și nici traseu**, iar **o notă spusă de acea persoană nu este
  scrisă peste imagine** — ultima notă arătată este atunci ultima spusă de cineva care este în film.
- **Nici rapoartele acelei persoane nu dau ritmul filmului.** Cu **Scurtează perioadele
  liniștite** pornit, ceasul încetinește în jurul fiecărui raport și sare peste orele dintre ele;
  o face numai la rapoartele celor care apar, așa că nimic din fișier nu marchează clipa în care
  a fost raportat cineva lăsat deoparte. De aceea, scoaterea cuiva poate schimba cât durează
  fiecare perioadă și factorul de accelerare.
- **Cine nu mai este printre participanții turei** — scos din tură după ce i-au fost înregistrate
  rapoartele — nu este în listă, nu are marcaj, iar o notă spusă de acea persoană nu este scrisă
  niciodată peste imagine.
- Nimeni altcineva nu se schimbă: turele și echipele își păstrează culorile și rândurile din
  legendă. O etichetă nu se lungește niciodată din cauza cuiva care nu este arătat — două persoane
  pe nume Ana sunt deosebite printr-o inițială numai cât timp amândouă sunt în film.
- Aceeași persoană aflată în două ture se bifează separat la fiecare tură.
- **Alegerea este numai pentru acest film.** Nu este reținută odată cu setările și nu este
  păstrată nicăieri: închide dialogul, sau debifează tura și bifeaz-o din nou, și toată lumea
  este înapoi.

Când sunt bifate mai multe ture, **Mai multe ture** apare sub lista turelor și decide cum împart
ele filmul:

| Alegere | Ce obții | Ceasul arată |
|---|---|---|
| **Pe același calendar** | Turele se redau în ordinea reală a timpului, una după alta sau suprapuse, așa cum au fost | Data și ora |
| **Una lângă alta** | Toate turele pornesc în același moment, pentru comparație | *Timp scurs*, în ore și minute — sau în minute și secunde când întregul film acoperă mai puțin de o oră |

Fiecare film pornește **Pe același calendar**: *Una lângă alta* este o alegere pentru turele din
fața ta și nu este reținută pentru filmul următor.

Pe același calendar, **echipa fiecărei ture apare pe model cât timp este în subteran** și dispare
când iese ultimul membru — cine a ieșit în timp ce alții sunt încă înăuntru rămâne, în gri, până
atunci. Un film cu o singură tură se încheie în continuare cu echipa acolo unde a ieșit.

**O tură consemnată ulterior se redă pe durata rapoartelor ei.** Când urmărirea unei ture a fost
pornită după primul ei raport sau închisă la mai mult de șase ore după ultimul — un jurnal
introdus de pe hârtie a doua zi, ori jurnalul unei expediții importat după câteva săptămâni —
filmul redă tura pe intervalul acoperit de rapoarte, nu până în momentul închiderii urmăririi.

**Scurtează perioadele liniștite** sare înainte acolo unde nicio tură aleasă nu raportează nimic:
imaginea nu stă nemișcată mai mult decât așteptarea stabilită (o secundă la început) și nu redă
niciodată mai mult de o jumătate de oră din timpul turelor fără să se întâmple nimic — astfel încât
o noapte între două zile, sau zilele dintre două ture, să nu ocupe filmul. Ceasul arată saltul. Un
film cu multe ture trebuie totuși să-și împartă lungimea între ele: turele unei luni în douăzeci
de secunde dau fiecărei ture mult sub o secundă, așa că dă unui asemenea film o **Durată (secunde)** mai
mare.

---

## Previzualizarea este filmul

Ce arată previzualizarea este ce va conține fișierul: aceleași straturi, etichete și texte,
într-o casetă de forma cadrului.

- **Trage de model** pentru a alege vederea de la care pornește filmul; dacă modelul se
  rotește, camera se rotește de acolo. **Vederea de pornire** oferă planul și patru profile, iar
  **Revino la ea** pune camera la loc.
- Cursorul, **Momentul din film**, arată orice moment al lui. **Redă previzualizarea** rulează
  filmul în timp real, cu tot cu rotirea camerei.
- **Salvează acest moment ca imagine** — butonul cu aparat foto de lângă redare — salvează
  momentul pe care stă cursorul ca PNG, la dimensiunea cadrului filmului. Este cadrul pe care îl
  are filmul în acel moment: aceleași persoane, etichete, texte și legendă, cu camera rotită cât
  s-a rotit filmul până atunci. Nu se face niciun film pentru ea și nu se încarcă nimic. Vezi
  [Numele fișierului](#numele-fișierului) pentru cum se numește.

**Taste.** Fă clic pe previzualizare, sau ajungi la ea ori la cursor cu Tab — un contur în
jurul previzualizării arată când tastele sunt ale ei — și:

| Tastă | Ce face |
|---|---|
| **Spațiu** | Redă previzualizarea și o oprește |
| **Home**, **End** | Duc la primul și la ultimul moment al filmului |
| **← →** pe cursor | Un cadru înapoi sau înainte |

Tastele nu fac nimic cât rulează un export. Pe un buton, Spațiu apasă acel buton, iar în caseta
titlului scrie un spațiu, ca peste tot.

---

## Setările care contează

Dialogul se deschide pe un film mic și prudent: un GIF, 640 × 360, 10 cadre pe secundă, 20 de
secunde și 2 secunde de imagine fixă la sfârșit, modelul rotindu-se cu 6° pe secundă. Setările
tale sunt **ținute minte în acest browser** pentru următorul film — toate, mai puțin
titlul pe care l-ai scris, care nu este stocat niciodată.

**Presetările** stau deasupra grupurilor, pentru un fișier făcut dintr-o apăsare:

| Presetare | Ce stabilește |
|---|---|
| **Pentru chat** | Un GIF, 480 × 270, 10 cadre pe secundă, 15 secunde, calitate medie — destul de mic pentru a fi trimis într-un mesaj |
| **Video HD** | 1280 × 720, 30 de cadre pe secundă, calitate ridicată, ca **MP4** unde browserul îl poate scrie și ca **WebM** unde nu. Unde nu poate scrie niciunul, nu se schimbă nimic și dialogul o spune |
| **Reels / TikTok** | Video vertical pe tot ecranul pentru Reels pe Instagram și Facebook, TikTok, YouTube Shorts și Stories: un **MP4**, 1080 × 1920 (9:16), 30 de cadre pe secundă, 30 de secunde, calitate ridicată. Titlul, ceasul, legenda și bara de progres stau în afara benzilor pe care aceste aplicații le acoperă cu butoanele și textele lor |
| **Flux Instagram** | O postare portret pentru fluxul Instagram și Facebook: un **MP4**, 1080 × 1350 (4:5), 30 de cadre pe secundă, 30 de secunde |
| **Postare pătrată** | O postare pătrată pentru Instagram, Facebook sau X: un **MP4**, 1080 × 1080, 30 de cadre pe secundă, 30 de secunde |

Cele trei presetări pentru rețele sociale scriu **doar MP4**, pentru că Instagram nu primește WebM: unde browserul nu poate scrie un MP4 de dimensiunea aceea, nu se schimbă nimic și dialogul o spune.

O presetare schimbă **doar fișierul** — setările grupului *Fișier*. Cine este etichetat și cum, ce
se vede din model și ce texte se scriu sunt ale tale și nu sunt atinse niciodată de o
presetare. Turele bifate și persoanele lăsate deoparte nu sunt setări și rămân, atât după o
presetare, cât și după o revenire la valorile implicite.

| Grup | Setare | Ce decide |
|---|---|---|
| **Fișier** | **Format** | GIF, WebM sau MP4 — vezi [Ce format](#ce-format) |
| | **Dimensiunea cadrului** | Lat (16:9) de la 320 × 180 la 1920 × 1080; vertical (9:16, Reels, TikTok, Shorts, Stories) de la 360 × 640 la 1080 × 1920; pătrat (1:1) de la 480 × 480 la 1080 × 1080; portret 4:5 (fluxul Instagram și Facebook) de la 640 × 800 la 1080 × 1350 și 3:4 de la 600 × 800 la 1080 × 1440; sau standard (4:3) de la 320 × 240 la 1024 × 768. Un GIF are cel mult 800 de pixeli lățime |
| | **Cadre pe secundă** | De la 10 la 30 pentru un video; un GIF păstrează exact doar 10, 12,5, 20 și 25 |
| | **Durată (secunde)** | Cât durează reluarea, între 1 și 300 de secunde, oricât au durat turele în realitate |
| | **Imagine fixă la sfârșit (secunde)** | Ultimul cadru ținut pe loc, ca ochiul să se oprească pe locul unde a ajuns fiecare |
| | **Calitate** | Pentru un GIF, culorile din paletă (64, 128 sau 256); pentru un video, biții cheltuiți pe fiecare cadru |
| **Mișcare** | **Rotește modelul** | Cu o viteză dată, sau exact o rotație completă pe durata filmului; în sensul acelor de ceasornic sau invers. Oprit, camera stă pe loc |
| | **Alunecarea marcajului (secunde)** | Cât îi ia unui marcaj să ajungă la stația următoare |
| **Speologi** | **Etichetele speologilor** | Prenume, nume complet, inițiale sau fără etichete |
| | **Ora ultimului raport în etichetă** | Adaugă în eticheta fiecărui om ora ultimei lui poziții raportate — și data, când aceasta nu este cea de azi, adică de fiecare dată la o tură încheiată |
| | **Culoarea marcajelor** | Automat (după echipă pentru o tură, după tură pentru mai multe), după tură, după echipă sau o singură culoare |
| | **Arată speologii care au ieșit** | Dacă cineva raportat ca ieșit rămâne în imagine |
| | **Traseul parcurs** | O linie în urma fiecărui marcaj |
| **Vedere** | Straturile ridicării, **Relieful de deasupra peșterii**, **Colorare**, **Cameră**, **Grosimea liniilor**, **Scara verticală** | Ce se desenează din ridicare și cum. Un strat pe care ridicarea nu îl are este gri; colorările după adâncime au nevoie de o ridicare care stă pe relief real |
| **Texte** | **Titlu**, **Ceas**, **Factor de accelerare**, **Legendă**, **Bară de progres**, **Ultima notă**, **Mărimea textelor** | Ce se scrie peste imagine |
| **Fotografii** | **Fotografiile de pe momentele turelor** | *Nu apar*, *Într-un colț* sau *Pe tot cadrul*. Fotografiile sunt cele prinse de un moment al unei ture bifate, din jurnalul ei de urmărire; fiecare apare când filmul ajunge la momentul ei. Sub alegere, dialogul spune câte sunt, câte încă se încarcă și câte nu au putut fi încărcate și vor lipsi |
| | **Secunde pentru fiecare** | Secunde din film, nu din tură — echipa continuă să se miște dedesubt. Fotografiile care s-ar suprapune apar una după alta, în ordinea în care au fost făcute; cea pe care ar tăia-o sfârșitul filmului este scurtată |
| | **Apare și dispare treptat**, **Colț**, **Legendele lor** | Dacă o fotografie apare și dispare treptat; în ce colț (dreapta jos este cel în care nu stă niciun text); dacă legenda ei este scrisă de-a lungul marginii de jos |

**Revino la valorile implicite** stă sub presetări și nu este una dintre ele: readuce *fiecare*
setare la ce arată o primă deschidere — textele, etichetele și vederea, nu doar fișierul, și
titlul pe care l-ai scris. Dacă ai oprit **Titlul**, el revine, și odată cu el fișierul numit
după tură sau după peșteră; aceleași valori sunt cele pe care se deschide filmul următor. Turele
bifate și persoanele lăsate deoparte rămân cum sunt.

**Factorul de accelerare.** După ceas, filmul spune de câte ori mai repede decât în realitate
rulează — *„12 sept. 2026, 14:05 · ×240"* — astfel încât cine primește fișierul știe că un minut
din el înseamnă patru ore din tură. Cifra este rotunjită (la cea mai apropiată zece de la o sută
în sus, la un număr întreg de la zece în sus) și este ritmul a ceea ce se vede: acolo unde
**Scurtează perioadele liniștite** a scurtat o perioadă, ceasul sare, iar saltul nu este socotit
drept viteză. Un film care rulează aproximativ în ritmul în care s-au petrecut lucrurile nu poartă
nicio cifră. Face parte din textul ceasului, deci oprirea **Ceasului** o scoate și pe ea. Pe un
cadru mic sau îngust, sau cu o **Mărime a textelor** mare, ceasul și cifra pot să nu încapă
amândouă; cifra este atunci lăsată deoparte întreagă, nu tăiată, iar previzualizarea o arată.

**Culorile marcajelor.** Sunt douăsprezece. Un film cu mai mult de douăsprezece ture colorate
după tură — sau cu mai mult de unsprezece echipe colorate după echipă — le refolosește în aceeași
ordine, iar legenda poartă atunci o linie care o spune: *„Culorile turelor se repetă"* sau
*„Culorile echipelor se repetă"*.

**Relieful de deasupra peșterii.** Unele fișiere de ridicare conțin, pe lângă galerii, și
suprafața de deasupra peșterii. **Relieful de deasupra peșterii**, sub *Vedere*, desenează acea
suprafață în film. **Este oprit la început și rămâne oprit până îl pornești**: galeriile desenate
singure sunt o formă, dar o peșteră desenată sub dealurile ei poate fi așezată pe hartă de oricine
primește fișierul — același motiv pentru care busola și afișajul de altitudine pornesc oprite.
Comutatorul este oferit numai pentru o ridicare al cărei fișier își conține propriul relief;
pentru oricare alta este gri și spune *„Fișierul acestui model nu conține relief propriu, deci
nu este nimic de desenat."* Planul neted pe care vizualizatorul 3D îl poate așeza sub o
ridicare nu este relief și nu este desenat niciodată într-un film. Ca restul vederii, alegerea
este ținută minte în acest browser, iar o presetare nu o atinge.

---

## Ce format

| Format | Alege-l pentru | Limite |
|---|---|---|
| **GIF** | O buclă care se deschide aproape oriunde, fără player — o conversație, un forum, un e-mail | Cel mult 800 de pixeli lățime și 600 de cadre; crește repede când modelul se rotește |
| **WebM** | Un video pe care îl redă orice browser; mic pentru calitatea lui | — |
| **MP4** | Un video pe care îl deschid aplicațiile de mesagerie, prezentările și telefoanele | — |

**Un format poate fi gri.** WebM și MP4 sunt scrise de codorul video al browserului
tău, iar dialogul întreabă browserul ce poate scrie la dimensiunea și ritmul alese.
Când nu poate, formatul este dezactivat și spune de ce: *„Acest browser nu poate scrie MP4 la
1920 × 1080, cu 30 cadre/s."* — o dimensiune mai mică sau un ritm mai scăzut pot fi acceptate —
sau *„Acest browser nu are un codor video, așa că aici se poate face doar un GIF."* Propoziția
aceasta este tot ce se întâmplă: nimic nu este raportat ca eroare, iar celelalte formate rămân
cum erau.

### În ce browsere se poate face un film

**GIF-ul este oferit în orice browser.** Îl scrie aplicația însăși și nu cere browserului
decât vederea 3D cu care este desenată previzualizarea. **Cele două formate video depind de
browser**, iar dialogul nu se ia după numele browserului: îl întreabă pe cel pe care îl
folosești ce poate scrie codorul lui, de fiecare dată când se schimbă dimensiunea sau ritmul,
și oferă exact atât.

| Browser | Ce primești |
|---|---|
| **Chrome, Edge și celelalte browsere construite pe Chromium** | GIF, WebM și MP4 — acceptate la toate dimensiunile încercate, de la 320 × 180 cu 10 cadre/s la 1920 × 1080 cu 30. Aici este testat filmul de la un capăt la altul, cu fișierele redate chiar de browser |
| **Un browser al cărui codor video nu acceptă nimic** — varianta de Linux a motorului WebKit încercată aici este una: are un codor și a refuzat ambele formate la orice dimensiune, de la 320 × 180 la 1920 × 1080 | GIF. WebM și MP4 sunt gri, fiecare cu propoziția lui — *„Acest browser nu poate scrie WebM la 640 × 360, cu 10 cadre/s."* — iar alegerea altei dimensiuni schimbă cifrele din propoziție, nu răspunsul |
| **Un browser fără niciun codor video** | GIF, și singura propoziție *„Acest browser nu are un codor video, așa că aici se poate face doar un GIF."* |
| **Safari pe Mac sau pe iPhone, și Firefox** | Neîncercate de acest proiect. Li se pune aceeași întrebare ca oricărui alt browser: ce răspund că pot scrie este oferit, iar ce nu pot este gri, cu motivul lui |

Așadar, dacă ai nevoie de un video și formatul lui este gri la orice dimensiune, cauza este
browserul și un film mai mic nu schimbă nimic: fă video-ul într-un browser construit pe
Chromium, sau trimite GIF-ul.

**Mărimea este o estimare.** Sub setări dialogul spune *„Cadre: 300 · aproximativ 2,7 MB"*.
Pentru un video cifra este ținta codorului și este de obicei apropiată. Pentru un GIF este o
presupunere până când ai făcut unul: un GIF al unui model care se rotește, pe o ridicare
densă, poate ajunge la aproape dublul primei estimări. După ce un GIF a fost făcut în acest
browser, estimarea urmează cât au ieșit în realitate propriile tale GIF-uri și spune
*„…, după ultimul GIF făcut aici"*. Peste aproximativ 10 MB dialogul avertizează că GIF-ul este
mai mare decât acceptă majoritatea aplicațiilor de mesagerie și propune să îl scurtezi, să îl
micșorezi sau să îl rărești, să îi scazi calitatea, să oprești rotirea sau să faci un video.

---

## Ce poate purta fișierul

Tot ce este în film ți-a fost arătat în baza propriilor tale drepturi; fișierul este apoi
al tău de dat mai departe, unor oameni pe care instalarea nu i-a verificat niciodată.
Merită deci să știi ce se află în el.

| În fișier | La început | Ca să lipsească |
|---|---|---|
| Desenul ridicării și locul unde a fost raportat fiecare om | Întotdeauna | — acesta este filmul |
| **Numele oamenilor**, pe marcaje | Prenume. Două persoane care s-ar citi la fel primesc o inițială | **Etichetele speologilor**: *Inițiale* sau *Fără etichete* |
| **Când a fost raportat ultima dată fiecare om**, lângă numele lui | Oprit. Pornit, fiecare etichetă poartă ora și minutul, iar pentru un raport care nu este de azi și data | **Ora ultimului raport în etichetă** oprită |
| **Titlul**, scris peste imagine | Pornit. O tură: titlul turei. Mai multe: numele peșterii și zilele pe care se întind. Sau cuvintele pe care le scrii în **Textul titlului** | **Titlu** oprit |
| **Legenda** — titlurile turelor sau numele echipelor lângă culorile lor | Pornită | **Legendă** oprită |
| **Ceasul** — data și ora fiecărui moment | Pornit | **Ceas** oprit, sau **Una lângă alta**, care arată timpul scurs și nicio dată. Amândouă scot data numai din ceas — nu și din etichete, dacă ora ultimului raport este pornită |
| **Numele intrărilor** | Pornite | Debifează-le sub **Vedere** |
| **Numele stațiilor**, **Comentariile stațiilor** | Oprite | — |
| **Ultima notă** — text liber scris de cineva odată cu un raport | Oprită, fiindcă *„Notele sunt text liber și pot conține detalii de siguranță"* | — |
| **Fotografiile** prinse de momentele turelor, cu legendele lor | Oprite. Pornite, fiecare fotografie de pe un moment al unei ture bifate este desenată în cadre, mai puțin cele despre cineva pe care l-ai lăsat în afara filmului | **Fotografiile de pe momentele turelor**: *Nu apar* |
| **Busola și afișajul de altitudine** | Oprite. Pornite, ele *„scriu altitudini și un azimut în fiecare cadru"* | — |
| **Suprafața de deasupra peșterii** — dealuri, văi și locul intrărilor printre ele, acolo unde fișierul ridicării își conține relieful | Oprită. Pornită, peștera este desenată sub propriul ei peisaj, pe care cineva care cunoaște zona îl poate recunoaște | — lasă **Relieful de deasupra peșterii** oprit. Alegerea este ținută minte, deci privește previzualizarea înainte de a trimite un film făcut după unul care îl avea pornit |
| **Numele fișierului** | Urmează titlul — vezi mai jos | **Titlu** oprit |
| **O persoană** — marcajul ei, traseul ei, o notă spusă de ea | Toți participanții fiecărei ture bifate | Debifeaz-o sub [Cine apare](#cine-apare) |

O poziție care îți este ascunsă nu desenează niciun marcaj în film, din același motiv pentru care
nu desenează niciunul pe panoul ridicării din tură.

### Numele fișierului

Numele este arătat lângă butonul **Exportă** înainte să se facă ceva — *„Fișierul se va salva ca
silexgis-…"* — pentru că un nume ajunge mai departe decât imaginea: se vede într-o conversație
înainte ca filmul să fie măcar deschis.

| Textul „Titlu" | Fișierul se numește |
|---|---|
| Pornit, cu propriul **Textul titlului** | `silexgis-<cuvintele tale>-<data>` |
| Pornit, o tură, fără text propriu | `silexgis-<titlul turei>-<data>` |
| Pornit, mai multe ture, fără text propriu | `silexgis-<numele peșterii>-<data>` (numele ridicării când cel al peșterii nu este cunoscut) |
| **Oprit** | `silexgis-movie-<data>` — nimic despre care tură sau care peșteră |

Cuvintele sunt scrise cu litere mici, fără diacritice, cu cratime în locul a tot ce nu este
literă sau cifră, și tăiate la 60 de caractere. Data este ziua în care a fost făcut fișierul, pe
calendarul tău. Fișierul este salvat exact sub numele arătat când ai apăsat
**Exportă**.

O imagine salvată din previzualizare este numită după aceeași regulă, cu terminația `.png` — așa
că, având textul „Titlu" oprit, nici ea nu spune nimic despre care tură sau care peșteră.

### Un film care spune mai puțin

Oprește **Titlu**, pune **Etichetele speologilor** pe *Inițiale* sau *Fără etichete*, oprește
**Ora ultimului raport în etichetă**, oprește **Legendă**, alege **Una lângă alta** sau oprește
**Ceas**, debifează **Numele intrărilor** și lasă **Relieful de deasupra peșterii** oprit. Sub
[Cine apare](#cine-apare), debifează pe oricine nu trebuie să apară deloc.
Fișierul se numește atunci `silexgis-movie-<data>` și arată marcaje care se mișcă printr-o
ridicare fără nume. Forma ridicării rămâne — un film nu poate ascunde peștera al cărei film este.
O imagine salvată dintr-o astfel de previzualizare spune la fel de puțin și se numește la fel.

---

## Exportul și cât durează

**Exportă GIF** (sau WebM, sau MP4) pornește lucrul. Setările sunt blocate cât rulează, iar
dialogul spune cât a ajuns: mai întâi *„Se aleg culorile"* pentru un GIF, apoi *„Cadrul 37 din
220"*, apoi *„Se scrie fișierul…"*, cu timpul scurs și *„încă aproximativ …"*.

**Fiecare cadru este desenat din nou, unul câte unul**, și apoi codat. Pe un calculator fără
placă grafică browserul desenează ridicarea prin software, ceea ce este mai lent, iar un export
durează pe cât de multe și de mari îi sunt cadrele:

| Măsurat pe un calculator cu multe nuclee și fără placă grafică, douăzeci de ture pe o ridicare | A durat |
|---|---|
| GIF, 640 × 360, 300 de cadre (30 de secunde la 10 pe secundă) | aproximativ o jumătate de minut |
| WebM, 1280 × 720, 800 de cadre (32 de secunde la 25 pe secundă) | aproximativ un minut și jumătate |
| MP4, 1920 × 1080, 800 de cadre | aproximativ două minute |

Un calculator mai lent are nevoie de mai mult; cifra de urmat este *„încă aproximativ …"* a
dialogului. Înaintea unui video lung dialogul avertizează că *„exportul poate dura mult"* —
avertismentul apare pe la 1.100 de cadre la 1280 × 720 — și că o durată mai scurtă, mai puține
cadre pe secundă sau o dimensiune mai mică merg mai repede.

Când exportul se termină, browserul salvează fișierul, iar dialogul spune *„S-a salvat"* și
numele lui. Dialogul rămâne deschis, așa că același film poate fi făcut din nou în alt format.

**Un video lung este scris în fișier pe măsură ce este făcut.** De la o dimensiune estimată de
aproximativ 256 MB, într-un browser care poate face asta — astăzi Chrome, Edge și celelalte
browsere construite pe Chromium; nu Firefox și nu Safari — apăsarea pe **Exportă** întreabă mai
întâi unde să fie salvat fișierul, iar dialogul o spune dinainte, în locul numelui fișierului.
Filmul ajunge apoi în acel fișier pe măsură ce este codat, în loc să fie ținut în memorie până la
sfârșit, așa că un export lung nu rămâne fără memorie; fișierul este același, octet cu octet.
Închiderea întrebării nu pornește nimic. O anulare, sau un export care eșuează, nu lasă în urmă
un fișier scris pe jumătate — iar când numele ales era al unui fișier deja existent, acel fișier
mai vechi rămâne așa cum era. În celelalte browsere, pentru un GIF și pentru un video mai scurt
nu se schimbă nimic: filmul este făcut în memorie, iar browserul îl salvează la sfârșit.

---

## Anularea

| Apeși | Cât rulează un export | Fără niciun export în curs |
|---|---|---|
| **Anulează exportul** | Se oprește pe loc. Nu se salvează nimic, nimic nu este raportat ca eroare, iar dialogul rămâne deschis cu previzualizarea lui | (butonul este **Închide**) |
| **Escape**, sau **X**-ul din colț | Întreabă mai întâi: *„Oprești exportul filmului?"* — **Oprește și închide** aruncă cadrele desenate până atunci și închide dialogul; **Continuă exportul** merge mai departe. Escape încă o dată, sau Enter, continuă exportul | Închide dialogul |
| Un clic lângă dialog | Nimic | Nimic |

Părăsirea paginii sau închiderea filei încheie și ea un export; nu se salvează nimic.

---

## Când ceva este refuzat

| Vezi | De ce |
|---|---|
| *„Nicio tură nu a fost urmărită încă pe acest model."* | Nicio tură pe care ai voie să o citești nu a fost urmărită pe această ridicare |
| *„Nu este nimic de reluat"* pe rândul unei ture | Urmărirea ei nu acoperă niciun interval de timp |
| *„Jurnalul nu a putut fi citit"* | Jurnalul unei ture alese s-a întors incomplet; filmul refuză un jurnal parțial, la fel ca reluarea din tură. Debifează tura, sau închide și încearcă din nou |
| Un format gri | Browserul tău nu îl poate scrie la această dimensiune și la acest ritm — vezi [Ce format](#ce-format) |
| *„Filmul nu a putut fi făcut."* | Codorul a eșuat; linia de sub mesaj spune în ce etapă. Încearcă o dimensiune mai mică sau alt format. Mesajul rămâne până îl închizi din **X**-ul lui sau pornești alt export |
| *„Browserul nu a dat o suprafață de desen pe care să fie alcătuit cadrul…"* sub acel titlu, sau sub *„Imaginea nu a putut fi făcută."* | Browserul a refuzat suprafața pe care este alcătuit un cadru, de obicei pentru că a rămas fără memorie grafică. Închide alte file sau reîncarcă pagina, cum spune propoziția |

---

Înrudite: [Urmărirea în direct și turele publicate](live-tracking.md) ·
[Topografii, drumuiri și modele 3D](surveys-and-models.md) · [Ture](trips.md) ·
[Protecția locațiilor](../admin/location-protection.md)
