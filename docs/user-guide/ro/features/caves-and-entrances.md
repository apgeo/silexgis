# Peșteri și intrări

🇬🇧 [English](../../features/caves-and-entrances.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux: [Înregistrarea unei peșteri noi](../workflows/record-a-cave.md)

---

Fișa peșterii este cel mai dens lucru din aplicație. Pagina aceasta îi parcurge toată suprafața.

---

## Lista de peșteri

**Cadastru → Peșteri.** Un tabel servit de server: căutați după nume sau toponim, filtrați după
tip, sortați după orice coloană, paginați. Coloanele includ numele, codul, tipul, regiunea,
lungimea cartată, denivelarea, numărul de intrări și vizibilitatea.

De aici: **Peșteră nouă**, **Editează**, **Șterge** și **Exportă** (GeoJSON, GPX, KML, CSV,
shapefile arhivat).

Există și o vedere **Distribuții** peste peșterile de pe pagină — histogramă de lungimi, lungime
față de adâncime, rang–mărime cu ajustare de tip lege de putere, și o defalcare pe tipuri.
Vedeți [Măsurători și statistici](measurements-and-statistics.md#distribuții-carstice).

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
> topografie încărcată și le arată alături, inclusiv când nu se potrivesc. Vedeți
> [Măsurători și statistici](measurements-and-statistics.md).

### Stare
Stadiul explorării (necunoscut / în desfășurare / încheiat / abandonat) · Clasa de protecție ·
Peșteră amenajată · Lungimea amenajată

### Descoperire
Data descoperirii · Descoperitor

### Acces
**Vizibilitate** — privată / grup de speologie / utilizatori autentificați / publică.
**Locație protejată** — *„Coordonatele exacte și câmpurile de localizare precisă sunt ascunse
utilizatorilor fără permisiune explicită."* Vedeți
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

**Merită să fiți sincer cu privire la calitatea poziției.** Este diferența dintre „mergi aici" și
„caută pe versantul ăsta", și este singurul câmp pe care nimeni nu-l poate reconstitui mai târziu.

Adăugați o intrare din pagina peșterii, din unealta *Intrare nouă aici* a hărții, sau prin clic
dreapta pe hartă.

---

## Ce crește pe pagina unei peșteri

Dincolo de formular, pagina adună secțiuni pe măsură ce o hrăniți:

| Secțiune | Vedeți |
|---|---|
| **Intrări** | Mai sus |
| **Poligonații** | [Topografii și modele](surveys-and-models.md#poligonații) |
| **Modele topo 3D** | [Topografii și modele](surveys-and-models.md) |
| **Surse topografice** | [Topografii și modele](surveys-and-models.md#surse-topografice) |
| **Statistici din topografie** | [Măsurători](measurements-and-statistics.md#ce-măsoară-o-topografie) |
| **Fotografii și documente** | [Fotografii](photographs.md) · [Documente](documents-and-cabinets.md) |
| **Etichete** | Libere, filtrabile în tabele și pe straturile hărții |
| **Legături** | [Legături între înregistrări](links.md) |
| **Ture** | Turele care au numit peștera, cele mai noi întâi — se completează automat |
| **Permisiuni** | [Permisiuni](../admin/permissions.md) |
| **Linkuri de partajare** | [Partajare](sharing-and-public-pages.md) |
| **Coduri tipărite** | [Coduri QR](sharing-and-public-pages.md#coduri-qr-tipărite) |
| **Istoric** | [Istoric și modificări](history-and-audit.md) |

Rândul de rezumat le numără: *n intrări · n poligonații · n modele 3D · n atașamente ·
n jurnale de tură.*

### Secțiunea Ture

Nu adăugați ture la o peșteră. Secțiunea **Ture** listează turele care au numit-o prin câmpurile
lor *Ce a făcut tura*, cele mai noi întâi, numărate după aceeași regulă care decide ce poate
arăta lista. Dacă o tură a numit peștera dar dumneavoastră nu puteți citi tura, ea nu apare — iar
numărul reflectă asta.

---

## O peșteră este un element

Sub suprafață, o peșteră este un fel de [element](surface-features.md), motiv pentru care:

- o peșteră poate fi **conținută** de ceva (o zonă carstică) și poate conține lucruri (intrările
  ei), cu fir de navigare pe măsură,
- conținerea **moștenește protecția locației în jos**,
- o peșteră poate fi **legată** de orice, printr-o relație numită.

---

## Partajarea peșterilor în formatul de interschimb

Pe lângă formatele de hartă, meniul **Export** din lista de peșteri oferă **KarstLink (JSON-LD)** — o
descriere a peșterilor în vocabularul speologic comun publicat de UIS, astfel încât cineva care
folosește alt program să le poată citi. Exportă peșterile afișate în listă, cu aceleași filtre de
căutare, tip și etichetă.

**Întâi pune o întrebare, și asta contează.** Dacă vreuna dintre peșterile exportate are locația
protejată, dialogul spune câte sunt și întreabă ce trebuie să scrie fișierul despre ele. Sunt trei
răspunsuri:

- **Include peștera fără poziție** — tot restul călătorește, fără nicio coordonată.
- **Lasă acele peșteri complet în afara fișierului.**
- **Include-le la poziția aproximativă** — aceeași poziție rotunjită pe care harta o arată celor care
  nu au voie să vadă poziția exactă.

**Poziția exactă nu este oferită nimănui.** Nici unui administrator, nici celui care a înregistrat
peștera. Un fișier continuă să existe și după ce permisiunile care l-au produs s-au schimbat, așa că
această aplicație nu scrie o poziție protejată într-unul.

**Fișierul spune ce s-a decis.** Fiecare peșteră declară care dintre cele trei variante s-a aplicat
și, unde poziția a fost rotunjită, cât de departe poate fi de adevăr; un antet spune câte peșteri
sunt în fișier și câte au fost lăsate deoparte în mod deliberat — ca nimeni să nu confunde numărul de
peșteri din fișier cu numărul din registru.

Bifați **Ține minte răspunsul meu** și nu veți mai fi întrebat pe acest browser; un al doilea element
de meniu vă lasă să îl schimbați, iar dacă serverul refuză o cerere, dialogul revine în loc să eșueze
în tăcere.

Fiecare export de interschimb este înregistrat în [istoric și jurnalul de
audit](history-and-audit.md), împreună cu peșterile exportate și felul în care le-au fost tratate
pozițiile.

---

## Numere din alte registre

O peșteră are adesea o intrare și în altă parte — în [Grottocenter](https://grottocenter.org), baza
de date internațională a comunității, sau într-un cadastru național. Pagina peșterii are o secțiune
mică pentru aceste numere, astfel încât legătura să călătorească odată cu peștera și să apară într-un
export de interschimb.

Dacă administratorul dumneavoastră a activat căutarea în Grottocenter, un buton întreabă acolo dacă
peștera este deja cunoscută. **Se trimite doar numele peșterii** — niciodată poziția — și se
păstrează doar numărul pe care îl acceptați. Majoritatea instalărilor vor vedea „administratorul nu a
configurat aceasta", iar în acea stare nimic nu părăsește instalarea.

**O atenționare.** O intrare în Grottocenter publică coordonate. De aceea, pentru o peșteră cu
locația protejată aici, exportul de interschimb lasă deliberat legătura externă afară: publicarea ei
ar preda exact poziția pe care fișierul o ascunde.

---

## Ștergerea și restaurarea

**Ștergeți această peșteră?** — și, la elemente în general, *elementele conținute se șterg odată
cu ea*: o peșteră își ia intrările și poligonațiile. Confirmarea spune ce urmează să se întâmple:
**nu se elimină nimic, iar peștera poate fi restaurată din Peșteri și elemente șterse.** Ștergerea
unei singure intrări din fișa peșterii spune același lucru. Totul este consemnat în
[istoricul de modificări](history-and-audit.md).

O peșteră ștearsă **dispare de peste tot deodată** — din listă, de pe hartă, din căutare, din
turele care o numeau — iar adresa ei răspunde ca și cum nu ar fi existat niciodată, inclusiv
pentru proprietar.

### Peșteri și elemente șterse

**Peșteri → Peșteri și elemente șterse** (același buton se află și pe lista **Elemente**) arată
ștergerile **pe care le puteți anula**, cele mai recente primele.

| Coloană | Ce arată |
|---|---|
| **Denumire** | Ce a fost șters și, dedesubt, unde se afla — o intrare se deosebește prin peștera ei |
| **Fel** | Peșteră, Intrare sau tipul elementului |
| **Șters** | Când |
| **Șterse odată cu el** | Ce a plecat împreună — *1 intrare*, *2 intrări, 3 alte obiecte* |

- **Un rând este o ștergere, nu un obiect.** O peșteră ștearsă cu două intrări este un singur rând
  care spune *2 intrări*; restaurarea le aduce înapoi pe toate trei. O intrare pe care ați
  șters-o *înainte* de a șterge peștera este o ștergere separată: apare în listă după ce peștera
  revine și rămâne ștearsă până o restaurați și pe ea.
- **Cine poate șterge ceva îl poate și restaura.** Lista se calculează rând cu rând după această
  regulă, deci cine nu poate șterge nimic vede o listă goală. O intrare poate fi restaurată și de
  cine poate edita peștera, pentru că tot el o poate șterge din fișa peșterii.
- **Lista nu spune niciodată unde se află ceva.** Nu conține nicio coordonată, iar o peșteră
  restaurată se deschide arătând exact poziția pe care o puteați vedea înainte — aproximativă
  dacă era aproximativă pentru dumneavoastră. Nu vi se arată și nu vi se numără nimic din ce nu
  puteați citi înainte de ștergere.
- **Restaurează** întreabă mai întâi, aduce înapoi peștera sau elementul cu tot ce a fost șters
  odată cu el și îl deschide. **O tură care numea peștera o numește din nou.**
- Ceva aflat **într-o peșteră sau zonă ștearsă nu poate fi restaurat separat** — pagina spune ce
  trebuie restaurat mai întâi.
- O peșteră restaurată al cărei **nume a fost refolosit între timp** stă pur și simplu alături de
  cea cu același nume: denumirile nu sunt unice.
- O **poligonație** nu se restaurează separat. Revine odată cu peștera ei, dacă au fost șterse
  împreună.
- Peșterile și elementele șterse sunt **păstrate până le restaurează cineva**; nimic nu le elimină
  după un număr de zile.

---

Flux: [Înregistrarea unei peșteri noi](../workflows/record-a-cave.md) ·
Înrudite: [Fenomene de suprafață](surface-features.md) ·
[Măsurători și statistici](measurements-and-statistics.md)
