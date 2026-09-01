# Catalogul național al peșterilor

🇬🇧 [English](../../features/cave-catalogue.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Peșteri și intrări](caves-and-entrances.md) ·
[Geodate și importuri](geodata.md)

---

**Catalogul peșterilor** caută pe [speologie.org](https://www.speologie.org) — registrul comunitar
al peșterilor publicate din România, aproximativ 8.650 la număr — și vă lasă să aduceți peșteri de
acolo în propria instalare, fără să le retastați.

Sunt două ecrane. Primul doar caută; nu creează nimic, iar majoritatea vizitelor se opresc acolo,
pentru că de obicei voiați să citiți ce spune registrul și să urmați linkul către el. Al doilea
importă peșterile alese.

## Înainte să funcționeze: o cheie

Catalogul este serviciul altcuiva și se accesează cu o **cheie API personală**. O obțineți din
*Editează profilul* pe un cont speologie.org și cereți unui administrator s-o configureze pe
această instalare.

Până atunci, ecranul spune asta și nu face nimic altceva. Nu este o defecțiune: majoritatea
instalărilor acestei aplicații nu au nicio legătură cu registrul românesc, iar una fără cheie
funcționează exact ca înainte.

**Căutarea are nevoie de dreptul de a crea peșteri**, deși căutarea nu creează nimic. Cererile pleacă
cu cheia proprie a instalării către un serviciu mic, întreținut de voluntari, așa că pagina este
oferită celor care au un motiv să caute peșteri de adăugat.

---

## Căutarea

Dați o denumire, un județ, sau ambele. O căutare care nu numește niciunul este refuzată — registrul
nu este ceva de răsfoit de la început.

**Județul se alege dintr-o listă, nu se tastează.** Catalogul potrivește județele după codul din
două litere (`BH`, `GJ`, `HD`), exact și cu majuscule. Tastarea lui *Bihor* nu găsește absolut nimic
— de aceea nu există o casetă în care să-l scrieți.

### De ce ecranul spune uneori că a căutat trei grafii

Româna se scrie corect cu `ș` și `ț` (cu virgulă dedesubt). Vreme de aproape două decenii, fonturile
și tastaturile din circulație au produs în schimb literele turcești cu sedilă, `ş` și `ţ`, iar foarte
mult text românesc a fost scris așa. O bună parte din acest catalog a fost tastată fără diacritice
deloc.

**Căutarea catalogului le tratează pe toate trei ca pe cuvinte diferite.** Căutarea `urșilor` și
căutarea `urşilor` întorc peșteri complet diferite, iar niciuna nu găsește *Pestera Ursilor*. Așa că
o căutare de aici cere fiecare grafie a ceea ce ați scris, unește răspunsurile și vă spune ce grafii
a folosit. Nu trebuie să știți nimic din toate acestea ca să folosiți caseta — dar dacă ați căutat
vreodată direct pe acel site și v-ați mirat, aceasta este explicația.

O consecință de reținut: **paginarea dincolo de prima pagină este aproximativă.** Fiecare grafie este
paginată separat la celălalt capăt, așa că o pagină ulterioară poate fi scurtă sau poate repeta ceva.
Mai bine restrângeți căutarea decât să paginați în adâncime.

### Ce vă spune fiecare rând

Lungimea, denivelarea, altitudinea și clasa de protecție așa cum le ține registrul, plus două lucruri
pe care el nu le știe:

- **Dacă peștera este deja în instalarea dumneavoastră.** Dacă aveți dreptul s-o vedeți, rândul face
  legătura către ea. Dacă nu, rândul tot spune că intrarea este deja aici, fără să numească peștera —
  altfel ați fi invitat să importați o a doua copie a ceva ce un coleg are deja.
- Etichete pentru o peșteră consemnată ca **dispărută** sau ca având **sifon**.

Clic pe denumire deschide tot ce ține registrul, inclusiv descrierea. **Deschide pe speologie.org**
duce la pagina peșterii de acolo.

---

## Importul

**Importă** pe un rând, sau bifați mai multe rânduri și folosiți **Importă selecția**. În ambele
cazuri ajungeți pe ecranul de import cu acele peșteri listate, și încă nu s-a creat nimic.

### Primul lucru de înțeles: nu există coordonate

**Catalogul nu publică coordonate. Nici aproximative, nici ascunse — câmpul nu există.**

Așadar o peșteră importată de acolo ajunge **fără poziție**. Este o peșteră reală în registrul
dumneavoastră, apare în lista de peșteri și în căutare, are lungimea, denivelarea și descrierea ei —
și nu este pe nicio hartă, pentru că poziția unei peșteri de aici este poziția intrării ei
principale, iar ea nu are intrare.

Aveți două opțiuni oneste, iar ecranul le oferă pe amândouă:

1. **Lăsați-o nepoziționată.** Perfect rezonabil. Le găsiți mai târziu la **Peșteri**, cu filtrul
   *fără poziție*, și le așezați pe măsură ce aflați unde sunt.
2. **Poziționați-o acum.** Apăsați **Așază pe hartă** pe un rând și dați clic pe hartă. Se creează o
   intrare principală în punctul indicat, consemnată drept *citită de pe hartă*, nu ca măsurătoare
   GPS — pentru că asta și este.

Nimic nu inventează o poziție în locul dumneavoastră. Centrul unui județ nu este o peșteră, iar o
coordonată care pare măsurată și nu este e mai rea decât un câmp gol.

### Alegeri care se aplică la tot ce importați

**Vizibilitatea** (privată dacă nu spuneți altfel), un **grup de speologie** de care să fie legate, și
dacă sunt **cu locație protejată**. Sunt aceleași alegeri pe care le oferă orice import de aici, iar
valorile implicite sunt cele mai restrictive.

### Pentru fiecare peșteră

- **Ce se face** — se creează, se actualizează, sau se lasă neatinsă. O peșteră importată anterior
  este propusă spre actualizare, nu ca duplicat.
- **Tipul** — dedus din denumire (*Peștera …* este peșteră, *Avenul …* este aven), cu aceleași reguli
  de denumire folosite de importul din fișier. Îl puteți schimba dacă denumirea induce în eroare.
- **Poziția** — opțională, ca mai sus.

### Ce rescrie actualizarea

Actualizarea unei peșteri venite din catalog rescrie **câmpurile care aparțin catalogului**:
denumirea, descrierea, regiunea, altitudinea, lungimea, denivelarea și clasa de protecție. Tot
restul a ceea ce ați consemnat pe acea peșteră — intrări, topografii, fotografii, documente,
etichete, legături, notele dumneavoastră — rămâne neatins. Dacă ați modificat manual unul dintre
câmpurile catalogului, o actualizare vă va înlocui modificarea.

### Anularea

Întregul import este **un singur lot**. Îl anulați din **Geodate → Istoric importuri**, din același
loc și cu același buton care anulează un GPX greșit. Tot ce a creat dispare; o peșteră pe care doar a
actualizat-o rămâne, pentru că lotul nu a creat-o.

---

## Ce se importă și ce doar se păstrează

Lungimea, denivelarea, denivelarea negativă, altitudinea, clasa de protecție, masivul și localitatea
cea mai apropiată intră în câmpurile proprii ale peșterii. Descrierea este transformată din
formatarea catalogului în text simplu, iar una foarte lungă este scurtată, cu o notă care spune asta
și un link către pagina completă.

Codurile proprii ale registrului — identificatorul, codul de județ, codul de rocă, numărul
hidrologic, identificatorul de bazin, codul ariei protejate, interesul științific și dacă este
consemnată ca dispărută — se păstrează pe peșteră ca proprietăți suplimentare și se repetă într-o
notă scurtă la finalul descrierii, ca să le vadă un cititor fără să le caute.

**Două sunt păstrate intenționat așa cum sunt scrise, fără a fi interpretate.** **Codul de rocă**
(`00`, `05`, …) nu are o legendă publicată, așa că nu este transformat în tip de rocă — a completa o
geologie pe ghicite ar fi mai rău decât a lăsa câmpul gol. **Identificatorul de bazin** este un număr
ale cărui denumiri acea interfață nu le publică.

**Localitatea cea mai apropiată se păstrează unde se păstrează o localizare** — în câmpul de adresă
al peșterii, care este ascuns celor ce nu au voie să vadă poziția exactă a unei peșteri protejate —
și nu în descriere, care se arată tuturor.

---

## Cum să fim oaspeți buni

Această instalare vorbește cu speologie.org **o cerere pe rând, cu o pauză între ele**. Este
intenționat și de aceea o căutare durează o clipă. Nu există buton care să descarce registrul, nu
există sincronizare programată și nu există cale de a-l parcurge de la început.

Acel registru este întreținut de voluntari și plătit de cineva. Peșterile importate de acolo
păstrează un link către pagina din care provin, așa că meritul călătorește împreună cu datele. Dacă
clubul dumneavoastră are nevoie de întregul catalog, nu doar de peșterile la care lucrați, întrebați-i
— nu-l luați pagină cu pagină.

---

[← Referință](README.md)
