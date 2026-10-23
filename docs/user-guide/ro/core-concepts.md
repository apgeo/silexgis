# Noțiuni de bază

🇬🇧 [English](../core-concepts.md) · 🇷🇴 **Română**

[← Înapoi la ghid](README.md) · Anterior: [Primii pași](first-steps.md)

---

Șase idei. Tot restul aplicației este o aplicare a uneia dintre ele.

---

## 1. Tot ce este pe hartă este un *obiect*

Peșterile, intrările, drumuirile și fenomenele de suprafață nu sunt patru sisteme separate
care se întâmplă să semene. Sunt **un singur model**, cu un *fel*:

| Fel | Ce este |
|---|---|
| **Peșteră** | Peștera însăși — înregistrarea de care atârnă toate datele |
| **Intrare de peșteră** | O cale de intrare, cu poziția ei precisă |
| **Drumuire** | Linia topografică, proiectată pe hartă |
| **Obiect** | Tot restul: dolină, izvor, faliu, perete, zonă carstică… |

Pentru că împart un model, decurg trei lucruri care altfel ar cere cazuri speciale:

- **Orice poate conține orice.** O zonă carstică conține peșteri; o peșteră își conține
  intrările. Conținerea generează firul de navigare din capul paginii unui obiect.
- **Conținerea moștenește protecția în jos.** Protejezi o zonă și ce este înăuntru este
  protejat odată cu ea.
- **Orice poate fi legat de orice printr-o relație numită** — vezi ideea 4.

Obiectele poartă o **categorie** (suprafață, subteran, zonă, structură), un **tip** (dintr-un
vocabular al instalării tale), o geometrie (punct, linie, poligon sau variantele
multiple) și proprietăți proprii tipului lor.

## 2. Vizibilitatea, permisiunile și protecția locației sunt trei lucruri diferite

Oamenii le confundă constant. Nu sunt același lucru și se compun.

**Vizibilitatea** este o proprietate a înregistrării, stabilită de cine a creat-o. Patru valori:

| Valoare | Înseamnă |
|---|---|
| Privată | Doar tu (și cine a primit acces explicit) |
| Grup de speologie | Membrii grupului de care este legată |
| Utilizatori autentificați | Oricine are cont pe această instalare |
| Publică | Oricine, inclusiv vizitatori fără cont |

**Permisiunile** sunt reguli — *permite* sau *refuză*, pe acțiune, pe fel de lucru, la un
domeniu ales. Trăiesc în **grupuri de permisiuni** din care faci parte, sau direct pe un
obiect. Vezi [Permisiunile explicate](admin/permissions.md).

**Protecția locației** este un indicator separat pe o peșteră sau un obiect: *poziția exactă
este reținută*. Este independentă de celelalte două. O peșteră poate fi lizibilă pentru toți
cei autentificați și totuși să aibă poziția protejată. Vezi
[Protecția locațiilor](admin/location-protection.md).

Consecința de reținut: **a putea citi un lucru nu înseamnă a-l putea localiza**, iar o
invitație pe o tură nu acordă absolut nimic.

## 3. Nimic nu este numărat peste mai mult decât poți citi

Fiecare total, fiecare număr, fiecare lungime de listă din această aplicație este calculată
peste înregistrările pe care *tu* ai voie să le citești. Statisticile turelor,
orele în subteran ale unei persoane, numărul de peșteri vizitate de o tură, lista unei tabere,
numărul de fotografii dintr-un album.

Două consecințe:

- **Doi oameni văd pe bună dreptate totaluri diferite pentru același subiect și amândoi au
  dreptate.** Ecranul o spune acolo unde contează.
- **O listă scurtată pentru că nu poți vedea restul o spune.** Primești *„3 nearătate ție"*, nu
  o listă tăcut incompletă. Aplicația preferă să-ți spună că ceva există decât să
  te lase să crezi că ai imaginea întreagă.

## 4. Legăturile spun cum se raportează două lucruri unul la altul

Dincolo de conținere există **legături**: o relație numită între două sau mai multe lucruri de
*orice* fel — obiecte, documente, ture, speologi, cluburi, dulapuri, modele 3D, vederi
salvate.

Relația vine dintr-o listă pe care instalarea o deține și o poate extinde: *Conține*,
*Documentat de*, *Duplicat al*, *Același obiect ca*, *Adiacent cu*, plus cele specifice turelor
(*Cartat*, *Săpat la*, *Continuare lăsată*, …). O relație direcțională se citește corect din
ambele capete — o singură legătură spune „Conține" pe o pagină și „Conținut în" pe cealaltă.

O legătură poate indica o *parte*, nu întregul: o pagină sau un interval de pagini dintr-un
document, un pasaj citat, un moment sau o secvență dintr-o înregistrare, o stație topografică.
Fiecare legătură are o adresă scurtă proprie, de lipit într-un mesaj. Și fiecare legătură arată
fiecărui cititor doar capetele pe care are voie să le vadă — o legătură nu este niciodată
lucrul prin care se divulgă o peșteră protejată.

Vezi [Legături între înregistrări](features/links.md).

## 5. Înregistrările care se întâmplă au o viață, iar o ciornă nu anunță pe nimeni

Turele, evenimentele și taberele trec prin stări:

`ciornă → propusă → planificată → confirmată → efectuată → publicată`
cu `anulată` și `amânată` disponibile pe tot parcursul.

Partea importantă: **o ciornă nu notifică pe nimeni**. Poți redacta o tură în mai multe
reprize fără să plece nimic. **Publicarea este actul care o anunță** — și numai celor care au
voie efectiv s-o deschidă.

O ciornă nu este însă o tură *ascunsă*. Cine este admis de vizibilitatea turei o poate citi din
clipa în care există. Ciorna este despre *a anunța oamenii*, nu despre *a ascunde*.

## 6. Vocabularul aparține clubului tău

Aplicația vine cu liste rezonabile și apoi se dă la o parte. Tipuri de peșteri, tipuri de
intrări, tipuri de rocă, tipuri de obiecte, tipuri de documente, scopuri de tură, roluri în
tură, relații între obiecte, modele de raport, reguli de detectare a punctelor — toate sunt
liste pe care un administrator le poate extinde cu ce spune de fapt această arhivă.

Elementele care vin cu produsul nu pot fi redenumite pe sub picioarele programului și nici
șterse, așa că nimic din ce construiești nu dispare. Tot ce adaugi devine filtrabil și
numărabil din clipa în care îl adaugi.

Vezi [Vocabularele care aparțin clubului](admin/vocabularies.md).

---

## O observație despre felul în care îți vorbește aplicația

Este neobișnuit de directă, intenționat. Când ceva nu se poate face, sau o cifră nu se poate
calcula, sau un fișier nu conține text de citit, îți spune care dintre acestea este, în loc să
îți arate o rotiță sau un panou gol. Când o cifră este parțială, spune peste ce este parțială.
Când o acțiune nu se poate anula, o spune înainte s-o faci.

Citește rândurile acelea. Sunt documentația cea mai la îndemână.

---

Urmează: alege un [flux de lucru](workflows/README.md) sau răsfoiește
[referința](features/README.md).
