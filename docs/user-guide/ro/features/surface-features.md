# Fenomene de suprafață

🇬🇧 [English](../../features/surface-features.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Peșteri și intrări](caves-and-entrances.md) ·
[Spațiul de lucru al hărții](map-workspace.md)

---

Tot ce este pe hartă și nu este peșteră, intrare sau drumuire: doline, izvoare, resurgențe,
ponoare, falii, pereți, zone carstice, orice desenează clubul tău.

Dar „fenomen de suprafață" este puțin nepotrivit ca nume, pentru că modelul de dedesubt este
același pe care îl folosesc peșterile. Vezi
[Noțiuni de bază](../core-concepts.md#1-tot-ce-este-pe-hartă-este-un-obiect).

---

## Fel, categorie și tip

Trei axe, cu roluri diferite.

**Felul** — care dintre cele patru lucruri este: *Obiect*, *Peșteră*, *Intrare*,
*Drumuire*. Felul unei înregistrări nu se poate schimba.

**Categoria** — *Suprafață*, *Subteran*, *Areal*, *Structură*. Grupare largă, pentru filtrare.

**Tipul** — lucrul propriu-zis: dolină, izvor, faliu, perete… Tipurile vin dintr-un vocabular pe
care instalarea îl deține și îl poate extinde. Un tip poartă un **simbol** cu care se desenează pe
hartă și decide ce **proprietăți tipizate** i se cer obiectului.

## Geometrie

Un obiect poate fi **Punct**, **Linie**, **Poligon** sau variantele multiple. Desenarea este
tratată în [Spațiul de lucru al hărții](map-workspace.md#desenare-și-editare).

Zonele desenate ca contur primesc cifre de **formă măsurată** — vezi
[Măsurători](measurements-and-statistics.md#formă-măsurată-morfometrie).

## Proprietăți tipizate

Dincolo de nume, descriere și geometrie, un obiect poartă proprietățile pe care le definește
tipul lui. Unui izvor i se cer alte lucruri decât unei falii. Apar ca secțiune **Proprietăți** și
sunt filtrabile.

---

## Ierarhie: părinți și copii

Un obiect poate avea **părinți** — lucrurile care îl conțin — și **obiecte conținute**.

- O zonă carstică conține peșteri; o peșteră își conține intrările.
- Unul dintre mai mulți părinți poate fi marcat **principal**, iar firul de navigare îl urmează
  pe acela.
- **Conținerea moștenește protecția locației în jos.** Protejezi zona și tot ce este înăuntru
  este protejat odată cu ea.
- **Ștergerea unui obiect șterge ce conține.** Confirmarea o spune — și spune că nu se elimină
  nimic: întreaga ștergere poate fi anulată din
  [Peșteri și obiecte șterse](caves-and-entrances.md#peșteri-și-obiecte-șterse), la care se
  ajunge din butonul de deasupra listei.

Editezi părinții din pagina obiectului: *Editează părinții → Adaugă părinte…*, căutând obiecte
după nume.

## Obiecte înrudite și legături

Pe lângă conținere, obiectele pot fi **legate** — o relație numită, de intrare sau de ieșire, cu
o notă opțională. Iar dincolo de obiecte, [mecanismul general de legături](links.md) leagă un
obiect de documente, ture, speologi, cluburi, vederi salvate, modele 3D, dulapuri și tabere.

Un capăt de legătură pe care nu ai voie să-l citești este afișat ca *Inaccesibil*, nu numit.

---

## Lista de obiecte

**Registru → Obiecte.** Câmpurile ei — **Caută după denumire…**, **Toate felurile**, **Toate
tipurile**, **Toate categoriile** și **Filtrează după etichetă…** — formează un singur filtru,
păstrat în adresa paginii, cu controalele pentru filtre salvate alături (**Aplică un filtru
salvat…**, **Salvează ca filtru…**, **Filtre salvate** și filtrele rapide). Vezi
[Filtrarea pe paginile de listă](search-and-filters.md#filtrarea-pe-paginile-de-listă). Când nu
este pus nimic, sunt listate toate felurile. Coloane: nume, fel, tip, geometrie, vizibilitate,
actualizat.

Fiecare rând oferă **Arată pe hartă**, **Deschide**, **Editează**, **Șterge**. Un rând de
peșteră, intrare sau drumuire oferă și **Deschide pagina completă** — pagina lui specifică, nu
pagina generică de obiect. De la tastatură, **Tab** ajunge la numele unui rând și **Enter** îl
deschide.

Deasupra listei:

| Control | Ce face |
|---|---|
| **Arată-le pe hartă** | Deschide harta desenând ce găsește acest filtru |
| **Selectează pe hartă tot ce s-a găsit (n)** | Face din rezultat, până la 200, selecția hărții — vezi [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă) |
| **Exportă** | GeoJSON, GPX, KML, CSV sau shapefile arhivat cu tot ce găsește filtrul, nu doar pagina aceasta |
| **Peșteri și obiecte șterse** | [Readuce](caves-and-entrances.md#peșteri-și-obiecte-șterse) ce ai voie să restaurezi |

Cu harta deschisă lângă listă, indicarea unui rând încercuiește acel obiect pe hartă, iar
alegerea lui pe hartă îi marchează rândul.

## Vizibilitate și protecție

Aceleași patru vizibilități ca peste tot, și același indicator **Locație protejată**. Un obiect
a cărui geometrie este reținută arată **„Locație nedivulgată"** pe hartă și **„Localizare
nedivulgată — geometria exactă este protejată."** în panou.

---

Înrudite: [Spațiul de lucru al hărții](map-workspace.md) ·
[Legături între înregistrări](links.md) ·
[Vocabulare](../admin/vocabularies.md)
