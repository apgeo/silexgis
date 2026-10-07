# Un film al unei ture urmărite

🇬🇧 [English](../../features/tracking-movie.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Urmărirea în direct și turele publicate](live-tracking.md) ·
[Topografii, poligonații și modele 3D](surveys-and-models.md) · [Ture](trips.md)

---

O tură urmărită lasă în urmă un jurnal: cine a fost raportat unde și când. **Fă un film**
transformă acel jurnal într-un film scurt — ridicarea peșterii rotindu-se încet, fiecare om un
marcaj care se mută din stație în stație așa cum a fost raportat — salvat ca fișier GIF, WebM
sau MP4 pe calculatorul dumneavoastră.

Filmul este făcut **în browserul dumneavoastră**. Nimic nu se încarcă pe server, instalarea nu
păstrează nicio copie și nu există sunet. Este oferit doar cuiva care este autentificat; paginile
publice ale unei ture publicate nu au un asemenea buton.

> **Un film este o copie a ceea ce ați putut vedea, și circulă.** Dialogul o spune deasupra
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
lui, o reluare în curs — este exact cum l-ați lăsat când dialogul se închide.

Un film se face pe **o singură ridicare**. Turele urmărite pe altă ridicare a aceleiași peșteri
nu sunt oferite în același film.

---

## Alegerea turelor

**Ture** listează turele urmărite pe această ridicare pe care aveți voie să le citiți, fiecare cu
datele ei și *Rapoarte: N*. Bifați-le pe cele pe care le arată filmul.

- **Fiecare tură își păstrează culoarea în ordinea în care le bifați**, deci debifați și bifați
  din nou pentru a schimba ordinea culorilor.
- O tură a cărei urmărire nu acoperă niciun interval de timp nu poate fi bifată: *„Nu este nimic
  de reluat"*.
- O tură bifată care nu a putut fi citită o spune sub numele ei, iar filmul nu poate fi exportat
  până când nu este debifată sau citită din nou.
- **O tură încă în desfășurare poate fi filmată.** Rândul ei spune *„Încă în desfășurare ·
  rapoarte noi de la deschidere: N"*, iar numărul se schimbă pe măsură ce alți oameni
  înregistrează rapoarte cât timp dialogul stă deschis. Filmul se termină în clipa în care apăsați
  **Exportă**, nu în clipa în care a fost deschis dialogul: jurnalul turei este citit din nou în
  acea clipă, așa că rapoartele sosite între timp sunt în fișier. Dacă jurnalul nu poate fi citit
  atunci, nu se exportă nimic și dialogul spune *„Jurnalul nu a putut fi citit"*; apăsați din nou **Exportă**.

Cu mai multe ture, **Mai multe ture** (sub *Mișcare*) decide cum împart ele filmul:

| Alegere | Ce obțineți | Ceasul arată |
|---|---|---|
| **Pe același calendar** | Turele se redau în ordinea reală a timpului, una după alta sau suprapuse, așa cum au fost | Data și ora |
| **Una lângă alta** | Toate turele pornesc în același moment, pentru comparație | *Timp scurs*, în ore și minute — sau în minute și secunde când întregul film acoperă mai puțin de o oră |

**Scurtează perioadele liniștite** scurtează orice perioadă mai lungă decât lungimea pe care o
stabiliți (30 de minute la început) în care nicio tură aleasă nu raportează nimic, astfel încât
o noapte între două zile să nu ocupe jumătate din film. Ceasul arată saltul.

---

## Previzualizarea este filmul

Ce arată previzualizarea este ce va conține fișierul: aceleași straturi, etichete și texte,
într-o casetă de forma cadrului.

- **Trageți de model** pentru a alege vederea de la care pornește filmul; dacă modelul se
  rotește, camera se rotește de acolo. **Vederea de pornire** oferă planul și patru profile, iar
  **Revino la ea** pune camera la loc.
- Cursorul, **Momentul din film**, arată orice moment al lui. **Redă previzualizarea** rulează
  filmul în timp real, cu tot cu rotirea camerei.

---

## Setările care contează

Dialogul se deschide pe un film mic și prudent: un GIF, 640 × 360, 10 cadre pe secundă, 20 de
secunde și 2 secunde de imagine fixă la sfârșit, modelul rotindu-se cu 6° pe secundă. Setările
dumneavoastră sunt **ținute minte în acest browser** pentru următorul film — toate, mai puțin
titlul pe care l-ați scris, care nu este stocat niciodată.

| Grup | Setare | Ce decide |
|---|---|---|
| **Fișier** | **Format** | GIF, WebM sau MP4 — vedeți [Ce format](#ce-format) |
| | **Dimensiunea cadrului** | Lat (16:9) de la 320 × 180 la 1920 × 1080, sau standard (4:3) de la 320 × 240 la 1024 × 768 |
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
| **Vedere** | Straturile ridicării, **Colorare**, **Cameră**, **Grosimea liniilor**, **Scara verticală** | Ce se desenează din ridicare și cum. Un strat pe care ridicarea nu îl are este gri; colorările după adâncime au nevoie de o ridicare care stă pe teren real |
| **Texte** | **Titlu**, **Ceas**, **Legendă**, **Bară de progres**, **Ultima notă**, **Mărimea textelor** | Ce se scrie peste imagine |

**Culorile marcajelor.** Sunt douăsprezece. Un film cu mai mult de douăsprezece ture colorate
după tură — sau cu mai mult de unsprezece echipe colorate după echipă — le refolosește în aceeași
ordine, iar legenda poartă atunci o linie care o spune: *„Culorile turelor se repetă"* sau
*„Culorile echipelor se repetă"*.

---

## Ce format

| Format | Alegeți-l pentru | Limite |
|---|---|---|
| **GIF** | O buclă care se deschide aproape oriunde, fără player — o conversație, un forum, un e-mail | Cel mult 800 de pixeli lățime și 600 de cadre; crește repede când modelul se rotește |
| **WebM** | Un video pe care îl redă orice browser; mic pentru calitatea lui | — |
| **MP4** | Un video pe care îl deschid aplicațiile de mesagerie, prezentările și telefoanele | — |

**Un format poate fi gri.** WebM și MP4 sunt scrise de codorul video al browserului
dumneavoastră, iar dialogul întreabă browserul ce poate scrie la dimensiunea și ritmul alese.
Când nu poate, formatul este dezactivat și spune de ce: *„Acest browser nu poate scrie MP4 la
1920 × 1080, cu 30 cadre/s."* — o dimensiune mai mică sau un ritm mai scăzut pot fi acceptate —
sau *„Acest browser nu are un codor video, așa că aici se poate face doar un GIF."*

**Mărimea este o estimare.** Sub setări dialogul spune *„Cadre: 300 · aproximativ 2,7 MB"*.
Pentru un video cifra este ținta codorului și este de obicei apropiată. Pentru un GIF este o
presupunere până când ați făcut unul: un GIF al unui model care se rotește, pe o ridicare
densă, poate ajunge la aproape dublul primei estimări. După ce un GIF a fost făcut în acest
browser, estimarea urmează cât au ieșit în realitate propriile dumneavoastră GIF-uri și spune
*„…, după ultimul GIF făcut aici"*. Peste aproximativ 10 MB dialogul avertizează că GIF-ul este
mai mare decât acceptă majoritatea aplicațiilor de mesagerie și propune să îl scurtați, să îl
micșorați sau să îl răriți, să îi scădeți calitatea, să opriți rotirea sau să faceți un video.

---

## Ce poate purta fișierul

Tot ce este în film v-a fost arătat în baza propriilor dumneavoastră drepturi; fișierul este apoi
al dumneavoastră de dat mai departe, unor oameni pe care instalarea nu i-a verificat niciodată.
Merită deci să știți ce se află în el.

| În fișier | La început | Ca să lipsească |
|---|---|---|
| Desenul ridicării și locul unde a fost raportat fiecare om | Întotdeauna | — acesta este filmul |
| **Numele oamenilor**, pe marcaje | Prenume. Două persoane care s-ar citi la fel primesc o inițială | **Etichetele speologilor**: *Inițiale* sau *Fără etichete* |
| **Când a fost raportat ultima dată fiecare om**, lângă numele lui | Oprit. Pornit, fiecare etichetă poartă ora și minutul, iar pentru un raport care nu este de azi și data | **Ora ultimului raport în etichetă** oprită |
| **Titlul**, scris peste imagine | Pornit. O tură: titlul turei. Mai multe: numele peșterii și zilele pe care se întind. Sau cuvintele pe care le scrieți în **Textul titlului** | **Titlu** oprit |
| **Legenda** — titlurile turelor sau numele echipelor lângă culorile lor | Pornită | **Legendă** oprită |
| **Ceasul** — data și ora fiecărui moment | Pornit | **Ceas** oprit, sau **Una lângă alta**, care arată timpul scurs și nicio dată. Amândouă scot data numai din ceas — nu și din etichete, dacă ora ultimului raport este pornită |
| **Numele intrărilor** | Pornite | Debifați-le sub **Vedere** |
| **Numele stațiilor**, **Comentariile stațiilor** | Oprite | — |
| **Ultima notă** — text liber scris de cineva odată cu un raport | Oprită, fiindcă *„Notele sunt text liber și pot conține detalii de siguranță"* | — |
| **Busola și afișajul de altitudine** | Oprite. Pornite, ele *„scriu altitudini și un azimut în fiecare cadru"* | — |
| **Numele fișierului** | Urmează titlul — vedeți mai jos | **Titlu** oprit |

O poziție care vă este ascunsă nu desenează niciun marcaj în film, din același motiv pentru care
nu desenează niciunul pe panoul ridicării din tură.

### Numele fișierului

Numele este arătat lângă butonul **Exportă** înainte să se facă ceva — *„Fișierul se va salva ca
silexgis-…"* — pentru că un nume ajunge mai departe decât imaginea: se vede într-o conversație
înainte ca filmul să fie măcar deschis.

| Textul „Titlu" | Fișierul se numește |
|---|---|
| Pornit, cu propriul **Textul titlului** | `silexgis-<cuvintele dumneavoastră>-<data>` |
| Pornit, o tură, fără text propriu | `silexgis-<titlul turei>-<data>` |
| Pornit, mai multe ture, fără text propriu | `silexgis-<numele peșterii>-<data>` (numele ridicării când cel al peșterii nu este cunoscut) |
| **Oprit** | `silexgis-movie-<data>` — nimic despre care tură sau care peșteră |

Cuvintele sunt scrise cu litere mici, fără diacritice, cu cratime în locul a tot ce nu este
literă sau cifră, și tăiate la 60 de caractere. Data este ziua în care a fost făcut fișierul, pe
calendarul dumneavoastră. Fișierul este salvat exact sub numele arătat când ați apăsat
**Exportă**.

### Un film care spune mai puțin

Opriți **Titlu**, puneți **Etichetele speologilor** pe *Inițiale* sau *Fără etichete*, opriți
**Ora ultimului raport în etichetă**, opriți **Legendă**, alegeți **Una lângă alta** sau opriți
**Ceas** și debifați **Numele intrărilor**.
Fișierul se numește atunci `silexgis-movie-<data>` și arată marcaje care se mișcă printr-o
ridicare fără nume. Forma ridicării rămâne — un film nu poate ascunde peștera al cărei film este.

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

---

## Anularea

| Apăsați | Cât rulează un export | Fără niciun export în curs |
|---|---|---|
| **Anulează exportul** | Se oprește pe loc. Nu se salvează nimic, nimic nu este raportat ca eroare, iar dialogul rămâne deschis cu previzualizarea lui | (butonul este **Închide**) |
| **Escape**, sau **X**-ul din colț | Întreabă mai întâi: *„Oprești exportul filmului?"* — **Oprește și închide** aruncă cadrele desenate până atunci și închide dialogul; **Continuă exportul** merge mai departe. Escape încă o dată, sau Enter, continuă exportul | Închide dialogul |
| Un clic lângă dialog | Nimic | Nimic |

Părăsirea paginii sau închiderea filei încheie și ea un export; nu se salvează nimic.

---

## Când ceva este refuzat

| Vedeți | De ce |
|---|---|
| *„Nicio tură nu a fost urmărită încă pe acest model."* | Nicio tură pe care aveți voie să o citiți nu a fost urmărită pe această ridicare |
| *„Nu este nimic de reluat"* pe rândul unei ture | Urmărirea ei nu acoperă niciun interval de timp |
| *„Jurnalul nu a putut fi citit"* | Jurnalul unei ture alese s-a întors incomplet; filmul refuză un jurnal parțial, la fel ca reluarea din tură. Debifați tura, sau închideți și încercați din nou |
| Un format gri | Browserul dumneavoastră nu îl poate scrie la această dimensiune și la acest ritm — vedeți [Ce format](#ce-format) |
| *„Filmul nu a putut fi făcut."* | Codorul a eșuat; linia de sub mesaj spune în ce etapă. Încercați o dimensiune mai mică sau alt format |

---

Înrudite: [Urmărirea în direct și turele publicate](live-tracking.md) ·
[Topografii, poligonații și modele 3D](surveys-and-models.md) · [Ture](trips.md) ·
[Protecția locațiilor](../admin/location-protection.md)
