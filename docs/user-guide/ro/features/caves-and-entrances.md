# Peșteri și intrări

🇬🇧 [English](../../features/caves-and-entrances.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux: [Înregistrarea unei peșteri noi](../workflows/record-a-cave.md)

---

Fișa peșterii este cel mai dens lucru din aplicație. Pagina aceasta îi parcurge toată suprafața.

---

## Lista de peșteri

**Registru → Peșteri**, registrul peșterilor. Un tabel servit de server, pe care îl parcurgi
pe pagini. Coloane: nume, cod, tip, regiune, lungime cartată, denivelare, număr de intrări și
vizibilitate; sortezi după nume, regiune, lungime sau denivelare. Un rând deschide peștera — de la
tastatură, **Tab** până la numele ei și **Enter**.

Deasupra tabelului stau câmpurile lui de filtrare — **Caută denumire sau toponim…**, **Toate
tipurile**, **Regiune**, **Lungime (m)**, **Filtrează după etichetă…** — cu **Filtru avansat…**
pentru tot ce ele nu pot spune, plus filtrele salvate. Adresa paginii poartă toată întrebarea,
așa că un link o redeschide. Totul este descris la
[Filtrul registrului peșterilor](search-and-filters.md#filtrul-registrului-peșterilor).

De aici:

| Control | Ce face |
|---|---|
| **Peșteră nouă** | Deschide un formular de peșteră gol |
| **Arată-le pe hartă** | Deschide harta desenând peșterile găsite de acest filtru |
| **Selectează pe hartă tot ce s-a găsit (n)** | Face din rezultat, până la 200 (sau numărul stabilit de instalația ta), selecția hărții |
| **Exportă** | GeoJSON, GPX, KML, CSV sau shapefile arhivat — și [KarstLink](#partajarea-peșterilor-în-formatul-de-interschimb) — cu tot ce găsește filtrul, nu doar pagina aceasta |
| **Peșteri și obiecte șterse** | [Readuce](#peșteri-și-obiecte-șterse) ce ai voie să restaurezi |

Deschis din **Listează peșterile din vedere** al hărții, registrul este restrâns la ce arăta
harta și spune *„Doar peșterile din vederea hărții"*, cu **Arată peșterile de peste tot** alături.
Vezi [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă).

Există și o vedere **Distribuții** peste peșterile de pe pagină — histogramă de lungimi, lungime
față de adâncime, rang–mărime cu ajustare de tip lege de putere, și o defalcare pe tipuri.
Vezi [Măsurători și statistici](measurements-and-statistics.md#distribuții-carstice).

## Formularul peșterii

Șapte secțiuni. Doar numele este obligatoriu.

### Identificare
Nume · Alte toponime · Cod de identificare · Tip de peșteră · Descriere · Site web

### Localizare
Regiune · Bazin hidrografic · Vale · Râu tributar · Cea mai apropiată adresă ·
Număr cadastral · Note de localizare

### Geologie
Tip de rocă · Vârsta rocii

### Morfometrie
Lungime cartată · Lungime estimată · Extindere reală · Extindere proiectată ·
Denivelare pozitivă · Denivelare negativă · Denivelare potențială · Altitudine · Volum ·
Suprafață · Indice de ramificare · Vârsta peșterii

> Acestea sunt cifrele *declarate* — ce a tastat cineva. Aplicația **calculează** și cifre dintr-o
> topografie încărcată și le arată alături, inclusiv când nu se potrivesc. Vezi
> [Măsurători și statistici](measurements-and-statistics.md).

### Stare
Stadiul explorării (necunoscut / în desfășurare / încheiat / abandonat) · Clasa de protecție ·
Peșteră amenajată · Lungimea amenajată

### Descoperire
Data descoperirii · Descoperitor

### Acces
**Vizibilitate** — privată / grup de speologie / utilizatori autentificați / publică.
**Locație protejată** — *„Coordonatele exacte și câmpurile de localizare precisă sunt ascunse
utilizatorilor fără permisiune explicită."* Vezi
[Protecția locațiilor](../admin/location-protection.md).

---

## Intrări

O peșteră are una sau mai multe intrări, fiecare o înregistrare proprie, cu poziția ei.

| Câmp | Observații |
|---|---|
| **Coordonate** | Longitudine și latitudine. Desenate pe hartă sau tastate |
| **Altitudine** | Metri |
| **Tip de intrare** | Dintr-un vocabular al instalării |
| **Calitatea poziției** | Necunoscută · GPS · De pe hartă · Estimată |
| **Ridicată** | Când a fost luată poziția |
| **Principală** | Intrarea principală; câteva lucruri se bazează implicit pe ea |

**Merită să fii sincer cu privire la calitatea poziției.** Este diferența dintre „mergi aici" și
„caută pe versantul ăsta", și este singurul câmp pe care nimeni nu-l poate reconstitui mai târziu.

Adaugi o intrare din pagina peșterii, din unealta *Intrare nouă aici* a hărții, sau prin clic
dreapta pe hartă.

---

## Ce crește pe pagina unei peșteri

Dincolo de formular, pagina adună secțiuni pe măsură ce o hrănești:

| Secțiune | Vezi |
|---|---|
| **Intrări** | Mai sus |
| **Drumuiri** | [Topografii și modele](surveys-and-models.md#drumuiri) |
| **Modele topo 3D** | [Topografii și modele](surveys-and-models.md) |
| **Surse topografice** | [Topografii și modele](surveys-and-models.md#surse-topografice) |
| **Statistici din cartare** | [Măsurători](measurements-and-statistics.md#ce-măsoară-o-topografie) |
| **Fotografii și documente** | [Fotografii](photographs.md) · [Documente](documents-and-cabinets.md) |
| **Etichete** | Libere, filtrabile în tabele și pe straturile hărții |
| **Legături** | [Legături între înregistrări](links.md) |
| **Ture** | Turele care au numit peștera, cele mai noi întâi — se completează automat |
| **Permisiuni** | [Permisiuni](../admin/permissions.md) |
| **Linkuri de partajare** | [Partajare](sharing-and-public-pages.md) |
| **Coduri tipărite** | [Coduri QR](sharing-and-public-pages.md#coduri-qr-tipărite) |
| **Istoric** | [Istoric și modificări](history-and-audit.md) |

Rândul de rezumat le numără: *n intrări · n drumuiri · n modele 3D · n atașamente ·
n jurnale de tură.*

### Secțiunea Ture

Nu adaugi ture la o peșteră. Secțiunea **Ture** listează turele care au numit-o prin câmpurile
lor *Ce a făcut tura*, cele mai noi întâi, numărate după aceeași regulă care decide ce poate
arăta lista. Dacă o tură a numit peștera dar tu nu poți citi tura, ea nu apare — iar
numărul reflectă asta.

---

## O peșteră este un obiect

Sub suprafață, o peșteră este un fel de [obiect](surface-features.md), motiv pentru care:

- o peșteră poate fi **conținută** de ceva (o zonă carstică) și poate conține lucruri (intrările
  ei), cu fir de navigare pe măsură,
- conținerea **moștenește protecția locației în jos**,
- o peșteră poate fi **legată** de orice, printr-o relație numită.

---

## Partajarea peșterilor în formatul de interschimb

Pe lângă formatele de hartă, meniul **Export** din lista de peșteri oferă **KarstLink (JSON-LD)** — o
descriere a peșterilor în vocabularul speologic comun publicat de UIS, astfel încât cineva care
folosește alt program să le poată citi. Exportă toate peșterile pe care le găsește filtrul
registrului — câmpurile rapide și condițiile avansate deopotrivă — nu doar pagina de pe ecran. Cât
timp registrul este restrâns la vederea hărții, nu este disponibil: *„Nu cât timp lista este
restrânsă la fereastra hărții — elimină fereastra pentru a exporta toată lista filtrată."* Motivul
este protecția: fișierul de interschimb cuprinde o peșteră protejată oricare i-ar fi poziția, așa că
un fișier restrâns la o fereastră ar trebui să hotărască după acea poziție care peșteri protejate
intră în el — iar lista peșterilor din fișier ar sugera atunci unde se află. Un
fișier cuprinde cel mult 5.000 de peșteri; un set mai mare este refuzat, iar dialogul îți cere să
restrângi filtrele și să exporți pe părți, în loc să predea un fișier oprit la o limită.

**Întâi pune o întrebare, și asta contează.** Dacă vreuna dintre peșterile exportate are locația
protejată, dialogul spune câte sunt și întreabă ce trebuie să scrie fișierul despre ele. Sunt trei
răspunsuri:

- **Include-le fără coordonate** — tot restul călătorește, fără nicio coordonată.
- **Lasă-le complet în afara fișierului.**
- **Așază-le pe grila de protecție** — aceeași poziție rotunjită pe care harta o arată celor care
  nu au voie să vadă poziția exactă.

**Poziția exactă nu este oferită nimănui.** Nici unui administrator, nici celui care a înregistrat
peștera. Un fișier continuă să existe și după ce permisiunile care l-au produs s-au schimbat, așa că
această aplicație nu scrie o poziție protejată într-unul.

**Fișierul spune ce s-a decis.** Fiecare peșteră declară care dintre cele trei variante s-a aplicat
și, unde poziția a fost rotunjită, cât de departe poate fi de adevăr; un antet spune câte peșteri
sunt în fișier și câte au fost lăsate deoparte în mod deliberat — ca nimeni să nu confunde numărul de
peșteri din fișier cu numărul din registru.

Bifează **Ține minte acest răspuns și nu mă mai întreba** și nu vei mai fi întrebat — răspunsul îți
urmează contul în orice browser. Un al doilea element de meniu, **KarstLink (JSON-LD) — schimbă
răspunsul meu…**, te lasă să îl schimbi, iar dacă serverul refuză o cerere, dialogul revine în loc
să eșueze în tăcere.

Fiecare export de interschimb este înregistrat în [istoric și jurnalul de
audit](history-and-audit.md), împreună cu peșterile exportate și felul în care le-au fost tratate
pozițiile.

---

## Numere din alte registre

O peșteră are adesea o intrare și în altă parte — în [Grottocenter](https://grottocenter.org), baza
de date internațională a comunității, sau într-un cadastru național. Pagina peșterii are o secțiune
mică pentru aceste numere, astfel încât legătura să călătorească odată cu peștera și să apară într-un
export de interschimb.

Dacă administratorul tău a activat căutarea în Grottocenter, un buton întreabă acolo dacă
peștera este deja cunoscută. **Se trimite doar numele peșterii** — niciodată poziția — și se
păstrează doar numărul pe care îl accepți. Majoritatea instalărilor vor vedea „administratorul nu a
configurat aceasta", iar în acea stare nimic nu părăsește instalarea.

**O atenționare.** O intrare în Grottocenter publică coordonate. De aceea, pentru o peșteră cu
locația protejată aici, exportul de interschimb lasă deliberat legătura externă afară: publicarea ei
ar preda exact poziția pe care fișierul o ascunde.

---

## Ștergerea și restaurarea

**Ștergi această peșteră?** — și, la obiecte în general, *obiectele conținute se șterg odată
cu ea*: o peșteră își ia intrările și drumuirile. Confirmarea spune ce urmează să se întâmple:
**nu se elimină nimic, iar peștera poate fi restaurată din Peșteri și obiecte șterse.** Ștergerea
unei singure intrări din fișa peșterii spune același lucru. Totul este consemnat în
[istoricul de modificări](history-and-audit.md).

O peșteră ștearsă **dispare de peste tot deodată** — din listă, de pe hartă, din căutare, din
turele care o numeau — iar adresa ei răspunde ca și cum nu ar fi existat niciodată, inclusiv
pentru proprietar.

### Peșteri și obiecte șterse

**Peșteri → Peșteri și obiecte șterse** (același buton se află și pe lista **Obiecte**) arată
ștergerile **pe care le poți anula**, cele mai recente primele.

| Coloană | Ce arată |
|---|---|
| **Denumire** | Ce a fost șters și, dedesubt, unde se afla — o intrare se deosebește prin peștera ei |
| **Fel** | Peșteră, Intrare sau tipul obiectului |
| **Șters** | Când |
| **Șterse odată cu el** | Ce a plecat împreună — *1 intrare*, *2 intrări, 3 alte obiecte* |

- **Un rând este o ștergere, nu un obiect.** O peșteră ștearsă cu două intrări este un singur rând
  care spune *2 intrări*; restaurarea le aduce înapoi pe toate trei. O intrare pe care ai
  șters-o *înainte* de a șterge peștera este o ștergere separată: apare în listă după ce peștera
  revine și rămâne ștearsă până o restaurezi și pe ea.
- **Cine poate șterge ceva îl poate și restaura.** Lista se calculează rând cu rând după această
  regulă, deci cine nu poate șterge nimic vede o listă goală. O intrare poate fi restaurată și de
  cine poate edita peștera, pentru că tot el o poate șterge din fișa peșterii.
- **Lista nu spune niciodată unde se află ceva.** Nu conține nicio coordonată, iar o peșteră
  restaurată se deschide arătând exact poziția pe care o puteai vedea înainte — aproximativă
  dacă era aproximativă pentru tine. Nu ți se arată și nu ți se numără nimic din ce nu
  puteai citi înainte de ștergere.
- **Restaurează** întreabă mai întâi, aduce înapoi peștera sau obiectul cu tot ce a fost șters
  odată cu el și îl deschide. **O tură care numea peștera o numește din nou.**
- Ceva aflat **într-o peșteră sau zonă ștearsă nu poate fi restaurat separat** — pagina spune ce
  trebuie restaurat mai întâi.
- O peșteră restaurată al cărei **nume a fost refolosit între timp** stă pur și simplu alături de
  cea cu același nume: denumirile nu sunt unice.
- O **drumuire** nu se restaurează separat. Revine odată cu peștera ei, dacă au fost șterse
  împreună.
- Peșterile și obiectele șterse sunt **păstrate până le restaurează cineva**; nimic nu le elimină
  după un număr de zile.

---

Flux: [Înregistrarea unei peșteri noi](../workflows/record-a-cave.md) ·
Înrudite: [Fenomene de suprafață](surface-features.md) ·
[Măsurători și statistici](measurements-and-statistics.md)
