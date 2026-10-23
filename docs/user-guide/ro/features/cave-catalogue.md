# Catalogul național al peșterilor

🇬🇧 [English](../../features/cave-catalogue.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Peșteri și intrări](caves-and-entrances.md) ·
[Geodate și importuri](geodata.md)

---

**Catalogul peșterilor** caută pe [speologie.org](https://www.speologie.org) — registrul comunitar
al peșterilor publicate din România, aproximativ 8.650 la număr — și te lasă să aduci peșteri de
acolo în propria instalare, fără să le retastezi.

Sunt două ecrane. Primul doar caută; nu creează nimic, iar majoritatea vizitelor se opresc acolo,
pentru că de obicei voiai să citești ce spune registrul și să urmezi linkul către el. Al doilea
importă peșterile alese.

## Înainte să funcționeze: o cheie

Catalogul este serviciul altcuiva și se accesează cu o **cheie API personală**. O obții din
*Editează profilul* pe un cont speologie.org și ceri unui administrator s-o configureze pe
această instalare.

Până atunci, ecranul spune asta și nu face nimic altceva. Nu este o defecțiune: majoritatea
instalărilor acestei aplicații nu au nicio legătură cu registrul românesc, iar una fără cheie
funcționează exact ca înainte.

**Căutarea are nevoie de dreptul de a crea peșteri**, deși căutarea nu creează nimic. Cererile pleacă
cu cheia proprie a instalării către un serviciu mic, întreținut de voluntari, așa că pagina este
oferită celor care au un motiv să caute peșteri de adăugat.

---

## Căutarea

Dai o denumire, un județ, sau ambele. O căutare care nu numește niciunul este refuzată — registrul
nu este ceva de răsfoit de la început.

**Județul se alege dintr-o listă, nu se tastează.** Catalogul potrivește județele după codul din
două litere (`BH`, `GJ`, `HD`), exact și cu majuscule. Tastarea lui *Bihor* nu găsește absolut nimic
— de aceea nu există o casetă în care să-l scrii.

### De ce ecranul spune uneori că a căutat trei grafii

Româna se scrie corect cu `ș` și `ț` (cu virgulă dedesubt). Vreme de aproape două decenii, fonturile
și tastaturile din circulație au produs în schimb literele turcești cu sedilă, `ş` și `ţ`, iar foarte
mult text românesc a fost scris așa. O bună parte din acest catalog a fost tastată fără diacritice
deloc.

**Căutarea catalogului le tratează pe toate trei ca pe cuvinte diferite**, iar efectul nu este
subtil:

| ce scrii | ce găsește catalogul |
|---|---|
| `Padis` | 1 peșteră |
| `Padiș` | niciuna |
| `Padiş` | 2 peșteri |
| `Scarisoara` | niciuna — ghețarul este trecut ca *Scărișoara* |

Așadar scrierea fără diacritice, adică exact ce te obligă majoritatea tastaturilor să faci, de
obicei nu găsește **nimic** — iar o peșteră care este în registru arată exact ca una care nu este.

Acest ecran ocolește problema: cere până la două duzini de grafii ale a ceea ce ai scris, toate
într-o singură cerere, și îți spune câte a folosit. Nu trebuie să știi nimic din toate acestea ca să
folosești caseta.

**Soluția este bună, nu perfectă.** O denumire lungă care are nevoie de două accente în litere
diferite poate rămâne totuși în afara a ceea ce se încearcă — *Scarisoara* este exemplul incomod.
Dacă o căutare nu întoarce nimic și crezi că peștera există, scrie diacriticele.

O consecință de reținut: **paginarea dincolo de prima pagină este aproximativă.** Fiecare grafie este
paginată separat la celălalt capăt, așa că o pagină ulterioară poate fi scurtă sau poate repeta ceva.
Mai bine restrângi căutarea decât să paginezi în adâncime.

### Restrângerea după bazin hidrografic

Registrul așază fiecare peșteră într-un bazin hidrografic, iar bazinele formează un arbore — grupă
montană, masiv, grup de bazine, vale. Alegi unul și rezultatele se restrâng la el **și la tot ce
se află în el**, așa că alegerea *Munților Bihorului* îți dă toate văile din ei, nu doar pe cea care
le poartă numele.

Două precizări oneste. Catalogul însuși nu poate căuta după bazin, așa că filtrul restrânge ceea ce
a adus căutarea, nu îi pune registrului o altă întrebare — ecranul îți spune câte peșteri au fost
citite pentru a produce răspunsul. Iar un bazin singur nu este o căutare: tot ai nevoie de o
denumire sau de un județ, fiindcă „toate peșterile din acest bazin" ar însemna parcurgerea întregului
registru.

### Ce îți spune fiecare rând

Lungimea, denivelarea, altitudinea și clasa de protecție așa cum le ține registrul, plus două lucruri
pe care el nu le știe:

- **Dacă peștera este deja în instalarea ta.** Dacă ai dreptul s-o vezi, rândul face
  legătura către ea. Dacă nu, rândul tot spune că intrarea este deja aici, fără să numească peștera —
  altfel ai fi invitat să imporți o a doua copie a ceva ce un coleg are deja.
- Etichete pentru o peșteră consemnată ca **dispărută** sau ca având **sifon**.

Clic pe denumire deschide tot ce ține registrul, inclusiv descrierea. **Deschide pe speologie.org**
duce la pagina peșterii de acolo.

---

## Importul

**Importă** pe un rând, sau bifează mai multe rânduri și folosește **Importă selecția**. În ambele
cazuri ajungi pe ecranul de import cu acele peșteri listate, și încă nu s-a creat nimic.

### Primul lucru de înțeles: nu există coordonate

**Catalogul nu publică coordonate. Nici aproximative, nici ascunse — câmpul nu există.**

Așadar o peșteră importată de acolo ajunge **fără poziție**. Este o peșteră reală în registrul
tău, apare în lista de peșteri și în căutare, are lungimea, denivelarea și descrierea ei —
și nu este pe nicio hartă, pentru că poziția unei peșteri de aici este poziția intrării ei
principale, iar ea nu are intrare.

Ai două opțiuni oneste, iar ecranul le oferă pe amândouă:

1. **Las-o nepoziționată.** Perfect rezonabil. Le găsești mai târziu la **Peșteri**, cu filtrul
   *fără poziție*, și le așezi pe măsură ce afli unde sunt.
2. **Poziționeaz-o acum.** Apasă **Așază pe hartă** pe un rând și dă clic pe hartă. Se creează o
   intrare principală în punctul indicat, consemnată drept *citită de pe hartă*, nu ca măsurătoare
   GPS — pentru că asta și este.

Nimic nu inventează o poziție în locul tău. Centrul unui județ nu este o peșteră, iar o
coordonată care pare măsurată și nu este e mai rea decât un câmp gol.

### Alegeri care se aplică la tot ce imporți

**Vizibilitatea** (privată dacă nu spui altfel), un **grup de speologie** de care să fie legate, și
dacă sunt **cu locație protejată**. Sunt aceleași alegeri pe care le oferă orice import de aici, iar
valorile implicite sunt cele mai restrictive.

### Pentru fiecare peșteră

- **Ce se face** — se creează, se actualizează, sau se lasă neatinsă. O peșteră importată anterior
  este propusă spre actualizare, nu ca duplicat.
- **Tipul** — dedus din denumire (*Peștera …* este peșteră, *Avenul …* este aven), cu aceleași reguli
  de denumire folosite de importul din fișier. Îl poți schimba dacă denumirea induce în eroare.
- **Poziția** — opțională, ca mai sus.

### Ce rescrie actualizarea

Actualizarea unei peșteri venite din catalog rescrie **câmpurile care aparțin catalogului**:
denumirea, descrierea, regiunea, altitudinea, lungimea, denivelarea și clasa de protecție. Tot
restul a ceea ce ai consemnat pe acea peșteră — intrări, topografii, fotografii, documente,
etichete, legături, notele tale — rămâne neatins. Dacă ai modificat manual unul dintre
câmpurile catalogului, o actualizare îți va înlocui modificarea.

### Anularea

Întregul import este **un singur lot**. Îl anulezi din **Geodate → Istoric importuri**, din același
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

**Codul de rocă este păstrat intenționat așa cum este scris, fără a fi interpretat.** `00`, `05` și
celelalte nu au o legendă publicată, așa că nu sunt transformate în tip de rocă — a completa o geologie
pe ghicite ar fi mai rău decât a lăsa câmpul gol.

**Bazinul este cazul opus și este rezolvat.** Interfața catalogului răspunde cu un simplu număr, dar
site-ul publică denumirile tuturor celor 653 de bazine, așa că această instalare poartă o copie a
acelui arbore. O peșteră importată primește o denumire reală de bazin în câmpul ei, iar descrierea
poartă tot drumul până la el — *Munții Apuseni › Munții Bihorului › Bazinele închise › Bazinul Padiș*
— ceea ce așază peștera în țară pentru cineva care n-a auzit niciodată de acel bazin. Numărul rămâne
lângă denumire, ca să poți verifica citirea față de catalog.

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
clubul tău are nevoie de întregul catalog, nu doar de peșterile la care lucrezi, întreabă-i
— nu-l lua pagină cu pagină.

---

[← Referință](README.md)
