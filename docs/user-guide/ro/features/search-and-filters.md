# Căutare și filtre

🇬🇧 [English](../../features/search-and-filters.md) · 🇷🇴 **Română**

[← Referință](README.md)

---

## Caseta de căutare

În bara de sus și pe hartă. O casetă, mai multe feluri de răspuns, grupate:

| Grup | |
|---|---|
| **Peșteri** | |
| **Elemente** | |
| **Jurnale de tură** | |
| **Tabere** | |
| **Locuri** | Nume de locuri de la un serviciu extern de geocodare (Nominatim) |
| **În textul documentelor** | Ce scrie *în interiorul* documentelor dumneavoastră |

Fiecare rezultat oferă **Apropie la** sau **Deschide**, după caz.

### Căutarea în interiorul documentelor

Partea distinctivă. Un rezultat de document **citează propoziția care s-a potrivit** și deschide
pagina documentului **la pagina pe care s-a găsit expresia** (sau foaia, sau diapozitivul).

- **Insensibilă la diacritice în ambele sensuri**: `pestera` găsește `peșteră`, iar rezultatul
  este citat înapoi scris așa cum l-a scris autorul.
- Cuvintele sunt **lematizate în limba în care este scris documentul** — română și engleză din
  start.
- *„Se afișează n din m documente potrivite"* când sunt mai multe.
- Dacă nu s-a potrivit nimic: *„Niciun text de document nu s-a potrivit. Paginile scanate nu
  conțin text de căutat până nu sunt tastate."* — care de obicei este răspunsul real.
- O **versiune înlocuită** este marcată *„versiunea n, înlocuită"* și este găsibilă doar de cei
  care ar fi putut-o înlocui.

**Găsiți doar ce aveți voie să citiți.**

---

## Filtre

Un filtru este o **întrebare** despre înregistrările dumneavoastră: la ce feluri de obiecte se
referă, ce condiții trebuie să îndeplinească și cum este sortat răspunsul. Îl construiți pe un
ecran de listă — astăzi [registrul peșterilor](#filtrul-registrului-peșterilor) și
[galeria](photographs.md#condiții-și-vizualizări-salvate-ale-galeriei) — și îl puteți păstra ca
[filtru salvat](#filtre-salvate).

**Unui filtru i se răspunde întotdeauna cu ce aveți dumneavoastră dreptul să vedeți.** El
restrânge ce aveți voie să citiți; nu îl lărgește niciodată.

### Feluri de obiecte

Un filtru acoperă unul sau mai multe **feluri de obiecte**: **Obiective · Jurnale de tură ·
Vizualizări de hartă · Documente · Hărți georeferențiate · Dulapuri · Modele topografice ·
Fotografii · Fișiere de geodate**. Fiecare apare o singură dată, și există o limită câte acoperă
un filtru.

### Câmpuri după care puteți pune condiții

Câmpurile oferite depind de felul obiectului. Printre ele:

- pentru toate: **Nume · Titlu · Etichetă · Proprietar · Club · Vizibilitate · Creat · Ultima
  modificare**;
- pentru elemente (**Obiective**) și peșteri: **Fel · Categorie · Tip · Locație protejată ·
  Regiune · Tip de rocă · Lungime cartată (m) · Denivelare (m) · Tip de peșteră · Alte
  denumiri**, plus
  [proprietățile tipizate](#condiții-pe-proprietăți) pe care le declară un tip de element;
- pentru ture: **Tip de tură · Data turei · A avut incident · Stare · Club organizator**;
- pentru fotografii: **Descriere · Nume de loc · Fotografiat la · Datată (fotografiată, altfel
  încărcată)**;
- pentru modele topografice, hărți și fișiere: **Format topografie · Topografiat la · Model
  curent · Tip de hartă · Format fișier · Limbă**.

Un câmp pe care nu aveți dreptul să-l folosiți nu este oferit. Dacă un filtru îl numește totuși —
unul partajat cu dumneavoastră sau scris de mână — este refuzat exact ca și cum câmpul nu ar
exista: refuzul nu vă spune niciodată ce se află acolo.

### Sortare

**Adăugat · Modificat · Nume · Proprietar · Dată · Cel mai apropiat**

Dacă nimic din ce se caută nu poate fi sortat într-un anumit fel, vă spune, în loc să sorteze
arbitrar.

### Limite, declarate

Un filtru este mărginit, iar fiecare margine are mesajul ei: prea multe părți (*„Încercați să-l
împărțiți"*), imbricare prea adâncă, prea multe valori într-o condiție, text prea lung într-o
condiție, cel puțin un fel de obiect obligatoriu.

### Selectorul

Acolo unde alegeți valori, selectorul spune ce face:

- *„Tastați cel puțin n caractere"*,
- *„Nimic nu se potrivește"* / *„Nimic ales"* / *„n alese"*,
- și, important, **„n neafișate — dispărute sau nu ale dumneavoastră de văzut"**.

Unele valori sunt **localizabile** — *„Vi se poate arăta unde este"* — și oferă un *Mergi la…*
care mută harta acolo.

Același selector este folosit oriunde alegeți un element, o peșteră sau o tură dintr-o căutare:
părintele unui element, *Adaugă părinte*, ținta unei legături, membrii unui set de elemente,
elementul unei reguli de permisiune, cealaltă peșteră din *Cea mai mică distanță*, peștera unei
intrări noi, a unei fotografii din bibliotecă sau a unei hărți georeferențiate, tura și peștera
unui import de înregistrare și peșterile pe care le poartă un telefon. Peșterile se găsesc după
orice parte a numelui, fără a ține seama de diacritice. Doar lista de ture a importului de
înregistrare arată cele mai noi ture înainte să tastați.

De la tastatură: **Jos** din caseta de căutare ajunge la primul rezultat, **Sus / Jos** trec de
la un rezultat la altul, **Enter** sau **Spațiu** alege. Acolo unde o alegere poate fi retrasă,
*Renunță la alegere* apare după ce ați ales ceva.

---

## Filtre pe hartă

Harta citește aceleași filtre. Un filtru salvat, sau un filtru construit în registrul de
peșteri, restrânge intrările de peșteri, elementele de suprafață, liniile de topografie și — în
vederea 3D — pereții peșterilor. Un filtru despre peșteri păstrează intrările acelor peșteri; un
filtru despre intrări păstrează acele intrări. Nu face decât să scoată din ce puteți vedea deja.

- **În adresă.** Adresa hărții poartă filtrul ei la fel ca adresa registrului de peșteri, așa că
  o hartă filtrată este un link pe care îl puteți trimite. Un link al cărui filtru nu poate fi
  citit — sau pe care cel care îl deschide nu îl poate rula, de pildă filtrul dumneavoastră salvat
  privat — deschide harta nefiltrată și spune *„Filtrul din acest link nu a putut fi citit…"*.
- **O notificare îl numește**: *„Hartă filtrată după …"*, cu un buton care îl golește. Un filtru
  care nu mai merge cât priviți (șters, nemaipartajat) golește straturile pe care le restrângea
  și spune asta.
- **Selectorul de etichete din panoul de straturi** este un filtru cu o singură condiție: păstrează
  intrările care poartă eticheta și intrările peșterilor care o poartă.
- **Vederile salvate** țin minte filtrul și îl readuc când sunt aplicate. Un **link partajat**
  către o vedere restrânsă de mai mult decât o etichetă arată zona și fundalul, dar nicio peșteră,
  pentru că în spatele unui link nu este nimeni ca care să ruleze filtrul; dialogul de partajare
  spune asta.
- **Un loc nu intră în vederile salvate.** Un filtru care conține un loc anume — un punct
  introdus, un contur lipit sau locul de la care se măsoară *Cel mai apropiat* — funcționează în
  adresa hărții, dar nu se salvează niciodată într-o vedere, pentru că o vedere este arătată
  tuturor celor cu care este partajată. Salvarea uneia spune asta; măsurați în schimb de la un
  obiect sau de la o zonă numită, ori aplicați un filtru salvat.

### Straturi din filtre salvate

**Adaugă un strat dintr-un filtru salvat…** în panoul de straturi desenează peste hartă încă o
întrebare: intrările și elementele pe care le păstrează filtrul salvat, într-o culoare și un
simbol proprii, schimbate din rândul stratului. Cel mult șase deodată. Se aprind, se reordonează
și se fac transparente ca orice alt strat și se păstrează în vederile salvate. Un strat al cărui
filtru a dispărut sau nu mai poate fi rulat rămâne în listă marcat **Ilizibil** și nu desenează
nimic. Marcajele acestor straturi sunt doar de privit; selecția rămâne la straturile principale.
Linkurile partajate nu le arată.

### Filtre rapide

Fixați până la șase filtre salvate — ale dumneavoastră sau partajate cu dumneavoastră — din
**Filtre salvate** (*„Fixați ca filtru rapid"*). Apar ca butoane de un clic pe hartă și în bara
registrului de peșteri, în orice browser în care vă autentificați. Un buton aplică filtrul;
apăsat din nou, îl ridică. Un buton al cărui filtru nu mai există sau nu poate fi rulat apare
estompat și spune asta, până îl scoateți, din buton sau din **Filtre salvate**.

---

## Filtrul registrului peșterilor

**Peșteri** deschide registrul peșterilor. Deasupra listei stau câmpurile lui rapide:
**Caută denumire sau toponim…**, **Toate tipurile**, **Regiune**, **Lungime de cel puțin (m)**
și **Filtrează după etichetă…**. Ele formează un singur filtru, iar adresa paginii îl poartă,
așa că un link copiat deschide aceeași întrebare pentru oricine îl urmează (cu răspunsul pe
care *acela* are dreptul să-l vadă).

**Filtru avansat…** deschide [editorul de condiții](#editorul-de-condiții) pe același filtru,
pentru tot ce câmpurile nu pot spune — un SAU, un NU, un interval. Când îl
aplicați, părțile pe care câmpurile le pot arăta se întorc în câmpuri; restul este păstrat
alături: *„Acest filtru mai are condiții pe care aceste câmpuri nu le arată (n). Ele restrâng
lista în continuare."* — cu **Editați-le** și **Eliminați-le**.

Lângă câmpuri:

- **Salvați ca filtru…** păstrează întrebarea curentă ca [filtru salvat](#filtre-salvate);
- **Aplicați un filtru salvat…** alege unul dintre ale dumneavoastră sau unul partajat cu
  grupul dumneavoastră de speologie;
- **Filtre salvate** deschide lista lor.

Cât timp este aplicat un filtru salvat, pagina spune *„Filtru salvat: nume"*, iar *„modificat
între timp"* după ce schimbați câmpurile. **Nu mai numiți acest filtru salvat** păstrează
întrebarea și uită numele.

Dacă filtrul dintr-un link nu poate fi citit, pagina o spune și arată toate peșterile în loc să
ghicească; cereți celui care l-a trimis un link nou.

---

## Editorul de condiții

Același editor servește registrul peșterilor (**Filtru avansat**) și galeria (**Condiții pentru
fotografii**). Fiecare rând este o condiție: un **Câmp**, o **Comparație** și o **Valoare**.

- **Adaugă condiție** adaugă un rând; **Adaugă grup** adaugă un grup în interiorul celui curent.
- Fiecare grup spune cum se combină rândurile lui: **Toate acestea (ȘI)** sau **Oricare dintre
  acestea (SAU)**.
- **Nu** transformă un rând sau un grup în opusul lui.
- Grupurile se imbrică până la o adâncime fixă; dincolo de ea editorul spune *„Grupurile pot fi
  imbricate pe cel mult n niveluri."*
- **Rezultate: n** se numără pe loc, înainte să aplicați, cu ce aveți dreptul să vedeți.
- **Aplică** pune filtrul editat pe listă.

### Comparații

Comparațiile oferite de un câmp depind de ce conține:

| Comparație | Pentru |
|---|---|
| **este** · **este unul dintre** | o alegere dintr-o listă: un tip, o etichetă, o persoană, un grup de speologie |
| **conține** · **începe cu** | text |
| **mai mic decât** · **cel mult** · **mai mare decât** · **cel puțin** · **între** | numere și date |
| **este gol** · **nu este gol** | orice poate rămâne necompletat |

**cel puțin** și **cel mult** includ valoarea însăși: *Lungime cartată cel puțin 500* găsește o
peșteră de exact 500 m; *mai mare decât 500* nu o găsește. **între** include ambele capete.

**Datele sunt zile.** Când alegeți o zi, este ziua din propriul dumneavoastră calendar: *Data
turei cel puțin 5 octombrie* găsește turele din 5 octombrie și de după, oriunde v-ați afla când
întrebați. Un filtru salvat păstrează ziua aleasă de autorul lui și o arată ca acea zi.

### Condiții pe proprietăți

Elementele de pe hartă au un tip, iar unele tipuri își declară proprietăți tipizate proprii — un
număr, un text, o dată, un da/nu. Acolo unde se filtrează elemente, editorul o oferă pe fiecare
ca **Proprietate: nume**, cu comparațiile pe care felul ei le permite.

O proprietate aparține tipului ei, așa că se cere **doar împreună cu tipul său de obiect, în
același grup ȘI** — editorul o spune sub rând. O proprietate a unui tip pe care nu aveți dreptul
să-l citiți nu este oferită și este refuzată ca un câmp care nu există. Peșterile însele nu au
un tip de element, așa că o condiție pe tip sau pe proprietate nu găsește niciodată o peșteră.

### Când un filtru este refuzat

Dacă serverul nu poate rula un filtru, spune de ce, **pe rândul la care se referă** — de exemplu
*„Acest câmp nu admite această comparație."*, *„Intervalul se termină înainte să înceapă."* sau
*„O valoare are mai mult de 200 caractere."* Problemele care nu țin de un singur rând — prea
multe condiții, grupuri imbricate prea adânc — sunt arătate pentru filtru în ansamblu.
**Aplică** rămâne indisponibil până când filtrul poate fi rulat.

---

## Filtre salvate

Un filtru salvat este **o întrebare pe care o păstrați**. **Filtre salvate**, din meniul
contului, le arată pe cele pe care le puteți folosi: ale dumneavoastră și cele partajate cu un
grup de speologie din care faceți parte.

| Coloană | Arată |
|---|---|
| **Nume** | numele și descrierea lui |
| **Caută** | condițiile, în cuvinte |
| **Cine îl poate folosi** | **Doar eu** sau **Membrii** unui grup de speologie |
| **Salvat de** | **Dumneavoastră**, autorul așa cum vi-l arată profilul lui, sau **Un membru al clubului** |
| **Stare** | **Pregătit**, **Învechit** (un tip sau o etichetă numită de el a fost ștearsă între timp, așa că acea condiție nu mai găsește nimic) sau **Nu poate fi citit** |

**Deschideți în registrul peșterilor** aplică acolo un filtru despre peșteri. O vizualizare
salvată a galeriei apare și ea aici și se aplică din galerie, cu **Aplică o vizualizare salvată
a galeriei…**.

### Partajarea nu acordă nimic

Partajarea unui filtru cu un grup de speologie le dă membrilor lui **doar întrebarea**. Fiecăruia i se
răspunde cu ce are el însuși dreptul să vadă; un filtru nu împrumută nimănui drepturile
autorului său. Din același motiv, un filtru salvat **nu păstrează niciodată un loc de la care se
măsoară** — *cel mai apropiat de aici* se întreabă din nou de fiecare dată.

### Cine îl poate modifica

Doar **autorul lui** sau un **administrator complet** poate redenumi, descrie, partaja sau șterge
un filtru salvat. Lista oferă **Redenumiți, descrieți sau partajați** și **Ștergeți** exact pe
filtrele pe care le puteți modifica. Ștergerea unui filtru partajat îl ia tuturor celor cu care a
fost partajat.

Un filtru salvat care întreabă ceva ce nu puteți întreba — un câmp pe care nu îl puteți folosi
sau o formă pe care această versiune nu o poate citi — apare ca **Nu poate fi citit** și nu poate
fi aplicat.

---

## Domenii

Acolo unde apare un selector de domeniu, îngustează la: **Peșteri · Intrări · Toate elementele ·
Documente · Ture · Vederi**.

---

## Filtrarea pe paginile de listă

Fiecare pagină de listă are și filtrele ei rapide — căutare după nume, filtrare după tip, fel sau
categorie, filtrare după etichetă. Lista de peșteri, cea de elemente, de ture, de evenimente, de
tabere și listele de documente funcționează toate așa, cu paginare și sortare pe server.

---

Înrudite:
[Documente și dulapuri](documents-and-cabinets.md#căutarea-în-interiorul-documentelor) ·
[Spațiul de lucru al hărții](map-workspace.md#căutarea)
