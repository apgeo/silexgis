# Vocabularele care aparțin clubului

🇬🇧 [English](../../admin/vocabularies.md) · 🇷🇴 **Română**

[← Administrare](README.md)

---

Aplicația vine cu liste rezonabile și apoi se dă la o parte. Pagina aceasta acoperă fiecare listă
pe care instalarea o poate extinde, unde se află și ce guvernează.

**O regulă valabilă peste tot:** elementele care vin cu produsul **nu pot fi redenumite pe sub
picioarele programului și nici șterse**, așa că nimic din ce construiți nu dispare. Tot ce
adăugați devine filtrabil și numărabil din clipa în care îl adăugați.

---

## Configurare → Tipuri de documente

**Ce guvernează:** tipul unui document — raport topografic, autorizație, raport de tură, buletin,
corespondență — și, esențial, **ce detalii li se cer documentelor de acel tip**.

Asta permite ca o arhivă să fie organizată în felul în care clasează efectiv clubul, nu în felul
pe care l-am ghicit noi.

Are nevoie de **scriere** pe vocabulare, nu doar de citire: autorarea schemei unui tip decide ce
poate spune fiecare document de acel tip.

Înrudit: [Documente și dulapuri](../features/documents-and-cabinets.md)

---

## Configurare → Scopuri de tură

**Ce guvernează:** trei lucruri deodată.

1. **Pentru ce a fost o tură** — cele opt feluri livrate sunt *Explorare · Cartare / topografie ·
   Întreținere / echipare · Instruire · Turism / vizită · Salvare · Știință · Altul*. Un club care
   face ceva la care nu s-a gândit nimeni și-l adaugă singur.
2. **Ce cere un raport de acel fel** — cele trei secțiuni de raport (date de teren, logistică,
   siguranță), pornind de la un formular pe care clubul îl descrie o dată.
3. **Ce [listă de verificare](../features/checklists-and-callout.md)** parcurg turele de acel fel.

> Schimbarea a ce cere un scop **nu invalidează niciun raport deja scris**.

Înrudit: [Ture](../features/trips.md)

---

## Configurare → Roluri în tură

**Ce guvernează:** ce a făcut cineva pe o tură. Fiecare rând din echipă își randează funcția din
această listă.

Vine cu: *Participant · Propunător · Responsabil de tură · Șofer · Topograf · Fotograf · Cursant ·
Instructor · Persoană de contact*. **Participarea și propunerea există din start și nu pot fi
scoase.**

Cine a făcut două treburi este înregistrat făcându-le pe amândouă, nu pus să aleagă.

---

## Configurare → Modele de raport

**Ce guvernează:** modelul în care se redactează o tură.

Descărcați-l pe cel standard — un fișier text scurt care se explică singur în propriile comentarii
— editați-l, încărcați-l și alegeți-l.

Două proprietăți care fac asta sigur:

- **Un model poate cere doar lucruri care i-au fost deja date cititorului.** Nu poate fi folosit
  pentru a lărgi divulgarea.
- **Un rând al cărui conținut se dovedește gol pur și simplu dispare**, așa că același model
  produce un document onest și pentru un membru, și pentru un editor.

Înrudit: [Ture](../features/trips.md#redactarea)

---

## Configurare → Relații între elemente

**Ce guvernează:** formularea fiecărei [legături](../features/links.md).

Vine cu *Același obiect ca · Înrudit cu · Conține/Conținut în · Documentat de/Documentează ·
Sursă pentru/Derivat din · Adiacent cu · Original al/Duplicat al · Necesită clarificare*,
relațiile de tură și *Text al/Are text*.

Adăugați ce spune de fapt această arhivă. O relație direcțională poartă ambele citiri, deci o
legătură spune un lucru pe o pagină și inversul lui pe cealaltă.

Restricții pe care le impune aplicația:

- O relație care **vine cu produsul** își păstrează codul și felul în care se citește și nu poate
  fi ștearsă.
- O relație pe care **legăturile o înregistrează deja** nu poate fi schimbată astfel sau ștearsă.
- Codurile sunt unice.

Are nevoie de Administrator complet.

---

## Configurare → Reguli de detectare

**Ce guvernează:** ce se propune să devină un punct numit *Peșteră*, *P.*, *aven*, *izbuc*,
*ponor* sau *doline* când [importați un fișier GPS](../workflows/import-a-gps-file.md).

**Nu este o pagină de administrator.** Oricine are ceva de importat își ține propriile seturi.
Doar *promovarea* unui set la ce moștenește un grup de speologie sau întreaga instalare este un
act de administrator, iar asta este refuzată pe server oricui altcuiva.

### Un set de reguli

Are un **nume** și un **cuprins**: *Întreaga instalare · Un grup de speologie · Dumneavoastră*.
Seturile pot fi **salvate ca fișier** și **încărcate dintr-un fișier** — așa dați convențiile
dumneavoastră altui club. Un set care nu este al dumneavoastră poate fi editat ca *o copie
proprie*.

### O regulă

| Câmp | |
|---|---|
| **Numele regulii**, **Activă** | |
| **Compară după** | Conține · Cuvânt întreg · Începe cu · Expresie regulată |
| **Termeni** | Tastați unul câte unul |
| **Limbă** | Termeni în română · Termeni în engleză · Termeni în nicio limbă (care participă întotdeauna) |
| **Propune** | O peșteră · O intrare în peșteră · Un fenomen de suprafață, și de ce **tip** |
| **Citește numele** / **Citește descrierea** | Unde să caute |
| **Scoate termenul din nume** | Păstrează-l · Doar din față · Doar de la sfârșit · Oriunde este |

> **Ordinea decide.** Când două reguli pretind același candidat, câștigă cea mai de sus — iar
> ecranul de verificare vă spune ce alte reguli l-au mai pretins.

---

## Configurare → Seturi de elemente

Seturi numite de elemente către care pot fi îndreptate reguli de acces. Tratate la
[Permisiuni](permissions.md#seturi-de-elemente).

---

## Configurare → Texte mesaje

Formularea fiecărui mesaj pe care îl trimite aplicația, **editabilă per limbă**.

Formularea pe care a rescris-o un operator este cea pe care o văd membrii — inclusiv în cutia lor
de notificări, care randează rândurile din aceste texte în limba în care citește fiecare persoană
site-ul.

Înrudit: [Mesagerie](messaging.md) · [Notificări](../features/notifications.md)

---

## Alte vocabulare

Accesibile din înregistrările care le folosesc, nu dintr-o pagină proprie:

- **Tipuri de peșteri**, **tipuri de intrări**, **tipuri de rocă** — pe formularele de peșteră și
  de intrare.
- **Tipuri de elemente**, cu simbolurile și proprietățile lor tipizate — pe formularele de element.
- **Feluri de hărți georeferențiate** — geologică, topografică, turistică, hartă de peșteră, altele.
- **Roluri în tabără** — membru, organizator, bucătar, tabără de bază, șofer, medic, echipament,
  invitat.
- **Feluri de evenimente** — ședință de club, instruire, zi de lucru, verificare de echipament,
  conferință, termen limită.
- **Etichete** — libere, create pe măsură ce le folosiți, filtrabile peste tot.

---

## O observație despre schimbarea ulterioară a unui vocabular

Adăugarea este întotdeauna sigură. Redenumirea este sigură pentru orice ați adăugat dumneavoastră.
Ce nu se oferă — pentru că ar rescrie tacit evidența — este ștergerea sau redefinirea a ceva ce
înregistrările folosesc deja. Aplicația refuză și vă spune ce încă arată către acel lucru.

Merită știut înainte să proiectați o schemă: **este mult mai ușor să adăugați un tip de document
decât să contopiți două după aceea.**

---

Înrudite: [Permisiuni](permissions.md) · [Ture](../features/trips.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md)
