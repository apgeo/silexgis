# Vederi salvate, panouri și ferestre multiple

🇬🇧 [English](../../features/saved-views-and-windows.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Spațiul de lucru al hărții](map-workspace.md)

---

Aplicația este construită pentru un ecran mare și pentru mai multe. Pagina aceasta acoperă
salvarea unei configurații de hartă, desprinderea panourilor în ferestre proprii și aranjarea
panoului de selecție.

---

## Vederi salvate

O **vedere salvată** este o configurație de hartă denumită: extinderea, straturile, filtrele ei.

| Acțiune | |
|---|---|
| **Salvează vederea curentă** | Denumiți-o și este a dumneavoastră |
| **Link de partajare** | Dați vederea cuiva fără cont — linkul ei de copiat și același link ca cod QR |
| **Exportă imaginea hărții** | O poză de lipit într-un document |

Vederile dumneavoastră salvate apar pe **panoul de bord**, la *Vederile mele salvate*.

**Un loc scris în filtrul hărții nu se salvează niciodată într-o vedere** — un punct introdus, un
poligon lipit sau locul de la care se măsoară *Cel mai apropiat*. Salvarea unei astfel de vederi
este refuzată, cu motivul: *„…o vedere salvată nu le păstrează niciodată. Măsurați în schimb de
la un obiect sau de la o zonă numită, ori aplicați un filtru salvat."* Vedeți
[Filtre pe hartă](search-and-filters.md#filtre-pe-hartă).

**Partajarea unei vederi.** Apăsarea **Link de partajare** pe o vedere o face partajabilă și îi
arată linkul. Dialogul spune ce arată linkul și avertizează când vederea este restrânsă de un
filtru, ceea ce lasă peșterile și elementele în afara linkului — *„Această vedere este
restrânsă de un filtru salvat sau de un filtru venit dintr-un link, pe care un link nu îl poate
aplica…"* — sau când desenează straturi din filtre salvate, pe care un link nu le arată
niciodată. O vedere își păstrează linkul: apăsarea din nou a **Link de partajare** îl arată din
nou.

O vedere partajată se deschide pentru un vizitator anonim peste **fundalul cu care a fost
salvată** — sau, dacă acela cere o cheie de acces ori este o sursă pe care instalarea o ține
departe de linkuri (straturile Google, imaginile Sentinel-2 cu licență necomercială), peste
fundalul implicit al instalării. Dacă vederea le avea pornite, arată și **intrări de peșteri și
elemente de suprafață, în patru condiții**:

- **doar înregistrări care sunt ele însele Public.** O peșteră contează după propria
  vizibilitate, iar intrările ei o urmează; un element contează după a sa. O peșteră Private
  pusă într-o zonă carstică Public *nu* apare, deși orice cont o poate citi prin zonă;
- **doar în zona vederii**: ce arată vederea salvată pe un ecran mare, plus cam o lățime de
  ecran de margine pe fiecare parte, și cel mult două niveluri de zoom mai departe decât a fost
  salvată. Harta din pagina partajată se oprește la această zonă;
- **restrânse cum era vederea**: o vedere salvată cu un filtru de etichetă arată doar ce lăsa
  filtrul să treacă, iar o vedere restrânsă de un filtru salvat sau de orice alt filtru nu arată
  nicio peșteră și niciun element, pentru că în spatele unui link nu este nimeni ca care să ruleze
  filtrul. Straturile desenate din filtre salvate nu se arată niciodată printr-un link;
- **protecțiile țin mai strâns decât pentru orice cont**: o peșteră sau un element protejat —
  protejat el însuși ori aflat în ceva protejat — nu apare deloc, nici măcar aproximativ, și nu
  intră în numărul unui grup, chiar dacă instalarea voastră îl lasă pe orice cont să-l vadă exact.
  Pagina arată doar înregistrările publice neprotejate, ca harta taberei și harta fotografiilor,
  astfel încât nimic de pe ea nu arată că ar mai fi și altceva acolo. Nici nimic de pe pagină nu
  identifică sau nu deschide o înregistrare.

Un clic pe un marcaj arată numele lui; un clic pe un grup apropie harta. Nimic de pe pagină nu
este desenat într-o poziție aproximativă. Liniile topografice, fotografiile, turele și fișierele
importate nu apar niciodată pe o vedere partajată, iar din vederea salvată vizitatorul nu
primește nimic în afară de nume, locul în care privea și care dintre cele două straturi erau
pornite.

**Ștergerea vederii îi încheie linkul**, imediat; lista vederilor salvate nu are un control
separat pentru retragerea linkului cu păstrarea vederii. Un administrator poate opri și datele
pe vederile partajate pentru toată instalarea (`SILEXGIS__Map__SharedViewData=false`), iar
atunci fiecare link arată doar fundalul. Dacă vederea unui link a fost ștearsă sau nu a existat:
*„Această vedere partajată nu există sau a fost revocată."*

Vederile salvate pot fi și [legate](links.md) de alte înregistrări, ca orice altceva.

---

## Panouri desprinse

Mai multe părți ale spațiului de lucru se deschid **într-o fereastră proprie** și rămân
sincronizate cu fereastra principală:

| Panou | |
|---|---|
| **Registrul peșterilor** | Tabelul, lângă hartă |
| **Selecție** | Panoul de detalii |
| **Vizualizator 3D** | Vizualizatorul topografic (CaveView) |
| **Scena 3D** | Globul |
| **Un text adnotat cu legături** | De citit lângă harta pe care o conduce |

Aranjamentul canonic pe două monitoare: **harta într-o fereastră, un tabel sincronizat și
vizualizarea 3D în alta.** În registrul peșterilor desprins, indicarea unei peșteri o
încercuiește pe harta din cealaltă fereastră, iar alegerea a ceva pe hartă îi marchează rândul —
vedeți [Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă).

**Ce aranjați într-o fereastră desprinsă se păstrează în contul dumneavoastră**, exact ca în
fereastra principală, iar o schimbare făcută chiar înainte să închideți o pagină se păstrează și
ea, în loc să se piardă odată cu pagina.

Fereastra vizualizatorului 3D arată *„Selectează o peșteră cu model 3D pentru a-l afișa aici."*
până selectați una în cealaltă fereastră. Dacă scena 3D este deja deschisă în alt panou al
aceleiași ferestre, o spune, în loc să pornească o a doua.

---

## Aranjarea panoului de selecție

Panoul de detalii are secțiuni: **Detalii · Etichete · Fișiere · Legături · Istoric · Permisiuni ·
Text**.

Puteți:

- să le **reordonați** — prin tragere, sau ținând Alt și apăsând săgețile sus/jos,
- să **ascundeți** o secțiune individual și s-o readuceți (cele ascunse sunt listate),
- să **reveniți la aranjarea implicită**,
- să stabiliți **spațierea** (densitatea),
- să alegeți dacă panoul **împinge harta lateral** sau **plutește peste hartă**.

Și puteți **salva aranjări denumite** și comuta între ele — una pentru topografie, una pentru
lucrul de arhivă. Sunt listate la **Aranjări salvate**.

**Aranjările vă urmează contul** în orice browser în care vă autentificați — ordinea secțiunilor,
ce este ascuns, spațierea, fixarea și aranjările denumite deopotrivă. Contul păstrează o cantitate
limitată de astfel de preferințe; dacă era plin când ați salvat o aranjare, aceea este păstrată
doar în acest browser și marcată **Doar în acest browser** în listă, cu motivul când o indicați.
Ștergeți o aranjare pe care n-o mai folosiți, iar cea marcată ajunge în cont odată cu acea
schimbare.

Dacă ascundeți tot, o spune — *„Toate secțiunile sunt ascunse."* — în loc să arate o cutie
goală.

## Navigarea într-o selecție

**Înapoi** și **Înainte** vă mută prin istoricul selecției — *„Înapoi la selecția anterioară"*.

Cu mai multe lucruri selectate: **n selectate**, cu marcaje *Lizibile aici* / *Amestecate*, și
**Încadrează-le pe toate** și **Golește selecția** (**Etichetează-le pe toate** apare, dar nu este
încă disponibil). *„Ține Ctrl sau Shift în timp ce dai clic pe hartă pentru a adăuga la
selecție."*

O selecție umplută din **Selectează pe hartă tot ce s-a găsit (n)** al unei liste cuprinde cel
mult 200, o spune când lista a găsit mai multe — *„Primele 200 din n găsite de listă."* — și, cât
timp este încă exact acel rezultat, oferă **Salvează ca filtru…**. Vedeți
[Între o listă și hartă](search-and-filters.md#între-o-listă-și-hartă).

Dacă nimic din ce ați selectat nu poate fi citit aici, o spune, în loc să arate un panou gol.

---

## Panoul de bord

**Panou de bord** este cealaltă vedere de ansamblu:

- **Numere** — peșteri, elemente, jurnale de tură, fișiere de geodate.
- **Activitate recentă** — ce s-a adăugat, pe feluri (peșteră, jurnal de tură, element, intrare,
  poligonație, tabără).
- **Urmează** — următoarele dumneavoastră câteva ture. *„Nimic în viitorul apropiat. O tură apare
  aici imediat ce cineva vă invită la una sau vă pune în echipă."* Cu un link către *Toate turele
  mele*.
- **Vederile mele salvate**.
- **Acțiuni rapide**, inclusiv **Importă fișier**.

Puteți face panoul de bord pagina pe care se deschide aplicația — Setări → Accesibilitate →
*Deschide aplicația pe*. Acea alegere rămâne pe dispozitivul pe care ați făcut-o.

---

Înrudite: [Spațiul de lucru al hărții](map-workspace.md) · [Vizualizarea 3D](3d-view.md) ·
[Partajare](sharing-and-public-pages.md)
