# Zone de lucru

🇬🇧 [English](../../features/work-areas.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Ture](trips.md) ·
[Fenomene de suprafață](surface-features.md)

---

**Zone de lucru** stă la primul nivel al barei, lângă hartă — pentru că este un mod de a privi
terenul, nu un registru al ce se află în el, și pentru că de aici își încep majoritatea
cititorilor sezonul.

---

## Ce este de fapt o zonă de lucru

O întindere de teren pe care lucrează un club: un masiv, o zonă carstică, un sistem de văi.

Tehnic, este **un obiect de tipul *zonă de lucru*** — nu o etichetă, nu o listă în care adaugi
lucruri. Distincția contează în practică:

> O etichetă este text liber la nivel de instalare. Redenumirea sau ștergerea celei care însemna
> „aici lucrăm" ar goli panoul care le listează, tăcut și definitiv, și nimic nu ar semnala
> eroarea. Un **tip** nu poate fi redenumit pe sub picioarele programului.

Deci *a fi zonă de lucru este un fapt despre înregistrare*, nu despre lista în care s-a nimerit
cineva s-o adauge.

Fiind un obiect obișnuit, poartă nume, descriere, formă, etichete, [legături](links.md), istoric
și permisiuni ca orice altceva — și este filtrată exact de aceeași regulă de acces ca orice alt
obiect. **Nu există o a doua cale de acces aici.**

## Niveluri

Nivelurile de sub una — o vale într-un masiv, un sector într-o vale — **nu sunt un al doilea fel
de lucru**. Sunt zone de lucru aflate în interiorul alteia prin
[ierarhia obișnuită de conținere](surface-features.md#ierarhie-părinți-și-copii).

Ceea ce înseamnă că **o subzonă este o zonă de lucru în drept propriu din clipa în care o
deschizi**, cu propriile subzone dedesubt.

## Crearea uneia

Desenează o zonă pe hartă și înregistreaz-o ca zonă de lucru — un obiect de tipul zonă de lucru.

Dacă nu există încă niciuna, pagina o spune: *„Nicio zonă de lucru nu a fost marcată încă.
Desenează o zonă pe hartă și înregistreaz-o ca zonă de lucru."*

**O zonă poate exista înainte de conturul ei.** O zonă de lucru fără formă desenată este listată
în continuare pe panou; vederea de ansamblu pur și simplu o lasă în afara hărții, în loc să-i
inventeze un contur.

---

## Pagina de ansamblu

O hartă proprie — nu harta spațiului de lucru — care arată **fiecare zonă de la un nivel**,
deosebite prin culoare și denumite pe hartă, cu nivelul de dedesubt la un clic distanță.

| Control | |
|---|---|
| **Toate zonele** / **La acest nivel** | Răsfoiești tot setul sau doar nivelul curent |
| **Firul de navigare** | Lanțul de sus până la ce ai deschis |
| **Pe hartă** | Deschide zona în spațiul de lucru al hărții |
| **Vezi toate** | |
| **Zone în interior: n** | Ți se spune *înainte* să se ceară formele acelui nivel, ca să știi că mai există un nivel de deschis |

Numele sunt desenate cu un halou, pentru că stau la fel de des peste imagini aeriene ca peste un
fundal simplu. Harta de ansamblu desenează doar fundaluri obișnuite din dale; un serviciu de hărți
(WMS sau WMTS) oferit de instalare apare în [spațiul de lucru al hărții](map-workspace.md#hărți-suprapuse)
și în vizualizarea 3D, nu aici.

### Un lucru care arată a defect și nu este

**O zonă al cărei părinte nu îl poți citi apare la primul nivel.**

Serverul declară un părinte doar când ai voie să citești și acel părinte — deci o zonă
cuibărită sub ceva ce nu poți vedea trebuie să apară *undeva*. Eliminată, ar dispărea complet
din vederea de ansamblu: vizibilă pentru server, invizibilă pentru cititorul ei.

Așa că doi oameni pot vedea pe bună dreptate aceeași zonă la niveluri diferite ale arborelui, și
ambele vederi sunt corecte.

### Dacă sunt mai multe decât încap

*„Nu sunt afișate toate zonele de lucru: instalația conține mai multe decât listează această
vizualizare."* Limita este declarată, nu lăsată să fie dedusă din numărătoare.

---

## Unde se folosesc zonele de lucru

**Pe o tură.** Zonele de lucru sunt primul dintre câmpurile
**[Ce a făcut tura](trips.md#ce-a-făcut-tura)** — terenul pe care a lucrat o tură. O zonă de
lucru este arătată acolo **împreună cu ce o conține**, ceea ce deosebește două sectoare cu
același nume.

**Pe o tabără.** O tabără are propria zonă de lucru, desenată pe harta ei alături de turele pe
care le poți citi.

**Pe panoul de bord.** Zonele de lucru sunt unul dintre panourile pe care le poate purta.

Asta face un sezon lizibil după aceea: ce teren a fost atins, cât de des și de către cine — în
măsura în care cititorul are voie să știe.

---

Înrudite: [Ture](trips.md) · [Tabere](camps.md) ·
[Spațiul de lucru al hărții](map-workspace.md) ·
[Fenomene de suprafață](surface-features.md)
