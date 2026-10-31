# Operațiuni de întreținere

🇬🇧 [English](../../admin/maintenance.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Încărcări](../features/uploads.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md)

---

O parte din muncă se face **când sosește un fișier**: i se citește textul, un document de birou
este paginat pentru citire, poziția unei fotografii este scoasă din datele aparatului.

Ceea ce ridică o întrebare pe care orice arhivă în creștere ajunge s-o pună: *dar cu tot ce a
sosit înainte ca acelea să funcționeze?*

Patru **operațiuni de recuperare** răspund. Fiecare parcurge rândurile existente ale instalării și
face munca ce nu s-a făcut la vremea ei.

---

> **Acestea nu au încă o pagină proprie.** Se declanșează prin interfața de programare a
> aplicației, nu dintr-un buton. Dacă nu te simți în largul tău făcând asta, pagina
> este totuși cea către care să-ți îndrepți administratorul — spune ce operațiune rezolvă ce
> simptom.

---

## Cele patru operațiuni

### Citirea textului

Citește textul fișierelor stocate pe care **nu le-a citit nimic**, sau pe care **un cititor mai
nou ar trebui să le citească din nou**.

**Rulează-o când:**

- documente încărcate înainte să observi că există căutare în text nu apar în rezultate,
- un lot de fișiere arată *„Nimic de aici nu știe încă să citească textul acestui format."*, iar instalarea a fost
  actualizată între timp,
- ai importat în masă o arhivă dintr-un dosar de pe server și vrei s-o faci căutabilă.

După aceea, starea **Text** a fiecărui document ar trebui să treacă la *Textul a fost citit*.
Fișierele care chiar nu conțin text — scanări, PDF-uri doar-imagine — vor spune tot asta, pe bună
dreptate. **Nimic nu recunoaște text dintr-o fotografie**, iar o operațiune de întreținere nu
schimbă asta.

### Conversia documentelor

Face **o copie lizibilă pentru fiecare document de birou care nu are încă una** — paginile pe care
browserul le arată pentru fișiere Word, Excel, PowerPoint și OpenDocument.

**Rulează-o când:**

- ai instalat serviciul opțional de conversie *după* ce ai încărcat o arhivă,
- documentele arată la nesfârșit *„Documentul se pregătește pentru citit aici."*, sau *„Această
  instalare nu poate pagina documente de birou"*, iar asta a fost rezolvată între timp.

Aceasta este cea mai probabilă operațiune de care are nevoie un club, pentru că serviciul de
conversie este opțional și se adaugă de obicei mai târziu.

### Recuperarea pozițiilor fotografiilor

Pune **poziția înregistrată de un aparat** pe fotografiile deja stocate fără una.

**Rulează-o când:**

- fotografiile geolocalizate nu apar pe stratul *Fotografii geolocalizate* al hărții,
- ai importat o arhivă foto mare înainte ca asta să funcționeze.

Citește ce este în fișiere. Nu inventează poziții și nu scrie nimic înapoi în fișierele
tale de imagine.

### Citirea topografiilor

Citește din nou **fiecare model de drumuire** (`.lox` / `.3d`) a instalării, din fișierul deja păstrat.

**Rulează-o când:**

- ai actualizat la o versiune care citește topografiile altfel (secțiunile „Upgrading across …”
  din ghidul de instalare spun asta acolo unde e cazul) — o stație
  numită în alt fel, o cifră înregistrată de acum, pereți construiți de acum dintr-un grafic,
- modelele topo încărcate înaintea unei asemenea actualizări se comportă altfel decât cele
  încărcate după ea.

Înlocuiește ce a produs fiecare citire anterioară; nu se încarcă nimic și niciun model nu își
schimbă identitatea, așa că turele urmărite pe un model rămân pe el. Un model nu reține ce
versiune l-a citit, așa că operațiunea le ia pe **toate**, nu doar pe cele vechi — socotește că
durează cât citirea fiecărei topografii o dată. Modelele care nu au putut fi citite sunt
încercate din nou; pereții (`.stl`) sunt lăsați în pace. Pentru un singur model, **Citește din
nou** pe rândul lui din lista peșterii face același lucru — vezi
[Topografii și modele](../features/surveys-and-models.md).

**Se pornește cu** `POST /api/v1/jobs/survey-reading-backfill`, dintr-un cont care are dreptul de
rulare a sarcinilor.

**Nimic nu iese din uz cât rulează.** O topografie care a fost citită rămâne **Gata** cât își
așteaptă rândul și cât este citită: peștera își păstrează cifrele, iar o urmărire a unei echipe pe
ea își păstrează desenul, din citirea anterioară, până când cea nouă o înlocuiește într-un singur
pas. Dacă o topografie nu poate fi citită de data aceasta, păstrează citirea pe care o avea.

---

## Ce au în comun

- **Niciuna nu primește vreo intrare proprie.** Munca este o proprietate a rândurilor instalării,
  deci singurul lucru care variază este care operațiune rulezi.
- **Se pun la coadă ca sarcini de fundal**, ca un import sau o conversie raster. Nu blochează pe
  nimeni: o operațiune este luată doar când nu așteaptă nicio altă muncă, deci un import pe care
  cineva tocmai l-a confirmat nu rămâne în urma unei treceri peste toate fișierele stocate.
- **O eroare trecătoare este încercată din nou de la sine.** O operațiune întreruptă de ceva din
  jurul ei — baza de date care repornește, o depășire de timp, o eroare de rețea — se întoarce în
  coadă și este luată din nou după 30 de secunde, apoi după 2 minute: trei încercări în total. O
  eroare care s-ar repeta la fel nu este reîncercată.
- **Au nevoie de dreptul de *Rulare* pe domeniul Jobs** — vezi
  [Permisiuni](permissions.md#acțiuni).
- **Se pot rula de mai multe ori în siguranță.** O operațiune face munca nefăcută; nu reface munca
  făcută. Operațiunea de citire a topografiilor este excepția
  doar la cost: nu are cum să știe ce topografii a citit o versiune mai veche, așa că fiecare
  rulare le citește pe toate din nou — cu același rezultat. Rulată de două ori la rând, nu pune
  nimic la coadă pentru o topografie a cărei citire încă așteaptă.

## Urmărirea uneia

O sarcină pusă la coadă are o stare pe care o poți citi — cel care a cerut-o își poate vedea
propriile sarcini, iar cine are **Citire** pe domeniul Jobs le poate vedea pe oricare.

Membrii care au cerut să fie anunțați când **încărcările lor termină de procesat** vor primi
notificarea pe măsură ce munca se așază. Vezi [Notificări](../features/notifications.md).

---

## Nu sunt operațiuni: lucrurile care se repară singure

Prin contrast, acestea nu au nevoie de întreținere:

| | |
|---|---|
| **Limba unui document** | Detectată la citirea textului și corectabilă manual pe document oricând — ceea ce îl reindexează pe loc |
| **Statisticile calculate ale unei peșteri** | Nu se stochează niciodată. Se calculează din topografie de fiecare dată, deci încărcarea unei topografii mai bune le corectează |
| **Primele vizite și orele în subteran** | Nu se stochează niciodată. Tastarea unei ture mai vechi din arhivă *corectează* cifrele, în loc să lase un indicator învechit |
| **Continuările unei tabere** | O lectură a ce spun deja turele, deci înregistrarea unei continuări o dată o înregistrează peste tot |

Acesta este un tipar deliberat: **acolo unde o cifră se poate deduce, se deduce.** Operațiunile
există doar pentru munca ce chiar trebuie făcută o dată și stocată — citirea unui fișier,
conversia unui fișier, sau scoaterea unei coordonate dintr-unul.

---

## Erorile văzute în browserele oamenilor

Când ceva merge prost în browserul unei persoane autentificate — o eroare pe care pagina nu a
tratat-o — browserul trimite un scurt raport chiar instalării, care îl scrie în jurnalul
serverului ca avertisment care începe cu **Browser error**. Fiecare apariție a aceluiași defect
poartă aceeași amprentă, așa că un defect văzut de douăzeci de oameni se citește ca un singur
defect, de douăzeci de ori.

| | |
|---|---|
| **Cine raportează** | Doar conturile autentificate. Un browser în care nu s-a autentificat nimeni — de pildă un vizitator care citește o pagină publicată — nu raportează nimic |
| **Unde ajunge** | În jurnalul instalării și nicăieri altundeva — niciun serviciu din afară, niciun cont de configurat |
| **Cât** | Cel mult 30 de rapoarte pe minut pentru fiecare cont și 50 de erori deodată, așa că o pagină prinsă într-o buclă este oprită în loc să umple jurnalul |
| **Ce lasă deoparte** | Adresa paginii este scrisă fără partea ei de interogare, deci un jeton de partajare sau o căutare tastată nu ajunge niciodată în jurnal |

Nu este nimic de pornit și nicio pagină în interfață: cine administrează serverul citește
jurnalul. Cum se citește este în [INSTALL.md](../../../INSTALL.md#errors-peoples-browsers-see).
Când un membru spune *„a mers prost"*, ora pe care o dă este de ajuns pentru a găsi rândul
potrivit.

---

Înrudite: [Încărcări](../features/uploads.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md) ·
[Fotografii](../features/photographs.md)
