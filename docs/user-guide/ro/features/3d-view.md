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

### Alegerea între generări

O instalare poate deține mai multe generări de relief deodată — una grosieră peste întreaga
regiune și una mai fină peste masivul în care lucrați, de pildă. Scena o desenează pe cea pe care
instalarea o marchează ca implicită. Pentru a desena alta, deschideți panoul de straturi și
căutați secțiunea **Terenul**: fiecare generare este listată după nivelul ei, data la care s-a
încheiat și zona pe care o acoperă (ca lățime × înălțime în km), cu modelul de altitudine
configurat pe primul loc, când există unul.

Schimbarea este o reîncărcare reală a terenului, iar topografiile se mută odată cu el: fiecare
generare are propriul reper de altitudine, iar scena reatârnă fiecare poligonație, model de
pereți și traseu de suprafața pe care o desenează acum. O generare pe care ați ales-o și care nu
poate fi citită este refuzată cu aceeași notificare pe care o primește o sursă configurată —
scena nu desenează pe tăcute alta sub un control care încă o numește pe cea aleasă. Alegerea
ține cât sesiunea; nu este salvată și nu intră în link.

### Când un relief mai fin acoperă zona în care sunteți

Când camera se oprește deasupra unei zone acoperite de o generare mai fină decât cea desenată —
sau deasupra oricărei generări, câtă vreme globul este sfera netedă — o notificare mică o oferă:
*Un relief mai fin acoperă această zonă — îl desenăm?* **Desenează-l** face schimbarea de mai
sus; **Nu acum** pune acea generare deoparte pentru restul sesiunii. Scena nu schimbă niciodată
singură: o schimbare reîncarcă terenul în timp ce vă mișcați și modifică altitudinile pe care
stă totul.

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

- Implicit se încarcă **numai pentru peștera pe care o selectați**. Sub comutatorul **Pereții
  peșterilor** din lista de straturi alegeți între aceasta — *Peștera selectată* — și *Toate
  peșterile din vedere*, descrisă mai jos.
- Stingerea lui din lista de straturi chiar îl eliberează — nu rămâne în memoria grafică
  prefăcându-se stins.
- Panoul vă spune la ce stadiu este: caută un model, se încarcă, desenat, încă se convertește,
  niciunul încărcat, sau eșuat cu un motiv.
- Cât timp pereții se descarcă, scena spune cât de mari sunt — *se încarcă pereții — 54.7 MB* —
  astfel încât un model de cincizeci de megaocteți și unul de trei sute de kiloocteți sunt
  așteptări diferite, nu aceeași tăcere. Odată desenați, se arată și mărimea, și numărul de
  triunghiuri. Un model convertit înainte ca serverul să înceapă să măsoare mărimile este descris
  doar prin numărul de triunghiuri.

Vedeți [Topografii, poligonații și modele 3D](surveys-and-models.md#pereții-peșterii-stl) pentru
încărcare și pentru declarația de coordonate de care are nevoie un `.stl`.

## Pereții tuturor peșterilor din vedere

Alegerea **Toate peșterile din vedere** sub comutatorul **Pereții peșterilor** desenează pereții
peșterilor la care privește camera, nu doar ai celei selectate. Un model de pereți se descarcă
întreg și se ține în memoria grafică atât timp cât este desenat — de obicei câteva sute de
kiloocteți, zeci de megaocteți pentru un sistem mare — așa că acest mod lucrează între limite și
vă spune când a atins una:

- **Apropiați mai întâi.** Sub un anumit nivel de zoom (14, dacă administratorul nu l-a schimbat)
  nu se încarcă nimic, iar scena spune *Apropie vederea pentru a vedea pereții peșterilor din ea*.
  O vedere largă cuprinde peșterile unui întreg masiv, fiecare fiind un punct care ar costa cât
  toată mărimea ei.
- **Cele mai apropiate întâi, până la o limită.** Peșterile cele mai apropiate de mijlocul
  vederii sunt luate primele, până când s-ar depăși fie un număr de peșteri (12), fie o mărime
  totală de descărcat (64 MB). Peștera selectată este luată întotdeauna prima, oriunde s-ar afla
  în vedere.
- **Nu arată niciodată o parte în tăcere.** Panoul și un rând deasupra scenei spun pentru câte
  peșteri sunt desenați pereții din câte sunt în vedere și ce le-a lăsat pe celelalte deoparte —
  *Peșteri din vedere cu pereții desenați: 3 din 7 — restul ar depăși 64 MB*. Mutați sau apropiați
  vederea spre peșterile care lipsesc și le vine rândul.
- **Ce iese din vedere este eliberat.** Pereții peșterilor de la care vă îndepărtați sunt
  eliberați, nu ascunși, iar stingerea pereților — sau revenirea la *Peștera selectată* — îi
  eliberează pe toți.
- O peșteră a cărei poziție exactă vă este închisă nu este desenată și nu este numărată.

Pereții fiecărei peșteri stau pe poligonațiile acelei peșteri, exact ca în celălalt mod. Cele trei
limite sunt setări ale instalării; vedeți `SILEXGIS__Map__MeshesInView…` în
[ghidul de instalare](../../../INSTALL.md#configuration-reference).

---

## Camera

| Control | Face |
|---|---|
| **Încadrează peștera** | Potrivește peștera selectată în vizor |
| **Privește drept în jos (plan)** | Vederea în plan |
| **Vedere din nord / sud / est / vest** | Elevații fixe |
| **Elimină perspectiva** | Proiecție ortografică, ca distanțele să se citească la fel aproape și departe |
| **Salvează imaginea** | Descarcă ceea ce arată scena, ca imagine |

**Salvează imaginea** descarcă vederea ca PNG, cu numele format din data și ora la care a fost
făcută (`silexgis-3d-20261006-140509.png`), la rezoluția întreagă a ecranului. Mențiunile de
sursă ale hărții de bază, ale oricărui strat suprapus aprins și ale modelului de elevație sunt
scrise de-a lungul marginii de jos a imaginii: licențele acelor surse cer ca mențiunea să
însoțească plăcile lor, iar în pagină ea este desenată lângă scenă, nu în ea. Butoanele,
etichetele cu nume și notele de deasupra scenei nu fac parte din imagine. Dacă imaginea nu poate
fi făcută, scena o spune în loc să salveze un fișier gol.

## Poligonații fără adâncimi

O poligonație ridicată în plan, fără altitudini, este desenată **pe suprafață**, iar scena spune
câte sunt în starea aceea. Nu este desenată la adâncimea zero ca și cum peștera ar fi plată —
asta ar fi o afirmație despre peșteră pe care n-a făcut-o nimeni.

## Straturi raster în 3D

Foile raster georeferențiate pot fi întinse pe glob. Observați diferența față de harta plană:
**pe glob fiecare foaie este aplatizată într-o singură imagine**, așa că se înmoaie la apropiere
mare. Harta plană citește fișierul însuși. Dacă examinați detalii pe o foaie geologică, folosiți
harta 2D.

## Trasee din fișiere importate

Fișierele importate din ecranul de geodate — un traseu GPX, un contur KML — pot fi afișate și în
vizualizarea 3D. Deschideți panoul de straturi și bifați fișierul la **Fișiere importate**;
fiecare fișier are propria estompare. Alegerea este comună cu harta plană: un fișier pornit
într-o vizualizare este pornit și în cealaltă, desenat în aceeași culoare.

Pe glob un fișier înseamnă liniile lui — trasee și contururi; punctele lui nu sunt desenate. Un
traseu care a înregistrat altitudini este desenat la ele, pe același teren ca topografiile, când
instalarea are relief. Un traseu care nu a înregistrat niciuna, sau ale cărui altitudini sunt
toate zero, este **așezat pe teren**: urmează versantul în loc să stea la nivelul mării, sub el.
Un clic pe un traseu nu selectează nimic — nu are o pagină proprie.

---

Înrudite: [Relief](terrain.md) · [Spațiul de lucru al hărții](map-workspace.md) ·
[Topografii și modele](surveys-and-models.md)
