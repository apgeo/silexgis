# Vocabularele care aparțin clubului

🇬🇧 [English](../../admin/vocabularies.md) · 🇷🇴 **Română**

[← Administrare](README.md)

---

Aplicația vine cu liste rezonabile și apoi se dă la o parte. Pagina aceasta acoperă fiecare listă
pe care instalarea o poate extinde, unde se află și ce guvernează.

**O regulă valabilă peste tot:** elementele care vin cu produsul **nu pot fi redenumite pe sub
picioarele programului și nici șterse**, așa că nimic din ce construiești nu dispare. Tot ce
adaugi devine filtrabil și numărabil din clipa în care îl adaugi.

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

## Configurare → Tipuri de obiecte

**Ce guvernează:** tipurile de [obiecte](../features/surface-features.md) pe care instalarea le
înregistrează pe hartă — dolină, izvor, puț, bivuac, zonă carstică și celelalte — iar pentru
fiecare:

| | |
|---|---|
| **Denumire**, **Descriere**, **Ordine** | Cum este listat tipul și în ce ordine |
| **Cod** | Litere mici, cifre și liniuțe de subliniere. *„Instalările schimbă obiecte după acest cod, deci păstrează-l stabil."* |
| **Categorie** | Suprafață · Subteran · Zonă · Structură |
| **Forme în care poate fi desenat** | Punct · Linie · Contur · Mai multe puncte · Mai multe linii · Mai multe contururi |
| **Când este protejat** | *Arătat aliniat la grilă (doar punctele)* sau *Nu este arătat deloc* — cum apare un obiect de acest tip cuiva fără vedere exactă. Vezi [Protecția locațiilor](location-protection.md) |
| **Doar în interiorul altui obiect** | Pentru tipuri care n-au sens singure, cum este un loc din interiorul unei peșteri |
| **Simbol pe hartă** | Ales din simbolurile pe care le are aplicația, sau niciunul |
| **Ajustări de stil pe hartă** | Opțional; gol păstrează stilul implicit |
| **Schema detaliilor** | Ce i se cere unui obiect de acest tip. În formular apar doar câmpuri simple de text, număr, da/nu și alegere, iar fereastra arată dinainte câmpurile pe care le va oferi formularul |

**Adaugă un tip** pentru ceva ce înregistrează clubul și nu descrie niciun tip livrat. Tipurile
care vin cu produsul sunt marcate **Livrat cu produsul**: codul, categoria, afișarea protejată și
nevoia de părinte rămân cum au fost livrate, își păstrează toate formele cu care vin și nu pot fi
șterse — dar denumirea, simbolul, ordinea și detaliile lor sunt ale tale.

**Cave** (peșteră) este singurul tip marcat **Toate peșterile**: îl are fiecare peșteră, oricum ar fi
fost creată — din formularul peșterii, dintr-un import din catalog sau din fișier, de pe telefon,
dintr-o fotografie — și nimic altceva nu-l poate primi, de aceea nu este oferit când desenezi sau
imporți alte obiecte. Aici pui **câmpurile proprii ale clubului** pentru peșteri („necesită
echipare", „colonie de lilieci"): adaugă-le în schema detaliilor și apar în formularul peșterii
la **Câmpurile clubului**, pe pagina peșterii și în filtru ca *Cave → câmp*, lângă *Tipul este
Cave*. Câmpurile cu care vine sunt codurile scrise de importul din catalog, de telefon sau de
o bibliotecă foto; își păstrează numele și tipurile, schema rămâne deschisă câmpurilor pe care nu
le enumeră și niciun câmp nu poate deveni obligatoriu — altfel peșterile importate ar fi refuzate
la prima editare. Felul în care e arătată o peșteră protejată nu depinde deloc de acest tip:
pentru cine nu are vederea exactă, peșterile sunt arătate mereu aproximativ, aliniate la grilă.

Există un singur **aven**: tipul de peșteră *Pit / Aven*. Tipul de suprafață *Pit* nu mai vine
cu produsul; o instalare care avea deja obiecte de acest tip îl păstrează ca tip propriu, pe care
îl poți retrage dând acelor obiecte alt tip și ștergându-l. **Hill** (deal) este un contur, ca
*Massif / Mountain*, și e tratat la fel peste tot unde e tratat un masiv.

Odată ce există obiecte de un tip (inclusiv șterse), tipul poate fi doar **lărgit**: categoria
rămâne, nicio formă nu mai poate fi scoasă și nu poate începe să ceară un părinte, pentru că
obiectele salvate n-ar mai fi în acord cu el. Un tip poate fi șters doar cât timp nu-l folosește
niciun obiect.

Are nevoie de **scriere** pe vocabulare, ca tipurile de documente: un tip de obiect decide în ce
forme poate fi desenat fiecare obiect al lui și cum este arătat unul protejat.

Înrudit: [Obiecte](../features/surface-features.md)

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

## Configurare → Roluri în tabără

**Ce guvernează:** ce a fost cineva într-o [tabără](../features/camps.md). Fiecare rând din
lista *Cine a fost* a unei tabere își randează rolul din această listă.

Vine cu: *Membru · Organizator · Bucătar · Tabără de bază · Șofer · Medic · Echipament ·
Invitat*. **Membru există din start și nu poate fi scos** — fără el o tabără nu ar putea
înregistra că cineva pur și simplu a fost acolo.

O listă proprie, ținută dinadins separat de rolurile în tură: a găti pentru treizeci de oameni
sau a ține tabăra de bază nu este o sarcină în subteran, așa că un rol adăugat aici nu este
oferit niciodată într-o tură, iar un rol de tură nu este oferit niciodată într-o tabără. Se
ajunge la ea și din lista taberelor, sub *Configurare*.

---

## Configurare → Modele de raport

**Ce guvernează:** modelul în care se redactează o tură sau o tabără. Sunt două feluri de model,
cu cuvinte diferite, iar fiecare fel are modelul său standard și modelul său ales.

Descarcă-l pe cel standard pentru felul dorit — un fișier text scurt care se explică singur în
propriile comentarii — editează-l, încarcă-l și alege-l. Un model de tabără are un rând al său,
`accounts`, care tipărește ce a scris fiecare tură din tabără despre ea însăși; scoate rândul
pentru un document mai scurt.

Un model poate fi și **al unui singur scop de tură** — buletinul de cartare, de pildă: o tură
înregistrată sub acel scop este redactată în el dacă nu alege cineva altul, înaintea modelului
ales pentru tot restul. Un model pe scop; lasă scopul gol pentru un model pe care îl poate
folosi orice tură.

Două proprietăți care fac asta sigur:

- **Un model poate cere doar lucruri care i-au fost deja date cititorului.** Nu poate fi folosit
  pentru a lărgi divulgarea.
- **Un rând al cărui conținut se dovedește gol pur și simplu dispare**, așa că același model
  produce un document onest și pentru un membru, și pentru un editor.

Înrudit: [Ture](../features/trips.md#redactarea) · [Tabere](../features/camps.md#raportul)

---

## Configurare → Relații între obiecte

**Ce guvernează:** formularea fiecărei [legături](../features/links.md).

Vine cu *Același obiect ca · Înrudit cu · Conține/Conținut în · Documentat de/Documentează ·
Sursă pentru/Derivat din · Adiacent cu · Original al/Duplicat al · Necesită clarificare*,
relațiile de tură și *Text al/Are text*.

Adaugă ce spune de fapt această arhivă. O relație direcțională poartă ambele citiri, deci o
legătură spune un lucru pe o pagină și inversul lui pe cealaltă.

Restricții pe care le impune aplicația:

- O relație care **vine cu produsul** își păstrează codul și felul în care se citește și nu poate
  fi ștearsă.
- O relație pe care **legăturile o înregistrează deja** nu poate fi schimbată astfel sau ștearsă.
- Codurile sunt unice.

Are nevoie de Administrator deplin.

---

## Configurare → Reguli de detectare

**Ce guvernează:** ce se propune să devină un punct numit *Peșteră*, *P.*, *aven*, *izbuc*,
*ponor* sau *doline* când [imporți un fișier GPS](../workflows/import-a-gps-file.md).

**Nu este o pagină de administrator.** Oricine are ceva de importat își ține propriile seturi.
Doar *promovarea* unui set la ce moștenește un grup de speologie sau întreaga instalare este un
act de administrator, iar asta este refuzată pe server oricui altcuiva.

### Un set de reguli

Are un **nume** și un **cuprins**: *Întreaga instalare · Un club · Tu*.
Seturile pot fi **salvate ca fișier** și **încărcate dintr-un fișier** — așa dai convențiile
tale altui club. Un set care nu este al tău poate fi editat ca *o copie
proprie*.

### O regulă

| Câmp | |
|---|---|
| **Numele regulii**, **Activă** | |
| **Compară prin** | Conține · Cuvânt întreg · Începe cu · Expresie regulată |
| **Termeni** | Tastează-i unul câte unul |
| **Limbă** | Termeni în română · Termeni în engleză · Termeni fără limbă (care participă întotdeauna) |
| **Propune** | O peșteră · O intrare în peșteră · Un fenomen de suprafață, și de ce **tip** |
| **Citește numele** / **Citește descrierea** | Unde să caute |
| **Scoate termenul din nume** | Păstrează-l · Doar de la început · Doar de la sfârșit · Oriunde s-ar afla |

> **Ordinea decide.** Când două reguli revendică același candidat, câștigă cea de mai sus — iar
> ecranul de verificare îți spune ce alte reguli l-au mai pretins.

---

## Configurare → Seturi de obiecte

Seturi denumite de obiecte către care pot fi îndreptate reguli de acces. Tratate la
[Permisiuni](permissions.md#seturi-de-obiecte).

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
- **Feluri de hărți georeferențiate** — geologică, topografică, turistică, hartă de peșteră, altele.
- **Feluri de evenimente** — ședință de club, instruire, zi de lucru, verificare de echipament,
  conferință, termen limită.
- **Etichete** — libere, create pe măsură ce le folosești, filtrabile peste tot.

---

## O observație despre schimbarea ulterioară a unui vocabular

Adăugarea este întotdeauna sigură. Redenumirea este sigură pentru orice ai adăugat tu.
Ce nu se oferă — pentru că ar rescrie tacit evidența — este ștergerea sau redefinirea a ceva ce
înregistrările folosesc deja. Aplicația refuză și îți spune ce încă arată către acel lucru.

Merită știut înainte să proiectezi o schemă: **este mult mai ușor să adaugi un tip de document
decât să contopești două după aceea.**

---

Înrudite: [Permisiuni](permissions.md) · [Ture](../features/trips.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md)
