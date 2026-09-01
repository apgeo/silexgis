# Vizualizarea 3D

🇬🇧 [English](../../features/3d-view.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Relief](terrain.md) ·
[Topografii, poligonații și modele 3D](surveys-and-models.md)

---

Vizualizarea 3D desenează hărțile de bază ale instalării pe un glob, cu topografiile peșterilor
aflate în pământ la adâncimile la care au fost măsurate.

Se deschide din bara de navigare (**Vizualizare 3D**), din hartă (**Arată scena 3D lângă hartă**)
sau într-o fereastră proprie. Pagina se descarcă doar când o deschideți, așa că nu costă nimic
pentru cine n-o folosește niciodată.

## Cerințe și sinceritate în privința lor

Vizualizarea 3D are nevoie de **WebGL 2**. Un browser sau un driver grafic fără el primește o
explicație și un link către harta 2D, nu o pânză neagră moartă.

**Nu se contactează niciun serviciu extern.** Fără relief străin, fără imagini străine, fără
geocodare străină. Motorul 3D este servit de propria dumneavoastră instalare, iar fundalul este
format din hărțile de bază pe care le-ați configurat — deci o instalare fără ieșire la internet
are nevoie de o hartă de bază la care poate ajunge.

Dacă browserul eliberează resursele grafice ale vizualizării de două ori la rând, vizualizarea se
oprește și oferă o reîncărcare, în loc să rămână stricată.

---

## Terenul

Din start globul este o **sferă netedă**. Asta nu are nevoie de niciun server de elevație și nu
descarcă nimic.

Dacă instalarea a generat relief, globul are denivelări reale. Vedeți [Relief](terrain.md) pentru
ce presupune asta și [Generarea reliefului](../admin/terrain-builds.md) pentru cum o face un
administrator.

Când relieful este configurat dar inaccesibil sau greșit format, scena o spune explicit —
numind care dintre *inaccesibil*, *nu este un set de dale de relief* sau *servit cu compresie
greșită* a întâlnit — și fie revine la o piramidă pe care o deține instalarea, fie desenează un
glob neted. Nu se preface.

---

## Vederea peșterii prin pământ

O topografie este sub un versant, ceea ce este o problemă când vrei s-o privești. Două răspunsuri:

**Arată peștera prin el.** Topografia este desenată peste teren și rămâne vizibilă din orice
unghi. Cât de adâncă este o galerie se vede în culoarea liniei — există o legendă de adâncime,
*Adâncime sub partea de sus a peșterii*.

**Decupează-l.** Terenul de deasupra peșterii este decupat, așa că priviți topografia printr-o
deschidere în suprafață.

Decuparea este sinceră în privința propriilor limite. Dacă deschiderea este văzută din profil,
vă spune să înclinați privirea spre peșteră. Dacă sunteți sub suprafață, vă spune că nu mai
există teren între dumneavoastră și peșteră și să vă ridicați deasupra. Dacă browserul nu poate
decupa deloc, sau nu este încărcată nicio topografie de decupat, o spune și revine la afișarea
peșterii prin teren.

---

## Pereții peșterii selectate

Dacă o peșteră are un **model de pereți `.stl`** încărcat, vizualizarea 3D îl poate desena la
locul lui, sub relief, lângă poligonațiile acelei peșteri.

- Se încarcă **numai pentru peștera pe care o selectați**.
- Stingerea lui din lista de straturi chiar îl eliberează — nu rămâne în memoria grafică
  prefăcându-se stins.
- Panoul vă spune la ce stadiu este: caută un model, se încarcă, desenat (cu numărul de
  triunghiuri), încă se convertește, niciunul încărcat, sau eșuat cu un motiv.

Vedeți [Topografii, poligonații și modele 3D](surveys-and-models.md#pereții-peșterii-stl) pentru
încărcare și pentru declarația de coordonate de care are nevoie un `.stl`.

---

## Camera

| Control | Face |
|---|---|
| **Încadrează peștera** | Potrivește peștera selectată în vizor |
| **Privește drept în jos (plan)** | Vederea în plan |
| **Vedere din nord / sud / est / vest** | Elevații fixe |
| **Elimină perspectiva** | Proiecție ortografică, ca distanțele să se citească la fel aproape și departe |

## Poligonații fără adâncimi

O poligonație ridicată în plan, fără altitudini, este desenată **pe suprafață**, iar scena spune
câte sunt în starea aceea. Nu este desenată la adâncimea zero ca și cum peștera ar fi plată —
asta ar fi o afirmație despre peșteră pe care n-a făcut-o nimeni.

## Straturi raster în 3D

Foile raster georeferențiate pot fi întinse pe glob. Observați diferența față de harta plană:
**pe glob fiecare foaie este aplatizată într-o singură imagine**, așa că se înmoaie la apropiere
mare. Harta plană citește fișierul însuși. Dacă examinați detalii pe o foaie geologică, folosiți
harta 2D.

---

Înrudite: [Relief](terrain.md) · [Spațiul de lucru al hărții](map-workspace.md) ·
[Topografii și modele](surveys-and-models.md)
