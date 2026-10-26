# Flux: înregistrarea unei peșteri noi

🇬🇧 [English](../../workflows/record-a-cave.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Peșteri și intrări](../features/caves-and-entrances.md)

---

Există trei drumuri. Alege-l pe cel care se potrivește cu ce ai efectiv în mână.

| Ai | Folosește |
|---|---|
| O poziție și un nume, stând la birou | **Varianta A** — harta |
| Un fișier GPS cu zeci de puncte | [Importul unui fișier GPS](import-a-gps-file.md) |
| Un card de memorie cu fotografii | [Fotografii în locuri](photographs-to-places.md) |
| O fișă de hârtie și nicio coordonată | **Varianta B** — formularul |

---

## Varianta A: de pe hartă

Este cel mai rapid mod de a pune o gaură reală în registru.

1. Deschide **Harta** și navighează aproximativ în locul potrivit. Caseta de căutare acceptă
   nume de locuri, nu doar nume de înregistrări.
2. **Clic dreapta** acolo unde este intrarea → **Peșteră nouă aici**. (Sau folosește unealta
   *Peșteră nouă aici* din bara de editare și fă clic pe hartă.)
3. Se deschide un formular cu coordonatele deja completate din locul în care ai dat clic.
4. Dă-i un **nume**. Restul poate aștepta.
5. Hotărăște acum două lucruri, pentru că se schimbă greu în mintea oamenilor mai târziu:
   - **Vizibilitatea** — privată, grup de speologie, autentificați, publică.
   - **Locație protejată** — bifează dacă poziția exactă trebuie reținută față de cine nu are
     dreptul anume. Vezi [Protecția locațiilor](../admin/location-protection.md).
6. Salvează.

Acum ai o peșteră cu o intrare. Tot ce urmează este rafinare.

## Varianta B: din formular

**Peșteri → Peșteră nouă.** Același formular, fără poziție. Poți adăuga intrări după aceea
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

Nu te simți obligat să completezi morfometria de mână. Dacă încarci o topografie, aplicația
calculează cifrele acelea din linia topografică și le arată alături de ce scrie în înregistrare
— inclusiv când cele două nu se potrivesc, ceea ce spune cu voce tare în loc să aleagă un
câștigător. Vezi
[Măsurători și statistici](../features/measurements-and-statistics.md).

## Adăugarea celorlalte intrări

Din pagina peșterii: **Adaugă intrare**. Fiecare intrare poartă:

- **coordonatele** proprii — desenate pe hartă, sau scrise ori lipite în **Scrie sau lipește o
  poziție** în oricare dintre [formatele de coordonate](../features/coordinates.md) (UTM, Stereo 70,
  grade și minute…), care completează longitudinea și latitudinea ce se salvează,
- **altitudinea**,
- un **tip de intrare**,
- o **calitate a poziției** — necunoscută, GPS, de pe hartă, estimată — despre care merită să
  fii sincer, pentru că este diferența dintre „mergi aici" și „caută pe versantul ăsta",
- data **topografiei**,
- un indicator **principală**; intrarea principală este cea pe care se bazează implicit alte
  câteva lucruri.

## Ce atașezi mai departe

Odată ce peștera există, pagina ei crește secțiuni pe măsură ce o hrănești:

- **Fotografii și documente** — trage-le pe pagină. O fotografie poate fi marcată cu stea ca
  *fotografie reprezentativă*. Vezi [Fotografii](../features/photographs.md).
- **Drumuiri și modele 3D** — încarcă Therion `.lox`, Survex `.3d` sau pereți ca `.stl`.
  Primul model de fiecare fel devine modelul curent al peșterii; când o încărcare ulterioară este
  o topografie corectată, dialogul de încărcare oferă să o facă curentă după ce a fost citită.
  Vezi [Topografii, drumuiri și modele 3D](../features/surveys-and-models.md).
- **Surse topografice** — arhivează fișierele `.th`, `.th2`, `.thconfig`, `.svx`, `.log` de
  compilare sau `.zip`-ul unei aplicații de topografie din care a fost făcută topografia
  compilată. Doar jurnalul de compilare este citit, pentru erorile de buclă pe care le raportează;
  restul se păstrează așa cum este, ca să se poată recompila când uneltele care au produs exportul
  vor fi mers mai departe.
- **Etichete** — libere, și filtrabile în tabele și pe straturile hărții.
- **Legături** — spre raportul care o descrie, spre peștera de alături, spre înregistrarea care
  s-a dovedit a fi aceeași gaură. Vezi [Legături](../features/links.md).
- **Ture** — pe acestea nu le adaugi aici. Secțiunea *Ture* a unei peșteri se completează
  singură din turele care au numit-o.
- **Coduri tipărite** — dacă clubul prinde etichete QR pe pereții peșterilor, de aici le
  publici sau le retragi. Vezi
  [Partajare și pagini publice](../features/sharing-and-public-pages.md#coduri-qr-tipărite).

## Corectarea unei greșeli

Fiecare modificare este înregistrată. Secțiunea **Istoric** a unei peșteri arată ce s-a
schimbat, când și de către cine, și permite **restaurarea unei singure valori** sau
**restaurarea tuturor valorilor de dinainte de o modificare**. Valorile care fac parte dintr-o
locație protejată sunt afișate ca ascunse, nu expuse prin istoric. Vezi
[Istoric și modificări](../features/history-and-audit.md).

## Două lucruri de hotărât ca club, o singură dată

**Când marchezi o locație ca protejată?** Stabilește regula o dată și scrie-o în notele
clubului, pentru că este mult mai ușor de aplicat consecvent decât de reparat după aceea.

**Ce intră în codul de identificare?** Aplicația nu impune o schemă. Dacă există una în
Cadastrul național, folosește-o; codul este căutabil și este ceea ce rezolvă o etichetă QR.

---

Urmează: [Importul unui fișier GPS](import-a-gps-file.md) ·
Referință: [Peșteri și intrări](../features/caves-and-entrances.md)
