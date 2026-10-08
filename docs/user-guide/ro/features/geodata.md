# Fișiere de geodate

🇬🇧 [English](../../features/geodata.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux:
[Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md)

---

**Cadastru → Geodate** conține fișierele vectoriale pe care le-ați încărcat, hărțile raster și
registrul a ce a creat fiecare import.

Trei file: **Fișiere vectoriale**, **Hărți raster**, **Importuri**.

---

## Fișiere vectoriale

### Încărcarea

Trageți fișiere înăuntru sau faceți clic pentru a alege. Acceptate:

**GPX · KML · KMZ · GeoJSON · shapefile arhivat · CSV cu coloane de coordonate · WKT**

Importul rulează în fundal. Stare: *În așteptare → Se importă… → Importat* (sau *Eșuat*).

Fiecare fișier devine un **strat** pe care îl puteți aprinde în arborele de straturi al hărții,
cu propriul dreptunghi de încadrare și cu suprascrieri de stil. Acesta este fișierul *desenat pe
hartă* — încă nu este în cadastru.

### În cadastru

Desenarea este jumătate din treabă. **Verifică și importă în cadastru** transformă punctele în
peșteri, intrări și fenomene de suprafață, propunând ce este fiecare pornind de la obiceiurile de
denumire ale clubului. Acesta este un flux întreg:
[Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md).

### Administrare

Căutați după nume, editați detaliile unui fișier, ștergeți-l — ceea ce îi șterge și elementele
importate, iar confirmarea o spune.

---

## Hărți raster

Vedeți [Hărți raster georeferențiate](georeferenced-maps.md).

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
de acel import — *„Ștergeți toate cele n obiecte create de acest import?"* — inclusiv pe cele
agățate de înregistrări care existau deja. Un import anulat este marcat ca atare, nu dispare.

Confirmarea spune că **fiecare peșteră și element creat de import poate fi restaurat**: anularea
le șterge așa cum le-ați șterge de mână, câte o ștergere pentru fiecare, deci ajung apoi la
[Peșteri și elemente șterse](caves-and-entrances.md#peșteri-și-elemente-șterse). Pentru un import
care a creat **ture**, adaugă că și acestea pot fi restaurate și pentru cât timp — ele ajung la
[Ture șterse](trips.md#ștergerea-și-restaurarea-unei-ture). Detaliul unui import anulat marchează
fiecare astfel de obiect ca *Șters* și trimite acolo de unde poate fi restaurat.

Dacă fișierul-sursă a fost șters între timp, rândul o spune; dacă importul nu a creat nimic, o
spune și pe asta.

> Încărcările de pe **telefon** apar tot aici și se anulează la fel. Vedeți
> [Telefon și sincronizare offline](mobile-sync.md).

---

## Exportul

Direcția inversă se află pe paginile de listă și pe hartă, nu aici. Peșterile, elementele și
fișierele de geodate se exportă ca:

**GeoJSON · GPX · KML · CSV · shapefile arhivat**

Un export poartă doar ce aveți voie să citiți, cu aceeași protecție a locațiilor ca ecranul.

---

Flux: [Importul unui sezon de puncte GPS](../workflows/import-a-gps-file.md) ·
Înrudite: [Reguli de detectare](../admin/vocabularies.md#configurare--reguli-de-detectare)
