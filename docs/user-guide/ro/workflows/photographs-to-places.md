# Flux: transformarea fotografiilor unei ture în locuri

🇬🇧 [English](../../workflows/photographs-to-places.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Fotografii](../features/photographs.md)

---

V-ați întors dintr-un weekend cu patru sute de poze și niciun punct GPS. Telefoanele moderne și
multe aparate scriu o poziție în fiecare cadru. Acest flux transformă asta în înregistrări pe
hartă — fără să vă pună să respingeți patruzeci de puncte unul câte unul.

**Nimic nu se scrie vreodată înapoi în fișierele dumneavoastră de imagine.** Fiecare poziție,
nume și decizie trăiește în verificare și apoi în înregistrările pe care le creează.

---

## Pasul 1 — Deschideți verificarea

Două căi:

- **Geodate → Din fotografii**, sau
- din pagina unei ture: **Derivă elemente din fotografiile acestei ture** — care clasează totul
  sub acea tură.

Apoi lăsați pozele înăuntru.

## Pasul 2 — Înțelegeți ce a făcut, înainte să atingeți ceva

Verificarea grupează pozele în **locuri**, nu în puncte. Douăsprezece fotografii ale unei
intrări sosesc ca *un singur candidat cu o galerie*. Un puț fotografiat de la buză și de la bază
este o judecată de valoare, motiv pentru care distanța de grupare este o setare.

Fiecare rând de candidat arată: **Poze · Nume · Devine · Tip · Poziție · Deja existent ·
Decizie**.

Coloana **Poziție** este cea de citit cu atenție, pentru că spune *cum* a fost plasat locul și
*cât valorează asta*:

| Sursă | Înseamnă |
|---|---|
| **De la aparat** | Poziția pe care a înregistrat-o aparatul când s-a făcut poza |
| **Dintr-un traseu** | Dedusă comparând momentul capturii cu un traseu înregistrat |
| **Plasată manual** | Cineva a tras-o pe hartă |
| **Neplasată** | Nici aparatul, nici un traseu nu au plasat-o |

Fixările de la aparat poartă și o calitate — *excelentă / bună / moderată / necunoscută* — așa
cum a raportat-o aparatul.

Dacă o poză a înregistrat **încotro era îndreptat aparatul**, acel azimut este desenat pe hartă.
Adesea asta transformă „undeva pe versantul ăsta" într-o gaură la care puteți merge înapoi.

## Pasul 3 — Salvați pozele fără poziție

Verificarea vă spune câte sunt. Două moduri de a le repara:

**Potrivirea cu un traseu.** Alegeți un traseu GPX pe care l-ați parcurs. Apoi stabiliți:

- **Decalajul ceasului aparatului** — adăugat la ora declarată a fiecărei poze înainte de
  comparație. Ceasurile aparatelor derivează, iar unul lăsat pe alt fus orar este greșit cu ore
  întregi arătând perfect plauzibil. Acest câmp este diferența dintre o plasare și o ficțiune.
- **Potrivește în limita de** *n* secunde — cât de departe de o poziție înregistrată mai poate
  fi plasată o poză. Dincolo de asta, traseul chiar nu spune nimic despre unde a fost făcută.

Fiecare rând potrivit arată apoi la câte secunde se află de o fixare reală. Câteva secunde
înseamnă o plasare; două minute înseamnă o presupunere.

**Sau plasați-o manual.** Selectați rândul, **Plasează pe hartă**, clic. Se scrie în verificare,
niciodată înapoi în fișier.

## Pasul 4 — Stabiliți opțiunile

**Locuri și grupare**
- *Fiecare loc devine* — felul implicit pentru un candidat nou.
- *Prefix de nume*.
- *Două poze sunt același loc în limita de* — distanța de grupare.

**Ce se creează**
- *Caută ceva deja existent în raza de n metri* — ca la importul GPS, se compară doar cu
  obiectele a căror poziție exactă aveți voie s-o vedeți.
- *Altitudinea de capturare* — lăsată deoparte dacă nu o cereți. Altitudinea GPS a unui telefon
  este cel mai puțin fiabil dintre cele trei numere pe care le raportează.

## Pasul 5 — Decideți fiecare loc

Fiecare candidat vă oferă ce se află deja în cadastru prin apropiere. Clasarea unei poze pe
peștera căreia îi aparține este o singură apăsare — nu trebuie să creați o înregistrare duplicat
doar ca să aveți unde pune fotografia.

Sau **creați una nouă**, alegeți-i tipul, dați-i nume.

## Pasul 6 — Confirmați, și anulați dacă a fost greșit

**Creează *n* selectate.** Nimic nu există până atunci.

Iar ca la importul GPS, întreaga confirmare apare la **Geodate → Importuri** și iese înapoi
dintr-o singură apăsare — inclusiv pozele agățate de peșteri care existau deja.

---

## Ce se întâmplă cu fotografiile în sine

Ajung în [galerie](../features/photographs.md). De acolo puteți:

- să le dați **legende**, un **fotograf** (din listă, sau un nume tastat) și o **licență**
  (CC0, familia CC BY, sau toate drepturile rezervate),
- să le puneți în **albume**, să le aranjați ordinea, să alegeți o copertă,
- să le **rotiți**, în masă,
- să marcați una cu stea ca **imagine principală** a unei peșteri sau a unei ture,
- să **publicați** fotografii individuale în galeria publică a instalării, ceea ce este o
  decizie separată de cine are voie să le citească.

> **Descărcarea originalelor este controlată.** Dacă punctul de capturare al unei fotografii ar
> localiza o peșteră a cărei poziție exactă nu aveți voie s-o vedeți, vi se spune că nu puteți
> descărca originalul. Randarea se vede în continuare. Aceasta este
> [protecția locațiilor](../admin/location-protection.md) făcându-și treaba.

---

Urmează: [Planificarea și redactarea unei ture](plan-and-log-a-trip.md) ·
Referință: [Fotografii](../features/photographs.md)
