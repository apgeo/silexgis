# Servicii de hărți (WMS și WMTS)

🇬🇧 [English](../../features/map-services.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Spațiul de lucru al hărții](map-workspace.md) ·
[Vizualizarea 3D](3d-view.md) · [Zone de lucru](work-areas.md) ·
Administrare: [Map backgrounds](../../../INSTALL.md#map-backgrounds) (în engleză)

---

Un **serviciu de hărți** este un fundal sau o hartă suprapusă care nu e un set de imagini gata
făcute, păstrate undeva, ci un server ținut de altcineva, căruia i se cere imaginea zonei de pe
ecran. Agențiile naționale de cartografie își publică așa foile topografice, ortofotoplanurile și
hărțile cadastrale, motiv pentru care un club ajunge de obicei așa la harta „oficială” a țării
sub peșterile sale.

Există două feluri, iar ca cititor nu trebuie să le deosebești:

- **WMS** (Web Map Service) desenează o imagine exact pentru zona și dimensiunea cerute.
- **WMTS** (Web Map Tile Service) dă aceeași hartă deja tăiată în dale, ca fundalurile obișnuite.

Se folosesc ca orice alt fundal sau altă hartă suprapusă. Pagina de față este despre ce e
diferit la ele.

---

## Unde le găsești

În arborele de straturi al [spațiului de lucru al hărții](map-workspace.md#arborele-de-straturi),
sub grupul în care le-a pus instalarea (fișierul livrat folosește un grup numit *Map services*).
Un serviciu este fie un **fundal** (unul singur odată, în locul celui dinainte), fie o **hartă
suprapusă** (desenată peste fundalul ales, mai multe odată), și fiecare are propriul cursor de
**opacitate**. Care servicii există decide administratorul: le listează instalarea, iar o
instalare care nu listează niciunul nu arată niciunul. Cele două exemple livrate cu aplicația, o
hartă federală elvețiană și harta de bază austriacă, sunt oprite și acoperă doar țările lor.

Aceleași servicii sunt oferite și în panoul de straturi al [vizualizării 3D](3d-view.md), așezate
peste relief. Ce ai ales se păstrează într-o [vedere salvată](saved-views-and-windows.md) ca orice
alt strat.

## Ce e diferit față de un fundal obișnuit

- **Depinde de serverul altcuiva.** Dacă serverul agenției e lent, căzut sau de neatins de acolo
  de unde ești (o instalare sau un telefon fără legătură cu exteriorul), stratul rămâne gol sau se
  umple încet, în timp ce restul hărții merge. Nu e o defecțiune a instalării tale. Încearcă mai
  târziu sau alege alt fundal.
- **Se oprește la granițele țării sale.** Un serviciu național desenează țara lui și nimic dincolo;
  în afara ei stratul e pur și simplu gol. Nivelurile de zoom peste ce poate desena serviciul nu
  sunt cerute.
- **Poate arăta diferit de la o scară la alta.** Serviciul hotărăște singur ce arată la fiecare
  scară; aplicația nu îl restilizează.
- **Își poartă creditul și condițiile.** Creditul înregistrat de administrator pentru serviciu
  însoțește stratul. Condițiile în care o agenție lasă harta să fie folosită sunt ale agenției, iar
  de la administrator se așteaptă să le fi citit.
- **Se desenează doar pe hartă și în 3D.** Nu apare niciodată pe harta de ansamblu a
  [zonelor de lucru](work-areas.md) și nu se copiază niciodată într-un document, cum e harta din
  raportul unei ture sau un raport tipărit: dacă condițiile unui serviciu permit reproducerea
  imaginilor sale este o întrebare la care aplicația nu răspunde în locul agenției, deci nu o
  face niciodată.

## Pentru administrator

Serviciile se listează în fișierul de straturi de hartă al instalării, cu o intrare
`kind="wms"` sau `kind="wmts"` care dă adresa serviciului și puținele setări care îl descriu (ce
strat, ce grilă). Cum se scrie intrarea, setările fiecărui fel și cele două exemple oprite
livrate ca model sunt în ghidul de instalare (în engleză), la
[Map backgrounds](../../../INSTALL.md#map-backgrounds). Trei lucruri care surprind:

- Grila de dale a unui serviciu WMTS este **numită în intrare**, nu citită din documentul de
  capabilități al serviciului, pentru că vizualizarea 3D nu citește capabilități, iar un fundal
  care ar depinde de ele s-ar desena într-o vedere și nu în cealaltă. Sunt acceptate doar cele
  două grile obișnuite, web-mercator (`EPSG:3857`) și longitudine/latitudine (`EPSG:4326`).
- O schimbare în fișier are efect la repornirea aplicației, iar o intrare care a fost pornită și
  e ștearsă apoi din fișier rămâne pornită în baza de date. Oprește un serviciu cu
  `enabled="false"`, nu îl șterge.
- O intrare pe care această versiune nu o poate desena este raportată la pornire și nu se mai
  desenează; nu oprește aplicația.

> **Încă nedocumentat.** Unde pe ecran apare creditul unui serviciu și dacă instalarea păstrează
> imaginile unui serviciu între vizite. Întreabă-l pe cel care ține instalarea dacă un serviciu
> pare să arate în continuare o hartă veche.
