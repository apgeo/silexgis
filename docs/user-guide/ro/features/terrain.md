# Relief

🇬🇧 [English](../../features/terrain.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
Administrare: [Generarea reliefului](../admin/terrain-builds.md)

---

Relieful este denivelarea reală de sub [vizualizarea 3D](3d-view.md). Fără el globul este o sferă
netedă și peșterile stau sub o suprafață fără trăsături; cu el, stau sub versanți reali.

**Este complet opțional.** O instalare care nu-l vrea deloc nu este afectată, nu descarcă nimic
și nu contactează niciun server de elevație.

---

## Ce înseamnă pentru tine ca cititor

Dacă instalarea are relief, vei observa că:

- scena 3D are dealuri,
- vederea decupată are sens, pentru că există ceva de decupat,
- adâncimile la care a fost măsurată o topografie se citesc față de o suprafață reală.

Dacă nu are, vizualizarea 3D funcționează în continuare. O spune limpede, în loc să eșueze.

Acolo unde instalarea are mai multe generări — una grosieră peste regiune și una mai fină peste un
masiv, de pildă — vizualizarea 3D te lasă să alegi pe care o desenează și îți oferă una mai fină
când te oprești deasupra terenului pe care îl acoperă. Vezi
[Alegerea între generări](3d-view.md#alegerea-între-generări).

## Imagini ale terenului pe hartă

Relieful nu este doar pentru glob. Un administrator poate cere ca o generare încheiată să fie
transformată în **imagini ale terenului** — relief umbrit, pantă, orientare, asperitate, poziție
topografică, rugozitate, un relief colorat sau curbe de nivel — iar oricine are dreptul de a citi
relieful le găsește apoi în lista de straturi a hărții plane, la **Imagini ale terenului**. Activează
una și se desenează sub peșteri.

- Fiecare imagine este listată după numele ei, cu felul imaginii și spațiul pe care îl ocupă pe disc
  alături, așa că o hartă a pantei și una a orientării cu același nume pot fi totuși deosebite. O
  imagine care încă se calculează apare în listă, dar nu poate fi activată încă.
- **Depășită** lângă o imagine înseamnă că terenul de sub ea s-a schimbat de când a fost calculată —
  instalarea desenează acum altă generare, sau modul în care se calculează acel fel de imagine a
  fost între timp corectat. Se desenează în continuare, pentru că un relief umbrit vechi este de
  obicei mai bun decât niciunul; indică eticheta ca să afli care dintre cele două motive este.
  Nu citi o nepotrivire între o asemenea imagine și peșteri ca pe o greșeală în datele peșterii.
- **Sunt imagini, nu date.** De-a lungul unei curbe de nivel nu se poate măsura, ea nu poate fi
  selectată sau exportată și nicio curbă nu își poartă înălțimea. Curbele de nivel rămân netede până
  când harta este apropiată dincolo de circa 1:4.000; mai aproape de atât își arată treptele.

## Ce costă să-l ai

Relieful este generat de un administrator, un dreptunghi pe rând. Acoperirea gratuită de 30 de
metri este obținută de aplicație, sau se furnizează rastere proprii. Fiecare generare este
transformată într-o piramidă de dale servită ca fișiere statice de aceeași instalare.

Două lucruri de știut ca ne-administrator:

1. **Generarea dalelor are nevoie de un serviciu suplimentar** pe care o instalare obișnuită nu îl
   rulează. Dacă a ta nu îl rulează, generările se opresc la acel pas, iar pagina
   Relief explică exact asta, împreună cu singurul rând de adăugat în fișierul de mediu al
   instalării. După ce serviciul rulează, generarea oprită este rulată din nou de unde s-a oprit,
   nu luată de la capăt.
2. **Adâncimea este un compromis.** Fiecare nivel aproximativ împătrește și dalele scrise, și
   timpul necesar. Nivelul 13 este cam ce conține efectiv acoperirea de 30 de metri; a cere mai
   mult de la acele date nu adaugă decât spațiu pe disc.

## Atribuire

Fiecare generare poartă un **credit** — cui aparțin datele — și acesta este scris în suprafața
publicată. O generare nu trebuie să poarte niciodată un credit care nu este al ei. Dacă furnizezi
rastere, trebuie să spui de unde vin.

## Înălțimi

O generare declară dacă numerele ei sunt **peste nivelul mării** (ortometrice) sau **peste
elipsoid**, iar pentru al doilea caz, înălțimea geoidului local. Greșeala ridică sau coboară
întreaga suprafață cu zeci de metri — ceea ce face apoi ca fiecare peșteră să pară greșit
poziționată pe verticală.

---

Pentru fluxul complet de generare și pentru cererea imaginilor terenului, vezi
[Generarea reliefului](../admin/terrain-builds.md). Înrudite: [Vizualizarea 3D](3d-view.md) ·
[Spațiul de lucru al hărții](map-workspace.md)
