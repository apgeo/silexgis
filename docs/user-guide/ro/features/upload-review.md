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
- **O fotografie sau un document** arată titlul, numele și mărimea fișierului. Fișierul în sine
  este păstrat până se decide linia, iar imaginea apare după ce este acceptat.
- **O înregistrare** arată titlul, jurnalul, orele și scanările ei.
- **Reperele unei hărți raster** arată locul lor pe hartă doar acolo unde l-a trimis serverul. Un
  reper pe un loc a cărui poziție nu ai voie să o vezi apare fără ea — vezi
  [Protecția locațiilor](../admin/location-protection.md).

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
| Accepți **o parte dintr-o modificare** | Deschide linia, debifează câmpurile pe care nu le vrei, apasă **Acceptă**. Doar modificarea unui loc se poate accepta câmp cu câmp; orice altceva se acceptă sau se respinge întreg |
| Decizi **mai multe linii deodată** | Bifează-le — sau folosește **Selectează** pentru *toate liniile în așteptare* ori *tot ce e de un fel*, sau bifează căsuța din titlul unei peșteri pentru a lua toate liniile în așteptare de sub ea — apoi **Acceptă selecția** / **Respinge selecția** |

Nu poți decide o încărcare trimisă de tine, oricare ar fi drepturile tale: o decide alt verificator.

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

Apasă pe un rând ca să o deschizi. Vezi aceeași pagină ca verificatorul, fără butoane: ce s-a
propus, linie cu linie, și ce s-a ales pentru fiecare. Nu îți poți decide propria încărcare,
oricare ar fi drepturile tale. O încărcare respinsă nu se scrie nicăieri; trimite-o din nou,
schimbată, dacă trebuia să ajungă acolo.

---

## Ce urmează

Cel care a încărcat este anunțat despre fiecare decizie. Pe telefon, rândurile apar ca acceptate
sau respinse, cu nota ta; pe web, încărcarea este listată la **Geodate → Importuri** cu aceeași
stare. Liniile acceptate se scriu exact cum s-ar fi scris o încărcare pe care nimeni nu a reținut-o,
cu drepturile celui care a încărcat și cu tine numit drept cel care a decis; Istoricul fiecărui loc
spune asta, iar o încărcare acceptată poate fi anulată ca oricare alta.
