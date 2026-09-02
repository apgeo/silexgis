# Flux: înregistrarea unei peșteri noi

🇬🇧 [English](../../workflows/record-a-cave.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Peșteri și intrări](../features/caves-and-entrances.md)

---

Există trei drumuri. Alegeți-l pe cel care se potrivește cu ce aveți efectiv în mână.

| Aveți | Folosiți |
|---|---|
| O poziție și un nume, stând la birou | **Varianta A** — harta |
| Un fișier GPS cu zeci de puncte | [Importul unui fișier GPS](import-a-gps-file.md) |
| Un card de memorie cu fotografii | [Fotografii în locuri](photographs-to-places.md) |
| O fișă de hârtie și nicio coordonată | **Varianta B** — formularul |

---

## Varianta A: de pe hartă

Este cel mai rapid mod de a pune o gaură reală în cadastru.

1. Deschideți **Harta** și navigați aproximativ în locul potrivit. Caseta de căutare acceptă
   nume de locuri, nu doar nume de înregistrări.
2. **Clic dreapta** acolo unde este intrarea → **Peșteră nouă aici**. (Sau folosiți unealta
   *Peșteră nouă aici* din bara de editare și faceți clic pe hartă.)
3. Se deschide un formular cu coordonatele deja completate din locul în care ați dat clic.
4. Dați-i un **nume**. Restul poate aștepta.
5. Hotărâți acum două lucruri, pentru că se schimbă greu în mintea oamenilor mai târziu:
   - **Vizibilitatea** — privată, grup de speologie, autentificați, publică.
   - **Locație protejată** — bifați dacă poziția exactă trebuie reținută față de cine nu are
     dreptul anume. Vedeți [Protecția locațiilor](../admin/location-protection.md).
6. Salvați.

Acum aveți o peșteră cu o intrare. Tot ce urmează este rafinare.

## Varianta B: din formular

**Peșteri → Peșteră nouă.** Același formular, fără poziție. Puteți adăuga intrări după aceea
din pagina peșterii, fie desenând pe hartă, fie tastând coordonatele.

## Completarea peșterii

Formularul unei peșteri este împărțit în secțiuni. Nimic nu este obligatoriu în afară de nume.

| Secțiune | Conține |
|---|---|
| **Identificare** | Nume, alte toponime, cod de identificare, tip de peșteră, descriere, site web |
| **Localizare** | Regiune, bazin hidrografic, vale, râu tributar, cea mai apropiată adresă, număr cadastral, note de localizare |
| **Geologie** | Tip de rocă, vârsta rocii |
| **Morfometrie** | Lungime cartată și estimată, extindere reală și proiectată, denivelare pozitivă/negativă/potențială, altitudine, volum, suprafață, indice de ramificare, vârsta peșterii |
| **Stare** | Stadiul explorării, clasa de protecție, peșteră amenajată și lungimea amenajată |
| **Descoperire** | Data descoperirii, descoperitor |
| **Acces** | Vizibilitate, locație protejată |

Nu vă simțiți obligat să completați morfometria de mână. Dacă încărcați o topografie, aplicația
calculează cifrele acelea din linia topografică și le arată alături de ce scrie în înregistrare
— inclusiv când cele două nu se potrivesc, ceea ce spune cu voce tare în loc să aleagă un
câștigător. Vedeți
[Măsurători și statistici](../features/measurements-and-statistics.md).

## Adăugarea celorlalte intrări

Din pagina peșterii: **Adaugă intrare**. Fiecare intrare poartă:

- **coordonatele** proprii (desenate pe hartă sau tastate),
- **altitudinea**,
- un **tip de intrare**,
- o **calitate a poziției** — necunoscută, GPS, de pe hartă, estimată — despre care merită să
  fiți sincer, pentru că este diferența dintre „mergi aici" și „caută pe versantul ăsta",
- data **ridicării**,
- un indicator **principală**; intrarea principală este cea pe care se bazează implicit alte
  câteva lucruri.

## Ce atașați mai departe

Odată ce peștera există, pagina ei crește secțiuni pe măsură ce o hrăniți:

- **Fotografii și documente** — trageți-le pe pagină. O fotografie poate fi marcată cu stea ca
  *imagine principală*. Vedeți [Fotografii](../features/photographs.md).
- **Poligonații și modele 3D** — încărcați Therion `.lox`, Survex `.3d` sau pereți ca `.stl`.
  Vedeți [Topografii, poligonații și modele 3D](../features/surveys-and-models.md).
- **Surse topografice** — arhivați fișierele `.th`, `.th2`, `.thconfig`, `.svx`, `.log` de
  compilare sau `.zip`-ul unei aplicații de topografie din care a fost făcută topografia
  compilată. Nimic nu le citește; sunt păstrate ca să se poată recompila când uneltele care au
  produs exportul vor fi mers mai departe.
- **Etichete** — libere, și filtrabile în tabele și pe straturile hărții.
- **Legături** — spre raportul care o descrie, spre peștera de alături, spre înregistrarea care
  s-a dovedit a fi aceeași gaură. Vedeți [Legături](../features/links.md).
- **Ture** — pe acestea nu le adăugați aici. Secțiunea *Ture* a unei peșteri se completează
  singură din turele care au numit-o.
- **Coduri tipărite** — dacă clubul prinde etichete QR pe pereții peșterilor, de aici le
  publicați sau le retrageți. Vedeți
  [Partajare și pagini publice](../features/sharing-and-public-pages.md#coduri-qr-tipărite).

## Corectarea unei greșeli

Fiecare modificare este înregistrată. Secțiunea **Istoric** a unei peșteri arată ce s-a
schimbat, când și de către cine, și permite **restaurarea unei singure valori** sau
**restaurarea tuturor valorilor de dinainte de o modificare**. Valorile care fac parte dintr-o
locație protejată sunt afișate ca ascunse, nu expuse prin istoric. Vedeți
[Istoric și modificări](../features/history-and-audit.md).

## Două lucruri de hotărât ca club, o singură dată

**Când marcați o locație ca protejată?** Stabiliți regula o dată și scrieți-o în notele
clubului, pentru că este mult mai ușor de aplicat consecvent decât de reparat după aceea.

**Ce intră în codul de identificare?** Aplicația nu impune o schemă. Dacă există una în
cadastrul național, folosiți-o; codul este căutabil și este ceea ce rezolvă o etichetă QR.

---

Urmează: [Importul unui fișier GPS](import-a-gps-file.md) ·
Referință: [Peșteri și intrări](../features/caves-and-entrances.md)
