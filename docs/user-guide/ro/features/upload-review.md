# Verificarea încărcărilor

🇬🇧 [English](../../features/upload-review.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Telefon și sincronizare offline](mobile-sync.md) ·
[Încărcări](uploads.md) ·
[Protecția locațiilor](../admin/location-protection.md)

---

Un administrator poate marca un cont cu **„Încărcările necesită verificare"**. Tot ce trimite acel
cont — din aplicația de telefon sau importând un fișier — **așteaptă**. Nimic din el nu se scrie pe
hărți, în documente sau în ture până nu spune „da" cineva care are dreptul să-l verifice.

Pagina aceasta este pentru acel cineva. Are nevoie de dreptul **Revizuirea încărcărilor**:
*citire* pentru a vedea încărcările în așteptare, *scriere* pentru a le decide.

**Un verificator vede în întregime tot ce așteaptă.** Fiecare linie este arătată așa cum a fost
trimisă, orice altceva ai voie să citești: poziția exactă a unei peșteri sau a unei intrări
protejate, fotografia unei peșteri pe care altfel nu ai putea-o deschide, o înregistrare pe care nu
o poți citi în altă parte. Nu poți judeca ce nu ți se arată. Aceasta privește doar ce așteaptă aici
— pe hartă, în registru și oriunde altundeva se aplică în continuare drepturile tale obișnuite și
protecția locațiilor — iar ce accepți se scrie tot cu drepturile celui care a încărcat, niciodată cu
ale tale. Pagina spune asta în partea de sus, iar ecranele care acordă dreptul avertizează: dă-l doar
persoanelor de încredere pentru toate peșterile.

---

## Coada

**Încărcări de verificat** din meniul lateral listează fiecare încărcare în care mai așteaptă ceva,
**cea mai veche prima**:

| Coloană | |
|---|---|
| **Trimis de** | Contul care a trimis-o |
| **Așteaptă din** | Când a sosit și cu cât timp în urmă |
| **Sursă** | Telefonul sau felul de import de fișier |
| **Ce conține** | Număr pe feluri: locuri, fotografii și documente, înregistrări, scanări, hărți raster, repere pe hartă… |
| **În așteptare** | Câte linii nu sunt încă decise |
| **Fișiere păstrate** | Mărimea fișierelor păstrate pe server până se decide încărcarea |

Apasă un rând pentru a deschide încărcarea.

---

## Citirea unei încărcări

Liniile încărcării sunt listate **sub peștera de care țin**: o peșteră, intrările și locurile ei,
înregistrările făcute în ea, fotografiile atașate. Liniile care nu țin de nicio peșteră — de
pildă utilizatorii telefonului — vin la urmă. Fiecare linie spune ce este, dacă este **nouă**, o
**modificare** sau o **ștergere**, și de ce are nevoie: *„Are nevoie de: Peștera X"* înseamnă că
linia nu poate fi acceptată fără Peștera X.

Apasă săgeata de lângă o linie pentru a o citi.

- **Un loc nou** arată valorile cu care ar fi creat.
- **O modificare a unui loc** arată un mic tabel, câmp cu câmp: locul **acum**, lângă ce
  **propune** telefonul, cu o căsuță pentru fiecare câmp. Câmpurile care coincid sunt marcate *la fel*.
- **O fotografie sau un document** arată titlul, numele și mărimea fișierului și — pentru o
  imagine — o copie mică a ei; apasă pe ea ca s-o vezi mai mare. Copia este desenată de server din
  fișierul care așteaptă și nu poartă nimic din datele fișierului (nici poziția camerei). Un fișier
  care nu este imagine nu are copie; este păstrat pe server până se decide linia.
- **O hartă raster** arată titlul, tipul și fișierul, și o copie a imaginii ei în același fel.
- **O înregistrare** arată titlul, jurnalul, orele și scanările ei.
- **O modificare a unei fotografii, a unei înregistrări, a unei scanări sau a unei hărți raster**
  arată același fel de tabel ca modificarea unui loc: fiecare câmp **acum**, lângă ce **propune**
  telefonul, cu o căsuță pentru fiecare câmp — vezi *O parte dintr-o modificare* mai jos.
- **Reperele unei hărți raster** arată unde se află fiecare pe hartă, inclusiv reperul de pe un loc
  protejat.

O linie poate spune și **„Modificat după ce l-a citit telefonul"**: cineva a schimbat acel loc pe
server după ce telefonul și-a luat copia. Vezi [Modificat între timp](#modificat-între-timp) mai jos.

Liniile deja decise sunt ascunse cât timp mai așteaptă ceva; bifează **Arată liniile deja decise**
pentru a le vedea pe toate, cu cine le-a decis și cu nota, dacă există.

---

## Decizia

Fiecare decizie poate purta o **notă pentru cel care a încărcat**. Scrie-o în căsuța de deasupra
liniilor înainte de a apăsa butonul; pleacă odată cu următoarea decizie, iar cel care a încărcat
este anunțat.

| Ca să… | Fă asta |
|---|---|
| Accepți sau respingi **tot** ce așteaptă | **Acceptă tot** / **Respinge tot** (respingerea cere o confirmare) |
| Decizi **o linie** | **Acceptă** / **Respinge** pe linie |
| Accepți **o parte dintr-o modificare** | Deschide linia, debifează câmpurile pe care nu le vrei, apasă **Acceptă**. Se scrie doar ce este bifat; o linie pe care n-ai deschis-o se acceptă întreagă |
| Decizi **mai multe linii deodată** | Bifează-le — sau folosește **Selectează** pentru *toate liniile în așteptare* ori *tot ce e de un fel*, sau bifează căsuța din titlul unei peșteri pentru a lua toate liniile în așteptare de sub ea — apoi **Acceptă selecția** / **Respinge selecția** |

Nu poți decide o încărcare trimisă de tine, oricare ar fi drepturile tale: o decide alt verificator.

Ce se poate accepta câmp cu câmp:

| Modificarea unui/unei… | Câmpuri |
|---|---|
| Loc (peșteră, intrare, alt punct de pe hartă) | Nume, descriere, proprietăți, poziție (punctul, altitudinea, calitatea poziției și semnul de intrare principală merg împreună) |
| Fotografii sau document | Titlu, legendă, fișierul nou (luat întreg sau deloc) |
| Înregistrări | Titlu, descriere, jurnal, început și sfârșit (împreună), peșteră |
| Scanări | Note, loc, ora scanării |
| Hărți raster | Titlu, tipul hărții, ordine, partea peșterii, imaginea nouă |

O linie nouă, o ștergere, legătura unei fotografii cu un loc, un reper pe o hartă raster și un
utilizator al telefonului se acceptă sau se resping mereu întregi. Un fișier nou lăsat nebifat nu
este luat; serverul îl păstrează doar cât păstrează un fișier respins.

### Dependențe

Acceptarea unei linii **acceptă și liniile în așteptare de care are nevoie** — o intrare are nevoie de
peștera ei. Înainte de a se scrie ceva ți se arată ce linii vin odată cu ea. Respingerea unei linii
**respinge și liniile în așteptare care depind de ea** și o spune mai întâi.

### Modificat între timp

Dacă un loc a fost schimbat pe server după ce l-a citit telefonul, acceptarea versiunii telefonului
ar scrie peste acea schimbare. Pagina se oprește și arată care sunt locurile; nu se decide nimic.
Apoi:

- **Scrie peste versiunea de acum** scrie câmpurile acceptate peste locul așa cum este acum.
- **Respinge-le** respinge versiunea telefonului pentru acele locuri.
- **Anulează** lasă totul în așteptare.

### Reluarea unei respingeri

O linie respinsă arată **Acceptă totuși** o vreme (implicit 30 de zile). Merge doar dacă fișierul
este încă păstrat și telefonul nu a mai trimis același loc între timp; altfel pagina spune că nu mai
poate fi schimbată.

### O linie care nu a putut fi scrisă

O linie pe care ai acceptat-o poate totuși să se încheie cu **Nu a putut fi scris** — de exemplu
fiindcă cel care a încărcat a pierdut între timp dreptul de a scrie acolo. Linia spune de ce. Restul
încărcării nu este afectat.

---

## Dacă ți-e contul

Când propriul tău cont este ținut pentru verificare, **Geodate → Importuri** arată **Încărcările
tale în verificare** deasupra listei de confirmări. Fiecare rând este o încărcare: când ai trimis-o,
de unde, ce conține, cum stă — **În așteptare**, **Acceptată**, **Acceptată în parte** sau
**Respinsă** —, cine a decis-o și când, și **nota** lăsată de verificator.

Apasă pe un rând ca să o deschizi. Vezi aceeași pagină ca verificatorul, fără butoane — și, spre
deosebire de un verificator, doar ce îți arată propriile drepturi și în altă parte: ce s-a propus,
linie cu linie, și ce s-a ales pentru fiecare. Nu îți poți decide propria încărcare,
oricare ar fi drepturile tale. O încărcare respinsă nu se scrie nicăieri; trimite-o din nou,
schimbată, dacă trebuia să ajungă acolo.

---

## Ce urmează

Cel care a încărcat este anunțat despre fiecare decizie. Pe telefon, rândurile apar ca acceptate
sau respinse, cu nota ta; pe web, încărcarea este listată la **Geodate → Importuri** cu aceeași
stare. Liniile acceptate se scriu exact cum s-ar fi scris o încărcare pe care nimeni nu a reținut-o,
cu drepturile celui care a încărcat și cu tine numit drept cel care a decis; Istoricul fiecărui loc
spune asta, iar o încărcare acceptată poate fi anulată ca oricare alta.
