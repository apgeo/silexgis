# Hărți raster georeferențiate

🇬🇧 [English](../../features/georeferenced-maps.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Spațiul de lucru al hărții](map-workspace.md) ·
[Geodate](geodata.md)

---

Foi geologice, hărți topografice vechi, hărți turistice, topografii scanate — orice este o
imagine cu o poziție — pot fi suprapuse peste hartă.

**Registru → Geodate → Hărți raster.**

---

## Încărcarea

Trage înăuntru o planșă. Sunt acceptate:

- **GeoTIFF** — georeferențiat, așa cum îl scriu cele mai multe programe GIS;
- **PDF** — o hartă publicată ca document, georeferențiată sau nu;
- **PNG sau JPEG** — o scanare sau o fotografie a unei planșe de hârtie.

Fiecare este convertită în fundal în Cloud-Optimized GeoTIFF: *În așteptare → Se procesează… →
Gata* (sau *Eșuat*).

O planșă care își poartă singură locul — un GeoTIFF, sau un PDF exportat cu georeferențiere — este
gata așa cum este; un PDF georeferențiat este decupat la chenarul hărții, așa că legenda și
marginile rămân în afara hărții. O planșă care **nu poartă niciun loc** — o scanare, un PDF simplu,
un GeoTIFF care se dovedește a nu avea nicio poziție — este desenată și apoi **așteaptă să fie
poziționată**, în loc să eșueze: în listă apare ca *De poziționat*, cu **Poziționează pe hartă…**.

Pentru un PDF, pagina este desenată o singură dată, la o rezoluție pe care instalarea o alege după
scara planșei. **O planșă poate fi desenată din nou** (**Redesenează…**) cu altă pagină, cu alte
straturi ale documentului sau cu altă rezoluție, fără s-o încarci a doua oară. O poziție stabilită
manual se păstrează și se aplică noului desen.

## Poziționarea unei planșe pe hartă

**Poziționează pe hartă…** (sau **Ajustează poziția…** la o planșă deja poziționată) deschide
editorul de poziționare: planșa alături de o hartă cu fundalul pe care îl folosește spațiul de
lucru. La fiecare schimbare vezi chiar transformarea făcută de server a planșei peste acea hartă,
la o opacitate pe care o alegi, împreună cu cât de bine se potrivesc punctele. Trei moduri:

- **Colțuri** — trage cele patru colțuri ale planșei acolo unde le e locul. **Perspectivă** pune
  fiecare colț exact unde l-ai lăsat; **Laturi paralele** păstrează paralele laturile opuse și
  arată cât de departe a ajuns fiecare colț de semnul tău. **Scrie coordonatele colțurilor** le ia
  în schimb longitudinea și latitudinea.
- **Două puncte** — apasă un punct pe planșă, apoi același punct pe hartă, de două ori. Planșa
  este mutată, rotită și scalată fără să fie deformată.
- **Puncte de control** — câte perechi are nevoie planșa: un punct pe planșă și același loc pe
  hartă, apăsate sau scrise. Coordonatele pot fi scrise în WGS 84, în sistemul de lucru al
  instalării sau în rețelele naționale pe care le enumeră. Alege **Transformare**:

  | | |
  |---|---|
  | **Similaritate** | Mută, rotește și scalează planșa fără să o deformeze |
  | **Afină** | O și întinde și înclină; laturile opuse rămân paralele |
  | **Perspectivă** | Pentru o planșă fotografiată din unghi |
  | **Polinomială de gradul 2** | Curbează ușor planșa — hârtie veche sau întinsă inegal |
  | **Polinomială de gradul 3** | O curbează mai liber; are nevoie de multe puncte bine răspândite |
  | **Spline cu plăci subțiri** | Trece exact prin fiecare punct; abaterea afișată pentru fiecare este calculată lăsând pe rând acel punct deoparte |

  **Abaterea** fiecărui punct — cât de departe îl lasă potrivirea de locul unde l-ai pus — este
  listată, iar punctele care se potrivesc cel mai prost sunt marcate. Trage oricare capăt al unei
  perechi ca s-o muți.

Ce mai face editorul:

- **Folosește stațiile marcate pe planșă.** Acolo unde stații topografice au fost arătate pe
  planșă — ca la o [hartă scanată lângă model](surveys-and-models.md#hărți-scanate-lângă-model) —
  ele sunt oferite ca puncte de control gata făcute, topografia dând locul fiecăreia.
- **Contur.** **Desenează conturul** în jurul părții din planșă care este harta; restul — legenda,
  cartușul, marginile — este lăsat în afara hărții. **Folosește chenarul hărții** ia chenarul
  propriu al unui PDF georeferențiat.
- **Poziția proprie a documentului** poate fi văzută în editor și preluată ca punct de pornire pe
  care apoi îl corectezi. Dacă îți ștergi mai târziu poziția, se revine la cea a documentului.
- **Exportă poziția / Importă poziția** — o poziție se schimbă ca **IIIF Georeference
  Annotation**, formatul deschis pe care îl citesc și îl scriu alte unelte IIIF de
  georeferențiere, cu punctele și conturul. O adnotare făcută în altă parte, chiar și pe o imagine
  cu altă rezoluție, ajunge în aceleași locuri.

După salvare, editorul așteaptă ca harta poziționată să fie gata și se oferă s-o arate pe hartă.

**O planșă legată de o peșteră spune unde este acea peșteră.** Editorul avertizează că, dacă
partajezi documentul original al planșei, partajezi și locul peșterii.

Fiecare hartă raster poartă:

| Câmp | |
|---|---|
| **Fel** | Geologică · Topografică · Turistică · Hartă de peșteră · Altele |
| **Opacitate implicită** | 0–1, opacitatea de la care pornește stratul |
| **Atribuire** | Cui aparține foaia |

O hartă raster poate fi **legată de o peșteră**, așa ajungând o topografie scanată lângă peștera
pe care o desenează.

## Filtrarea listei

Pe fila **Hărți raster** a ecranului Geodata, **Condiții…** deschide editorul de condiții pe lumea
hărților georeferențiate — *Tipul hărții*, numele, proprietarul, vizibilitatea, datele și
*Poziția* — cu **Aplică un filtru salvat…** și **Salvează ca filtru…** alături, exact ca la
fișierele geodata și la galerie. Condițiile sunt păstrate în adresa paginii, așa că linkul le
poartă. O hartă legată de o peșteră a cărei poziție nu ai voie s-o vezi exact este lăsată afară
din listă și din numărătoarea ei, oricare ar fi condițiile.

Un plan sau un profil scanat al unei peșteri este desenat în cadrul propriu al topografiei, nu pe
un caroiaj de hartă — un profil nici nu poate fi așezat pe teren — așa că de obicei este mai bine arătat **lângă
topografia 3D** decât pe hartă: acolo nu are nevoie de georeferențiere, ci doar de câteva stații
arătate pe el, iar o echipă urmărită este desenată și pe el. Vezi
[Hărți scanate lângă model](surveys-and-models.md#hărți-scanate-lângă-model).

## Folosirea lor pe hartă

Rasterele gata apar în arborele de straturi la **Hărți georeferențiate** / **Hărți raster**.
Fiecare are propriul control de opacitate.

Unealta de **glisare** — *Compară rasterul cu harta de dedesubt* — pune un separator pe hartă ca
să poți trage rasterul înainte și înapoi peste harta de bază. Aceasta este unealta pentru „se
suprapune corect foaia geologică din anii '50?".

## În vizualizarea 3D

Rasterele pot fi întinse pe glob, cu o rezervă pe care scena o declară singură: **pe glob fiecare
foaie este aplatizată într-o singură imagine**, așa că se înmoaie la apropiere mare. Harta plană
citește fișierul însuși.

Dacă citești detalii fine de pe o foaie, folosește harta 2D.

---

Înrudite: [Spațiul de lucru al hărții](map-workspace.md) · [Vizualizarea 3D](3d-view.md)
