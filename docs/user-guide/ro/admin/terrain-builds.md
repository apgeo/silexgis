# Generarea reliefului

🇬🇧 [English](../../admin/terrain-builds.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Relief](../features/terrain.md) ·
[Vizualizarea 3D](../features/3d-view.md)

---

**Administrare → Relief.** Transformarea datelor de elevație în terenul pe care îl desenează
[scena 3D](../features/3d-view.md).

> *„Desenați un dreptunghi, spuneți de unde vin datele lui de elevație, iar instalarea generează
> terenul pe care îl desenează scena 3D."*

Complet opțional. Fără el globul este o sferă netedă, ceea ce nu are nevoie de niciun server de
elevație și nu descarcă nimic.

---

## Înainte de a începe: serviciul suplimentar

**Transformarea rasterelor în dale are nevoie de un serviciu propriu, pe care o instalare
obișnuită nu îl rulează.**

Obținerea datelor de elevație și pregătirea lor funcționează ca atare. Generarea dalelor nu. Dacă
instalarea dumneavoastră nu rulează acel serviciu, o generare **se oprește la pasul de generare**,
iar pagina Relief vă arată exact ce să faceți — singurul rând de adăugat în fișierul de mediu de
lângă fișierele de instalare, și comanda de rulat din acel dosar.

Observați și ce mai spune: tot ce a fost înainte de acel pas **s-a întâmplat totuși**, iar ce a
obținut și a pregătit generarea este încă pe disc sub ea — dar **acea generare anume nu poate fi
dusă mai departe**. Porniți una nouă odată ce serviciul rulează.

---

## Realizarea unei generări

### 1. Desenați dreptunghiul

**Desenează dreptunghi**, trageți-l pe hartă. Raportează extinderea (vest, sud, est, nord) și
suprafața în grade pătrate.

Există un maxim. Un dreptunghi mai mare decât generează instalarea dintr-o dată este refuzat —
generați-l ca mai multe zone mai mici.

### 2. Spuneți de unde vin datele de elevație

Trei surse, care se pot combina:

**Obține acoperirea pentru acest dreptunghi.** Date de elevație de treizeci de metri care acoperă
toată suprafața uscatului, aduse de aplicație. *„Marea nu returnează nimic, ceea ce este un
răspuns normal, nu o eroare."*

**Rastere de pe acest calculator.** Lăsați rastere de elevație înăuntru, până la 512 MB fiecare.
Orice mai mare aparține unui dosar de pe server. Formularul nu vă lasă să porniți o generare cât
timp încărcările sunt încă în drum — numește fișierele care nu au sosit încă.

**Un dosar de pe server.** Unul pe care operatorul l-a listat ca lizibil, sau unul de sub el. Se
citește tot ce din el arată a raster de elevație. Dacă nu este configurat niciunul, o spune.

### 3. Creditul

**Cui aparțin datele.**

> *„Este scris în suprafața publicată, deci o generare nu trebuie să poarte niciodată un credit
> care nu este al ei."*

Obligatoriu dacă ați furnizat chiar dumneavoastră rastere. Înregistrați și **licența**.

### 4. Adâncimea

Cât de fin merge piramida de dale. Formularul descrie fiecare bandă, ca să nu ghiciți:

| Bandă | |
|---|---|
| **Grosieră** | O formă largă a peisajului. Rapidă, mică pe disc, dar un versant se citește ca o singură pantă |
| **Acoperire** (≈ nivelul 13) | Atâta detaliu cât conține efectiv acoperirea de treizeci de metri. **Valoarea de lucru implicită** |
| **Fină** | Mai fină decât acoperirea obținută. Merită cerută doar acolo unde rastere de câțiva metri acoperă dreptunghiul |
| **Topografică** | Ce merită date topografice de jumătate de metru |

> **Fiecare nivel aproximativ împătrește și dalele scrise, și timpul necesar.**

Dacă cereți mai mult decât conțin sursele, formularul spune **„Nimic de aici nu poate umple acele
niveluri"** — nivelurile mai adânci nu sunt nici refuzate, nici eliminate tacit; pur și simplu
adaugă spațiu pe disc fără detaliu.

### 5. Înălțimile

**Înălțimile se măsoară de la:** *Deasupra nivelului mării* (ortometrice) sau *Deasupra
elipsoidului*.

> **Greșeala ridică sau coboară întreaga suprafață cu zeci de metri.**

Pentru înălțimi elipsoidale, dați **înălțimea geoidului** — cât de sus stă acolo suprafața locală
a nivelului mării față de elipsoid.

### 6. Pornirea

**Pornește generarea.** Se pune la coadă.

---

## Urmărirea unei generări

Stare: **În așteptare · În rulare · Terminată · Eșuată.**

Faze, în ordine:

| Fază | |
|---|---|
| *Își așteaptă rândul* | Nimic nu a început |
| **Obținere** | Se aduce acoperirea |
| **Pregătire** | Se convertesc rasterele |
| **Generare** | Se fac dalele — pasul care are nevoie de serviciul suplimentar |
| **Verificare** | Se recitesc dalele și se verifică dacă sunt întregi |
| **Publicare** | |

Pagina de detaliu a unei generări arată din **ce a fost făcută** și **ce a spus unealta** —
jurnalul, unde o eroare se explică singură.

---

## Administrarea generărilor

Lista **Generări** arată starea, care este **cea desenată**, suprafața, adâncimea, când a fost
trimisă și **mărimea pe disc** — *„tot ce a lăsat în urmă generarea: dalele publicate și rasterele
convertite păstrate lângă ele."*

| Acțiune | |
|---|---|
| **Desenează asta** | Scena desenează acum această generare |
| **Oprește desenarea** | Scena desenează din nou teren gol |
| **Șterge** | Elimină generarea și spațiul pe care îl ține |

Două refuzuri de reținut:

- **Nu puteți șterge generarea pe care o desenează scena.** Alegeți alt relief sau opriți
  desenarea ei mai întâi.
- **Nu puteți șterge o generare în curs.** Fișierele ei se scriu chiar acum; poate fi eliminată
  după ce se oprește, indiferent cum se oprește.

---

## Probleme frecvente

| Mesaj | Înseamnă |
|---|---|
| *Acea zonă este deja în generare* | Așteptați-o |
| *O generare trebuie făcută din ceva* | Obțineți acoperirea sau furnizați rastere |
| *Acel fișier nu este un raster de elevație pe care îl citește această instalare* | Format greșit |
| *O dală de elevație spune unde se află doar prin cum se numește* | Un `.hgt` trebuie numit ca `N45E024.hgt`, pentru pătratul al cărui colț este 45°N 24°E |
| *Acel dreptunghi nu este un dreptunghi pe Pământ* | Desenați-l din nou |
| *Acea generare nu are suprafață de desenat* | Dalele ei nu au fost recitite și găsite întregi, sau nu mai sunt de unde se servește relieful |

---

## Din linia de comandă

Totul se poate face și dintr-un singur pas documentat în linia de comandă, fără pagina Relief a
aplicației. Vedeți documentația de instalare.

---

Înrudite: [Relief](../features/terrain.md) · [Vizualizarea 3D](../features/3d-view.md)
