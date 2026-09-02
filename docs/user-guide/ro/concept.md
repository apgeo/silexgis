# Ce este SilexGIS

🇬🇧 [English](../concept.md) · 🇷🇴 **Română**

[← Înapoi la ghid](README.md) · Urmează: [Primii pași](first-steps.md)

---

## Într-un paragraf

SilexGIS este o aplicație web pentru stocarea, vizualizarea și editarea datelor despre peșteri
și carst. Un club de speologie, o echipă de cercetare sau o persoană o instalează pe un server
propriu, creează conturile care trebuie create, și de atunci încolo aplicația este cadastrul,
arhiva, harta și jurnalul acelui grup, într-un singur loc. Este software liber
(AGPL-3.0-or-later) și vine dintr-un club real — Silex Brașov — care a ținut mai întâi toate
acestea pe hârtie și în foi de calcul.

## Nu este un site public

Acesta este cel mai important lucru de înțeles despre felul în care se comportă aplicația,
pentru că explică o duzină de decizii care altfel ar părea excesiv de precaute.

Aplicația **refuză implicit**. Trebuie să fiți autentificat ca să ajungeți aproape oriunde.
Crearea de conturi este închisă dacă cel care administrează instalarea nu o deschide anume. Nu
există un mod de răsfoire, nici o „hartă publică", nici un catalog de peșteri prin care un
străin să poată naviga.

Ce poate atinge un vizitator **fără cont** este o listă scurtă și numită de uși, fiecare
deschisă printr-un act deliberat:

- un **link de partajare** pe care cineva l-a generat și l-a dat,
- un **album publicat** sau **galeria publică** a instalării,
- un **cod QR** tipărit pe o etichetă la peșteră, care confirmă doar că acel cod este
  înregistrat și nu spune nimic altceva,
- pagina de **dezabonare** dintr-un e-mail.

O instalare care nu publică nimic îi arată unui vizitator pagina de autentificare și nimic
altceva.

## De ce atâta grijă pentru locații

Peșterile sunt vandalizate, jefuite și distruse de trafic. În multe locuri poziția unei intrări
este o informație realmente periculoasă. Așa că aplicația tratează **unde se află o peșteră**
ca pe un lucru separat de **faptul că o peșteră există**, și poate oferi al doilea lucru fără
primul.

O peșteră marcată cu **locație protejată** rămâne lizibilă: numele, descrierea, documentele,
istoricul ei. Ce se reține sunt **coordonatele exacte** — iar reținerea se face pe server, pe
fiecare cale care ar putea-o scurge: harta, tabelele, exporturile, vederile partajate,
fotografiile (a căror poziție de capturare ar da-o de gol), e-mailurile de notificare,
rapoartele.

Subiectul este tratat integral în [Protecția locațiilor](admin/location-protection.md). Merită
citit chiar dacă nu administrați nimic, pentru că explică de ce doi oameni care se uită la
același ecran pot vedea, pe bună dreptate, lucruri diferite.

## Ce conține

Un inventar aproximativ al lucrurilor care trăiesc într-o instalare:

- **Peșteri** — cu nume și alte toponime, cod de identificare, localizare (bazin, vale, cea mai
  apropiată adresă), geologie, morfometrie, date despre descoperire, clasă de protecție, stadiu
  al explorării și una sau mai multe **intrări** cu coordonate precise.
- **Fenomene de suprafață** — doline, falii, izvoare, pereți, orice se poate desena. Tipizate,
  cu simboluri și cu proprietăți proprii.
- **Topografii** — modele Therion `.lox` și Survex `.3d`, pereți de peșteră ca `.stl`,
  poligonații proiectate pe hartă și desenate în 3D, plus o arhivă a surselor topografice din
  care au fost compilate.
- **Geodate** — GPX, KML/KMZ, shapefile, GeoJSON, WKT, CSV cu poziții; și hărți raster
  georeferențiate servite ca straturi suprapuse.
- **Fotografii** — o galerie, albume, credit și licență, și un drum de la cardul de memorie
  până la înregistrări reale pe hartă.
- **Documente** — arhiva clubului: rapoarte, autorizații, buletine, scanări, audio, video —
  clasate într-un arbore de **dulapuri** și căutabile după ce scrie în ele.
- **Ture** — planificate, desfășurate și redactate, cu cine a fost, ce au făcut, ce au măsurat
  și ce au lăsat deschis.
- **Evenimente și tabere** — ședințele, instruirile și expedițiile clubului.
- **Persoane** — conturi, o listă de speologi (inclusiv persoane fără cont) și grupuri de
  speologie.

## Ce nu este, în mod deliberat

- **Nu este multi-organizație.** O instalare servește un singur grup. Nu există noțiunea de
  organizații separate care împart un server.
- **Nu este un program de prelucrare topografică.** Afișează ce produc Therion și Survex; nu le
  înlocuiește. Vă păstrează sursele ca să puteți recompila mai târziu.
- **Nu funcționează offline în browser.** Clientul web are nevoie de conexiune. Offline-ul este
  rostul aplicației de telefon — vedeți
  [Telefon și sincronizare offline](features/mobile-sync.md).
- **Nu este perfectă pe telefon.** Funcționează pe telefon, realmente, inclusiv desenarea
  geometriei cu degetul. Dar este gândită pentru un ecran mare și se vede.

## Cine ce face

Nu există funcții fixe. Există **grupuri de permisiuni** — seturi numite de reguli în care
cineva vă adaugă — plus ce dețineți, plus vizibilitatea pe care o poartă fiecare înregistrare.
În practică, majoritatea instalărilor ajung la ceva de felul:

| Ce fel de persoană | De obicei poate |
|---|---|
| Un membru | Să citească ce împarte clubul, să-și înregistreze turele, să încarce fotografii, să răspundă la invitații |
| Un editor / topograf | Toate cele de mai sus, plus să creeze și să corecteze peșteri, elemente și topografii |
| Un arhivar | În plus, să claseze documente în dulapuri și să îngrijească biblioteca |
| Un comitet / responsabil de acces | În plus, să vadă locațiile exacte ale peșterilor protejate |
| Un administrator complet | Tot, inclusiv conturi, permisiuni și setările instalării |

Acestea sunt convenții, nu funcții predefinite. Vedeți
[Permisiunile explicate](admin/permissions.md).

## De unde vine aplicația

| Versiune | Ani | Stadiu |
|---|---|---|
| v1 | ~2014–2022 | PHP/MySQL, instrumentul original al clubului, încă în funcțiune |
| v2 | 2022 | O rescriere parțială în React/Laravel, abandonată |
| **v3** | 2026– | Aceasta: o rescriere completă, versiunea descrisă de acest ghid |

v3 este o implementare de la zero. Acolo unde face ceva altfel decât v1, este de obicei o
decizie, nu o omisiune.

---

Urmează: [Primii pași](first-steps.md) · [Noțiuni de bază](core-concepts.md)
