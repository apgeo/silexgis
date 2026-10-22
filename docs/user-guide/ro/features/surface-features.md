# Fenomene de suprafață

🇬🇧 [English](../../features/surface-features.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Peșteri și intrări](caves-and-entrances.md) ·
[Spațiul de lucru al hărții](map-workspace.md)

---

Tot ce este pe hartă și nu este peșteră, intrare sau poligonație: doline, izvoare, resurgențe,
ponoare, falii, pereți, zone carstice, orice desenează clubul dumneavoastră.

Dar „fenomen de suprafață" este puțin nepotrivit ca nume, pentru că modelul de dedesubt este
același pe care îl folosesc peșterile. Vedeți
[Noțiuni de bază](../core-concepts.md#1-tot-ce-este-pe-hartă-este-un-element).

---

## Fel, categorie și tip

Trei axe, cu roluri diferite.

**Felul** — care dintre cele patru lucruri este: *Element*, *Peșteră*, *Intrare în peșteră*,
*Poligonație*. Felul unei înregistrări nu se poate schimba.

**Categoria** — *Suprafață*, *Subteran*, *Zonă*, *Structură*. Grupare largă, pentru filtrare.

**Tipul** — lucrul propriu-zis: dolină, izvor, faliu, perete… Tipurile vin dintr-un vocabular pe
care instalarea îl deține și îl poate extinde. Un tip poartă un **simbol** cu care se desenează pe
hartă și decide ce **proprietăți tipizate** i se cer elementului.

## Geometrie

Un element poate fi **Punct**, **Linie**, **Poligon** sau variantele multiple. Desenarea este
tratată în [Spațiul de lucru al hărții](map-workspace.md#desenare-și-editare).

Zonele desenate ca contur primesc cifre de **formă măsurată** — vedeți
[Măsurători](measurements-and-statistics.md#formă-măsurată-morfometrie).

## Proprietăți tipizate

Dincolo de nume, descriere și geometrie, un element poartă proprietățile pe care le definește
tipul lui. Unui izvor i se cer alte lucruri decât unei falii. Apar ca secțiune **Proprietăți** și
sunt filtrabile.

---

## Ierarhie: părinți și copii

Un element poate avea **părinți** — lucrurile care îl conțin — și **elemente conținute**.

- O zonă carstică conține peșteri; o peșteră își conține intrările.
- Unul dintre mai mulți părinți poate fi marcat **principal**, iar firul de navigare îl urmează
  pe acela.
- **Conținerea moștenește protecția locației în jos.** Protejați zona și tot ce este înăuntru
  este protejat odată cu ea.
- **Ștergerea unui element șterge ce conține.** Confirmarea o spune — și spune că nu se elimină
  nimic: întreaga ștergere poate fi anulată din
  [Peșteri și elemente șterse](caves-and-entrances.md#peșteri-și-elemente-șterse), la care se
  ajunge din butonul de deasupra listei.

Editați părinții din pagina elementului: *Editează părinții → Adaugă părinte…*, căutând elemente
după nume.

## Elemente înrudite și legături

Pe lângă conținere, elementele pot fi **legate** — o relație numită, de intrare sau de ieșire, cu
o notă opțională. Iar dincolo de elemente, [mecanismul general de legături](links.md) leagă un
element de documente, ture, speologi, cluburi, vederi salvate, modele 3D, dulapuri și tabere.

Un capăt de legătură pe care nu aveți voie să-l citiți este afișat ca *Inaccesibil*, nu numit.

---

## Lista de elemente

**Cadastru → Elemente.** Câmpurile ei — **Caută după denumire…**, **Toate felurile**, **Toate
tipurile**, **Toate categoriile** și **Filtrează după etichetă…** — formează un singur filtru,
păstrat în adresa paginii, cu controalele pentru filtre salvate alături (**Aplicați un filtru
salvat…**, **Salvați ca filtru…**, **Filtre salvate** și filtrele rapide). Vedeți
[Filtrarea pe paginile de listă](search-and-filters.md#filtrarea-pe-paginile-de-listă). Când nu
este pus nimic, sunt listate toate felurile. Coloane: nume, fel, tip, geometrie, vizibilitate,
actualizat.

Fiecare rând oferă **Arată pe hartă**, **Deschide**, **Editează**, **Șterge**. Un rând de
peșteră, intrare sau poligonație oferă și **Deschide pagina completă** — pagina lui specifică, nu
pagina generică de element. De la tastatură, **Tab** ajunge la numele unui rând și **Enter** îl
deschide.

Deasupra listei:

| Control | Ce face |
|---|---|
| **Arată-le pe hartă** | Deschide harta desenând ce găsește acest filtru |
| **Selectează pe hartă tot ce s-a găsit (n)** | Face din rezultat, până la 200, selecția hărții — vedeți [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă) |
| **Exportă** | GeoJSON, GPX, KML, CSV sau shapefile arhivat cu tot ce găsește filtrul, nu doar pagina aceasta |
| **Peșteri și elemente șterse** | [Readuce](caves-and-entrances.md#peșteri-și-elemente-șterse) ce aveți voie să restaurați |

Cu harta deschisă lângă listă, indicarea unui rând încercuiește acel element pe hartă, iar
alegerea lui pe hartă îi marchează rândul.

## Vizibilitate și protecție

Aceleași patru vizibilități ca peste tot, și același indicator **Locație protejată**. Un element
a cărui geometrie este reținută arată **„Locație nedivulgată"** pe hartă și **„Locație
nedivulgată — geometria exactă este protejată"** în panou.

---

Înrudite: [Spațiul de lucru al hărții](map-workspace.md) ·
[Legături între înregistrări](links.md) ·
[Vocabulare](../admin/vocabularies.md)
