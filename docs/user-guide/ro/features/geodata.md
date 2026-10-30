# Fișiere de geodate

🇬🇧 [English](../../features/geodata.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux:
[Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md)

---

**Registru → Geodate** conține fișierele vectoriale pe care le-ai încărcat, hărțile raster și
registrul a ce a creat fiecare import.

Trei file: **Fișiere vectoriale**, **Hărți raster**, **Importuri**.

---

## Fișiere vectoriale

### Încărcarea

Trage fișiere înăuntru sau fă clic pentru a alege. Acceptate:

**GPX · KML · KMZ · GeoJSON · shapefile arhivat · CSV cu coloane de coordonate · WKT**

Importul rulează în fundal. Stare: *În așteptare → Se importă… → Importat* (sau *Eșuat*).

Fiecare fișier devine un **strat** pe care îl poți aprinde în arborele de straturi al hărții,
cu propriul dreptunghi de încadrare și cu suprascrieri de stil. Acesta este fișierul *desenat pe
hartă* — încă nu este în registru.

### În registru

Desenarea este jumătate din treabă. **Verifică și importă în registru** transformă punctele în
peșteri, intrări și fenomene de suprafață, propunând ce este fiecare pornind de la obiceiurile de
denumire ale clubului. Acesta este un flux întreg:
[Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md).

### Administrare

Caută după nume, editează detaliile unui fișier, șterge-l — ceea ce îi șterge și obiectele
importate, iar confirmarea o spune.

### Filtrarea listei

Lângă caseta de căutare, **Condiții…** deschide același editor de condiții ca galeria, pe lumea
fișierelor geodata: *Formatul fișierului*, numele, proprietarul, vizibilitatea, datele și *Poziția*
amprentei fișierului. **Aplică** restrânge lista și pune condițiile în adresa paginii, așa că
linkul le poartă. **Aplică un filtru salvat…** alege unul după nume (pot fi alese doar filtrele
despre fișiere geodata), **Salvează ca filtru…** păstrează condițiile de pe ecran sub un nume și
pentru publicul alese de tine, iar eticheta de lângă butoane spune din ce filtru salvat vin
condițiile și dacă le-ai schimbat între timp. Lista este mereu cerută cu accesul tău: un filtru
partajat de altcineva arată exact fișierele pe care le-ai fi găsit scriind aceleași condiții, iar
un fișier pe care nu ai voie să-l citești nu este niciodată numărat. Trecerea la altă filă lasă
condițiile în urmă.

---

## Hărți raster

Vezi [Hărți raster georeferențiate](georeferenced-maps.md).

---

## Importuri

Registrul fiecărui import confirmat. Un rând per import, arătând:

| Coloană | |
|---|---|
| **Fișier** | Fișierul de geodate, fotografiile sau încărcarea de pe telefon din care a venit |
| **Cum** | *Verificat* sau *Fără verificare* |
| **Confirmat** | Când |
| **Ce a creat acest import** | Detaliu extensibil |

Și lucrul care face importul sigur de experimentat: **Anulează**. Șterge toate obiectele create
de acel import — *„Ștergi toate cele n obiecte create de acest import?"* — inclusiv pe cele
agățate de înregistrări care existau deja. Un import anulat este marcat ca atare, nu dispare.

Confirmarea spune că **fiecare peșteră și obiect creat de import poate fi restaurat**: anularea
le șterge așa cum le-ai șterge de mână, câte o ștergere pentru fiecare, deci ajung apoi la
[Peșteri și obiecte șterse](caves-and-entrances.md#peșteri-și-obiecte-șterse). Pentru un import
care a creat **ture**, adaugă că și acestea pot fi restaurate și pentru cât timp — ele ajung la
[Ture șterse](trips.md#ștergerea-și-restaurarea-unei-ture). Detaliul unui import anulat marchează
fiecare astfel de obiect ca *Șters* și trimite acolo de unde poate fi restaurat.

Dacă fișierul-sursă a fost șters între timp, rândul o spune; dacă importul nu a creat nimic, o
spune și pe asta.

### Anularea unei încărcări de pe telefon

Încărcările de pe **telefon** apar tot aici, dar diferă de un import de fișier: majoritatea liniilor
lor **schimbă peșteri și obiecte care existau deja**, nu creează altele noi. De aceea o încărcare se
anulează punând lucrurile înapoi, nu doar ștergând.

Deschide o încărcare ca să-i vezi liniile. Fiecare linie numește peștera sau obiectul vizat;
deschide o linie ca să vezi **ce a schimbat încărcarea în el** — fiecare câmp cu valoarea dinainte și
de după. Ecranul nu ghicește care câmpuri au fost schimbate din nou între timp: asta o stabilește
serverul, cu aceeași regulă după care anulează.

- **Anulează această încărcare** pune fiecare valoare înapoi cum era înainte de încărcare, șterge ce a
  creat încărcarea și aduce înapoi ce a șters. Un câmp pe care altcineva l-a schimbat din nou după încărcare **rămâne cum
  este**, iar rezultatul de dedesubt spune ce câmpuri au rămas și de ce.
- **Anulează acest rând** face la fel pentru o singură linie și lasă restul încărcării în vigoare.
  Încărcarea se consideră anulată când toate liniile au fost anulate.
- **Anulează oricum** pune înapoi și câmpurile schimbate între timp. Modificările de după sunt
  înlocuite, dar rămân în [istoricul](history-and-audit.md) peșterii, de unde pot fi readuse. O
  poziție pe care nu ai voie s-o vezi exact nu este niciodată pusă înapoi, orice ai apăsa.

O încărcare o poate anula doar contul care a confirmat-o sau un administrator. După ce o linie a fost
anulată — chiar și cu un câmp rămas — nu mai poate fi anulată de aici; câmpul rămas poate fi readus
din istoricul peșterii.

Vezi și [Telefon și sincronizare offline](mobile-sync.md).

---

## Exportul

Direcția inversă se află pe paginile de listă și pe hartă, nu aici. Peșterile, obiectele și
fișierele de geodate se exportă ca:

**GeoJSON · GPX · KML · CSV · shapefile arhivat**

Un export din registrul peșterilor sau din lista de obiecte cuprinde **tot ce găsește filtrul
listei**, nu doar pagina de pe ecran — vezi
[Căutare și filtre](search-and-filters.md#filtrarea-pe-paginile-de-listă). Peșterile se pot
exporta și pentru interschimb, ca
[KarstLink](caves-and-entrances.md#partajarea-peșterilor-în-formatul-de-interschimb).

Pe pagina **Geodate**, **Exportă fișierele din listă** scrie rândurile importate ale **tuturor
fișierelor pe care le arată lista** — se aplică atât căutarea după denumire, cât și condițiile — ca un
singur strat, fiecare rând purtând numele fișierului său într-o coloană `geofile`. Un fișier pe care
nu ai voie să-l citești nu apare în el, oricine ar trimite legătura. O selecție de peste 200 de
fișiere sau de peste 200.000 de rânduri este **refuzată**, cu îndemnul de a restrânge filtrul și de a
exporta pe părți, în loc să fie tăiată. Butonul de export de pe un rând exportă în continuare doar
acel fișier.

Lista de ture își păstrează controalele proprii, iar exportul ei în foaie de calcul le urmează.
Ruta de export acceptă și un filtru de ture (condiția dintr-o legătură sau un filtru salvat) pentru
programele care o apelează: o tură trebuie să le îndeplinească pe amândouă, iar un filtru care nu
poate fi citit este refuzat în loc să exporte toate turele. Deocamdată nicio pagină a listei de ture
nu trimite un astfel de filtru. Turele pe care nu ai voie să le citești nu ajung niciodată în fișier.

Un export poartă doar ce ai voie să citești, cu aceeași protecție a locațiilor ca ecranul.

---

Flux: [Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md) ·
Înrudite: [Reguli de detectare](../admin/vocabularies.md#configurare--reguli-de-detectare)
