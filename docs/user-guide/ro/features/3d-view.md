# Vizualizarea 3D

🇬🇧 [English](../../features/3d-view.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Relief](terrain.md) ·
[Topografii, drumuiri și modele 3D](surveys-and-models.md)

---

Vizualizarea 3D desenează hărțile de bază ale instalării pe un glob, cu topografiile peșterilor
aflate în pământ la adâncimile la care au fost măsurate.

Se deschide din bara de navigare (**Vizualizare 3D**), din hartă (**Arată scena 3D lângă hartă**)
sau într-o fereastră proprie. Pagina se descarcă doar când o deschizi, așa că nu costă nimic
pentru cine n-o folosește niciodată.

## Cerințe și sinceritate în privința lor

Vizualizarea 3D are nevoie de **WebGL 2**. Un browser sau un driver grafic fără el primește o
explicație și un link către harta 2D, nu o pânză neagră moartă.

**Nu se contactează niciun serviciu extern.** Fără relief străin, fără imagini străine, fără
geocodare străină. Motorul 3D este servit de propria ta instalare, iar fundalul este
format din hărțile de bază pe care le-ai configurat — deci o instalare fără ieșire la internet
are nevoie de o hartă de bază la care poate ajunge. Un **serviciu de hărți** (WMS sau WMTS) pe
care instalarea îl listează este oferit și în panoul de straturi al scenei, exact ca pe
[hartă](map-workspace.md#hărți-suprapuse).

Dacă browserul eliberează resursele grafice ale vizualizării de două ori la rând, vizualizarea se
oprește și oferă o reîncărcare, în loc să rămână stricată.

---

## Terenul

Din start globul este o **sferă netedă**. Asta nu are nevoie de niciun server de elevație și nu
descarcă nimic.

Dacă instalarea a generat relief, globul are denivelări reale. Vezi [Relief](terrain.md) pentru
ce presupune asta și [Generarea reliefului](../admin/terrain-builds.md) pentru cum o face un
administrator.

Când relieful este configurat dar inaccesibil sau greșit format, scena o spune explicit —
numind care dintre *inaccesibil*, *nu este un set de dale de relief* sau *servit cu compresie
greșită* a întâlnit — și fie revine la o piramidă pe care o deține instalarea, fie desenează un
glob neted. Nu se preface.

### Alegerea între generări

O instalare poate deține mai multe generări de relief deodată — una grosieră peste întreaga
regiune și una mai fină peste masivul în care lucrezi, de pildă. Scena o desenează pe cea pe care
instalarea o marchează ca implicită. Pentru a desena alta, deschide panoul de straturi și
caută secțiunea **Terenul**: fiecare generare este listată după nivelul ei, data la care s-a
încheiat și zona pe care o acoperă (ca lățime × înălțime în km), cu modelul de altitudine
configurat pe primul loc, când există unul.

Schimbarea este o reîncărcare reală a terenului, iar topografiile se mută odată cu el: fiecare
generare are propriul reper de altitudine, iar scena reatârnă fiecare drumuire, model de
pereți și traseu de suprafața pe care o desenează acum. O generare pe care ai ales-o și care nu
poate fi citită este refuzată cu aceeași notificare pe care o primește o sursă configurată —
scena nu desenează pe tăcute alta sub un control care încă o numește pe cea aleasă. Alegerea
ține cât sesiunea; nu este salvată și nu intră în link.

### Când un relief mai fin acoperă zona în care ești

Când camera se oprește deasupra unei zone acoperite de o generare mai fină decât cea desenată —
sau deasupra oricărei generări, câtă vreme globul este sfera netedă — o notificare mică o oferă:
*Un relief mai fin acoperă această zonă — îl desenăm?* **Desenează-l** face schimbarea de mai
sus; **Nu acum** pune acea generare deoparte pentru restul sesiunii. Scena nu schimbă niciodată
singură: o schimbare reîncarcă terenul în timp ce te miști și modifică altitudinile pe care
stă totul.

---

## Vederea peșterii prin pământ

O topografie este sub un versant, ceea ce este o problemă când vrei s-o privești. Două răspunsuri:

**Arată peștera prin el.** Topografia este desenată peste teren și rămâne vizibilă din orice
unghi. Cât de adâncă este o galerie se vede în culoarea liniei — există o legendă de adâncime,
*Adâncimea sub partea de sus a peșterii*.

**Decupează-l.** Terenul de deasupra peșterii este decupat, așa că privești topografia printr-o
deschidere în suprafață.

Decuparea este sinceră în privința propriilor limite. Dacă deschiderea este văzută din profil,
îți spune să înclini privirea spre peșteră. Dacă ești sub suprafață, îți spune că nu mai
există teren între tine și peșteră și să te ridici deasupra. Dacă browserul nu poate
decupa deloc, sau nu este încărcată nicio topografie de decupat, o spune și revine la afișarea
peșterii prin teren.

---

## Pereții peșterii selectate

Dacă o peșteră are un **model de pereți `.stl`** încărcat, vizualizarea 3D îl poate desena la
locul lui, sub relief, lângă drumuirile acelei peșteri. O peșteră fără un asemenea model este
desenată cu pereții [construiți din `.lox`-ul sau `.3d`-ul ei](surveys-and-models.md#pereți-dintr-un-lox-sau-3d),
acolo unde topografia conține schițe sau dimensiuni de galerie măsurate din care să fie construiți.

- Implicit se încarcă **numai pentru peștera pe care o selectezi**. Sub comutatorul **Pereții
  peșterilor** din lista de straturi alegi între aceasta — *Peștera selectată* — și *Toate
  peșterile din vedere*, descrisă mai jos.
- Stingerea lui din lista de straturi chiar îl eliberează — nu rămâne în memoria grafică
  prefăcându-se stins.
- Panoul îți spune la ce stadiu este: caută un model, se încarcă, desenat, încă se convertește,
  niciunul încărcat, sau eșuat cu un motiv.
- Cât timp pereții se descarcă, scena spune cât de mari sunt — *Se încarcă pereții — 54.7 MB.* —
  astfel încât un model de cincizeci de megaocteți și unul de trei sute de kiloocteți sunt
  așteptări diferite, nu aceeași tăcere. Odată desenați, se arată și mărimea, și numărul de
  triunghiuri. Un model convertit înainte ca serverul să înceapă să măsoare mărimile este descris
  doar prin numărul de triunghiuri.
- **Pereții mari sunt întâi întrebați.** Pereții mai mari de 50 MB — un cost real pe abonamentul
  de date al unui telefon — nu se descarcă la selectare: scena spune *Acești pereți au 54.7 MB de
  descărcat.* cu un buton **Descarcă-i**, și nu se aduce nimic până nu îl apeși. Odată acceptați,
  aceiași pereți nu mai sunt întrebați cât timp pagina rămâne deschisă. Mărimea este valoarea
  implicită a administratorului, iar tu o poți stabili pe a ta pentru acest
  browser în [Setări › Avansat](account-and-settings.md#avansat), la **Întreabă peste (MB)** (0
  întreabă înaintea fiecărei descărcări).

Vezi [Topografii, drumuiri și modele 3D](surveys-and-models.md#pereții-peșterii-stl) pentru
încărcare și pentru declarația de coordonate de care are nevoie un `.stl`.

## Fotografii la stațiile lor

Pentru **peștera selectată**, fiecare fotografie legată de o stație a uneia dintre topografiile ei
este marcată la acea stație cu un aparat foto — aceleași locuri pe care le marchează
vizualizatorul de topografii — la adâncimea stației, așa că stă printre liniile topografiei acolo
unde a fost făcută fotografia. Apasă un aparat foto pentru a deschide fotografiile acelei stații;
selecția rămâne pe peșteră. Comutatorul **Fotografii la stațiile lor** din lista de straturi le
stinge, iar rândul de sub el spune câte sunt. Se arată doar fotografiile și pozițiile topografice
pe care ai voie să le vezi.

## Pereții tuturor peșterilor din vedere

Alegerea **Toate peșterile din vedere** sub comutatorul **Pereții peșterilor** desenează pereții
peșterilor la care privește camera, nu doar ai celei selectate. Un model de pereți se descarcă
întreg și se ține în memoria grafică atât timp cât este desenat — de obicei câteva sute de
kiloocteți, zeci de megaocteți pentru un sistem mare — așa că acest mod lucrează între limite și
îți spune când a atins una:

- **Apropie mai întâi.** Sub un anumit nivel de zoom (14, dacă administratorul nu l-a schimbat)
  nu se încarcă nimic, iar scena spune *Apropie vederea pentru a vedea pereții peșterilor din ea.*
  O vedere largă cuprinde peșterile unui întreg masiv, fiecare fiind un punct care ar costa cât
  toată mărimea ei.
- **Cele mai apropiate întâi, până la o limită.** Peșterile cele mai apropiate de mijlocul
  vederii sunt luate primele, până când s-ar depăși fie un număr de peșteri (12), fie o mărime
  totală de descărcat (64 MB). Peștera selectată este luată întotdeauna prima, oriunde s-ar afla
  în vedere.
- **Nu arată niciodată o parte în tăcere.** Panoul și un rând deasupra scenei spun pentru câte
  peșteri sunt desenați pereții din câte sunt în vedere și ce le-a lăsat pe celelalte deoparte —
  *Peșteri din vedere cu pereții desenați: 3 din 7 — restul ar depăși 64 MB.* Mută sau apropie
  vederea spre peșterile care lipsesc și le vine rândul.
- **Ce iese din vedere este eliberat.** Pereții peșterilor de la care te îndepărtezi sunt
  eliberați, nu ascunși, iar stingerea pereților — sau revenirea la *Peștera selectată* — îi
  eliberează pe toți.
- O peșteră a cărei poziție exactă îți este închisă nu este desenată și nu este numărată.

Pereții fiecărei peșteri stau pe drumuirile acelei peșteri, exact ca în celălalt mod. Cele trei
limite sunt setări ale instalării; vezi `SILEXGIS__Map__MeshesInView…` în
[ghidul de instalare](../../../INSTALL.md#configuration-reference). Fiecare persoană le poate
schimba pentru propriul browser, până la plafonul instalării, în
[Setări › Avansat](account-and-settings.md#avansat) — panoul trimite acolo cât timp acest mod
este ales.

## Urmarea filtrului hărții

Scena citește filtrul hărții, ca harta plană: sub un filtru desenează intrările, fenomenele de
suprafață, drumuirile și pereții peșterilor pe care filtrul le păstrează, și pe nicio alta. Un
filtru care nu mai funcționează cât privești golește ce a restrâns, în loc să lase vechiul desen în
picioare. Vezi [Filtre pe hartă](search-and-filters.md#filtre-pe-hartă).

---

## Camera

| Control | Face |
|---|---|
| **Încadrează peștera din vedere** | Potrivește peștera selectată în vizor |
| **Privire verticală (plan)** | Vederea în plan |
| **Vedere dinspre nord / sud / est / vest** | Elevații fixe |
| **Elimină perspectiva** | Proiecție ortografică, ca distanțele să se citească la fel aproape și departe |
| **Salvează imaginea** | Descarcă ceea ce arată scena, ca imagine |

**Salvează imaginea** descarcă vederea ca PNG, cu numele format din data și ora la care a fost
făcută (`silexgis-3d-20261006-140509.png`), la rezoluția întreagă a ecranului. Mențiunile de
sursă ale hărții de bază, ale oricărui strat suprapus aprins și ale modelului de elevație sunt
scrise de-a lungul marginii de jos a imaginii: licențele acelor surse cer ca mențiunea să
însoțească plăcile lor, iar în pagină ea este desenată lângă scenă, nu în ea. Butoanele,
etichetele cu nume și notele de deasupra scenei nu fac parte din imagine. Dacă imaginea nu poate
fi făcută, scena o spune în loc să salveze un fișier gol.

## Drumuiri fără adâncimi

O drumuire ridicată în plan, fără altitudini, este desenată **pe suprafață**, iar scena spune
câte sunt în starea aceea. Nu este desenată la adâncimea zero ca și cum peștera ar fi plată —
asta ar fi o afirmație despre peșteră pe care n-a făcut-o nimeni.

## Fundaluri GeoTIFF și vectoriale în 3D

Un fundal sau o hartă suprapusă pe care instalarea o listează ca **GeoTIFF** — un GeoTIFF
optimizat pentru cloud, cum ar fi o umbrire de relief LiDAR, o ortofotografie sau o hartă
geologică — este desenat pe glob dală cu dală, din propriile niveluri de detaliu ale fișierului,
așa că rămâne la fel de clar în scenă ca pe hartă. Fișierul este citit doar după ce îl pornești și
doar porțiunile aflate în vedere.

Un **fundal vectorial** — drumuri, râuri și nume desenate într-un stil, cum sunt stilurile
OpenFreeMap — este desenat pe glob de același motor ca harta, în fiecare dală, cu tot cu etichete.
Etichetele stau pe teren și se rotesc odată cu el. Acolo unde două dale vecine nu cad de acord care
dintre două etichete înghesuite să fie lăsată deoparte, o etichetă poate apărea tăiată la marginea
unei dale.

Panoul de straturi spune când unul dintre acestea nu este desenat cum te-ai aștepta:

- *Nedesenat în 3D*, cu motivul — un GeoTIFF care nu poate fi citit, care nu spune unde se află pe
  teren sau al cărui sistem de coordonate nu poate fi folosit aici; un stil care nu poate fi
  încărcat. O astfel de hartă de bază nu este oferită, iar scena revine la cea implicită a
  instalării.
- *Îl desenează doar de aproape* — un GeoTIFF fără copii micșorate ale sale (overviews). De departe,
  scena ar trebui să citească întregul fișier pentru fiecare dală, așa că așteaptă să te apropii.
- *Desenat în 3D ca …* — un fundal vectorial desenat de fundalul-imagine pe care instalarea l-a
  numit înlocuitorul lui. Asta se întâmplă pe un calculator cu cel mult două nuclee de procesor sau
  cel mult doi gigaocteți de memorie, unde desenarea unui stil dală cu dală ar sacada scena, și
  ori de câte ori stilul nu poate fi încărcat. Alegerea fundalului îl alege în continuare și pe hartă.

## Straturi raster în 3D

Foile raster georeferențiate pot fi întinse pe glob. Observă diferența față de harta plană:
**pe glob fiecare foaie este aplatizată într-o singură imagine**, așa că se înmoaie la apropiere
mare. Harta plană citește fișierul însuși. Dacă examinezi detalii pe o foaie geologică, folosește
harta 2D.

## Trasee din fișiere importate

Fișierele importate din ecranul de geodate — un traseu GPX, un contur KML — pot fi afișate și în
vizualizarea 3D. Deschide panoul de straturi și bifează fișierul
 la **Fișiere importate**;
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
