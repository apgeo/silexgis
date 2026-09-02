# Flux: clasarea arhivei clubului

🇬🇧 [English](../../workflows/file-the-archive.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință:
[Documente și dulapuri](../features/documents-and-cabinets.md) ·
[Încărcări](../features/uploads.md)

---

O cutie de hârtii, un disc plin de scanări, treizeci de ani de buletine. Acesta este fluxul
pentru a le introduce și a le face găsibile.

---

## Pasul 1 — Proiectați întâi arborele de clasare

**Dulapurile** sunt arborele în care trăiesc documentele: *Arhiva clubului / Buletine / 1987*.
Petreceți o oră pe asta înainte să încărcați ceva, pentru că un dulap este și **unitatea pe care
se acordă permisiuni** — o singură regulă poate da unui comitet toată arhiva, în loc de o regulă
per document.

Câteva lucruri de știut în timp ce proiectați:

- **Un document poate sta în mai multe dulapuri deodată.** Nu există întrebarea mută-sau-copiază
  și nimic nu rămâne orfan pentru că aparține în două locuri.
- **Clasarea mută cine poate citi.** Punerea unui document pe un raft pe care îl numește o
  regulă dă acces subiecților acelei reguli. Aplicația o spune chiar lângă control.
- Dulapurile se cuibăresc, până la o adâncime limitată.

### Ce aterizează aici

Fiecare dulap poate purta valori implicite aplicate a tot ce se încarcă în el:

- **Tip de document** (sau *la fel ca dulapul de deasupra*),
- **Cine poate citi**,
- **Etichete** aplicate a tot ce se clasează aici.

Și un lucru separat, mai blând: **Metadate așteptate aici**. Aceasta este o *listă de
verificare, nu o regulă*. Încărcările nu sunt refuzate niciodată pentru lipsa lor; documentele
cărora le lipsesc sunt doar marcate **Incomplet**, ca să le puteți găsi mai târziu. Acesta este
mecanismul pentru „fiecare autorizație ar trebui să-și noteze expirarea" fără să blocați pe
cineva la ora 23:00 care vrea doar să salveze scanarea.

## Pasul 2 — Hotărâți-vă tipurile de documente

**Configurare → Tipuri de documente.** Un document are un *tip* — raport topografic,
autorizație, raport de tură, buletin, corespondență, ce clasează clubul — și **fiecare tip
decide ce detalii i se cer documentelor lui**.

Asta face arhiva organizabilă în felul în care clasează efectiv clubul, nu în felul pe care l-am
ghicit noi.

## Pasul 3 — Introduceți fișierele

Trei drumuri, în funcție de volum:

### Câteva fișiere
Trageți-le pe un dulap, sau pe înregistrarea de care aparțin (o peșteră, o tură, un element).

### Un lot
**Încărcări.** Lăsați fișiere sau alegeți un dosar. Înainte de a începe, pagina vă spune cel mai
mare fișier acceptat, spațiul rămas pentru dumneavoastră și ce tipuri sunt acceptate sau
refuzate.

Puteți:
- **numi lotul** — tot ce s-a încărcat împreună se regăsește după acel nume,
- **eticheta tot** cu o etichetă (creată dacă nu există încă),
- **desface arhivele ZIP pe server, păstrându-le dosarele** — dosarele devin dulapuri.

Dacă un fișier cu exact acest conținut este deja stocat, sunteți întrebat dacă să se păstreze și
această copie sau să se sară peste. Rezumatul spune *„n din m stocate · k sărite · j eșuate"*,
cu un motiv pe rând — deja stocat, mai mare decât se acceptă, tip neacceptat, spațiu insuficient,
gol, calea dosarului nu poate fi clasată, nu aveți voie să clasați în acel dulap, ilizibil.

Rândurile eșuate pot fi reîncercate dintr-o singură apăsare.

### Un dosar la care serverul ajunge deja
**Încărcări → Importă dintr-un dosar de pe server**, dacă instalarea este configurată pentru
asta. Copiază fișierele fără să le trimită prin rețea — răspunsul potrivit pentru un disc băgat
în server. Dosarele devin dulapuri.

## Pasul 4 — Lăsați textul să fie citit

Textul unui document încărcat este citit în fundal. Acceptate: **PDF, Word, Excel, PowerPoint,
LibreOffice, Rich Text, text simplu, Markdown și CSV** — inclusiv Word și PowerPoint dinainte de
2007, și fișiere scrise în paginile de cod central-europene mai vechi de care sunt pline arhivele
românești.

Pagina unui document arată cât a avansat citirea:

| Stare | Înseamnă |
|---|---|
| *Se citește textul…* | În curs |
| *Textul a fost citit* | Gata — este căutabil |
| *Acest document nu are strat de text* | Sunt poze cu pagini. Nu există cuvinte de citit în el |
| *Acest fel de fișier nu conține text* | de ex. o imagine sau o înregistrare |
| *Textul nu a putut fi citit* / *nu poate citi încă acest format* | Exact ce spune |

> **Nimic nu recunoaște text dintr-o fotografie.** Paginile scanate și PDF-urile doar-imagine nu
> au text de căutat, iar pagina spune asta în loc să vă lase să așteptați cuvinte care nu vin
> niciodată. Dacă arhiva dumneavoastră este în mare parte scanări, planificați tastarea celor
> importante.

> **Ați clasat arhiva înainte ca toate acestea să funcționeze?** Este ordinea normală a
> lucrurilor. Un administrator rulează o
> [operațiune de întreținere](../admin/maintenance.md) și tot restanțele devin lizibile și
> căutabile fără să reîncărcați nimic.

Un document **protejat cu parolă** este raportat ca blocat, nu citit ca prostii.

**Limba** este dedusă din textul propriu al documentului și lăsată *nestabilită* în loc de
ghicită, când textul nu spune limpede. Oricine poate edita documentul o poate corecta, ceea ce
îl reindexează pe loc. Cuvintele sunt lematizate în limba documentului — română și engleză din
start.

## Pasul 5 — Găsiți ce ați clasat

Aceeași casetă de căutare care găsește peșteri și ture găsește **documente după ce scrie în
ele**, citând propoziția care s-a potrivit.

- Căutarea este **insensibilă la diacritice în ambele sensuri**: `pestera` găsește `peșteră`,
  iar rezultatul este citat înapoi scris așa cum l-a scris autorul.
- Un rezultat deschide pagina documentului **la pagina pe care s-a găsit expresia**.
- Găsiți doar documente pe care aveți voie să le citiți.
- **Versiunile înlocuite** sunt căutabile doar de cei care le-ar fi putut înlocui — un paragraf
  scos într-o versiune nouă nu rămâne găsibil în cea veche.

## Pasul 6 — Citiți fără să descărcați

Un document are pagina lui: ce este, care versiune este curentă, unde este clasat, cât a avansat
citirea, și documentul însuși:

- **PDF-uri** pagină cu pagină, ca imagini desenate de server,
- **fotografii**,
- **text simplu și Markdown**,
- **audio și video** redate în browser.

Nimic nu este transcodat și nicio pagină scanată nu este inventată. Acolo unde un format nu poate
fi afișat, codecul nu este unul pe care browserul îl redă, sau fișierul nu conține text, pagina
spune **care dintre acestea este** și oferă descărcarea.

Funcționează pe telefon. Și cineva care are voie să citească raportul unei peșteri protejate îl
poate citi acolo fără să poată vreodată descărca fișierul.

## Pasul 7 — Discutați acolo unde stă documentul

Fiecare pagină de document poartă o **discuție**: cine recunoaște peștera dintr-o fotografie
neetichetată din 1987, care topografie a înlocuit-o pe care.

- Răspunsurile merg **un singur nivel în adâncime**, ca un fir să rămână lizibil.
- Cine a scris o observație o poate corecta; el sau un administrator o poate retrage.
- Oricine poate citi documentul poate participa — și **nimeni altcineva nu poate măcar afla că
  discuția există**, pentru că o observație este accesibilă doar prin documentul pe care stă.
- Notificările despre răspunsuri și despre comentarii la propriile încărcări sunt două comutatoare
  separate, așa că oprirea traficului de pe un document aglomerat nu vă costă niciodată răspunsuri
  care vă erau adresate.

## Întreținere

- **Neclasate** — tot ce ați adăugat și nu este încă într-un dulap. Clasarea este o decizie de
  mai târziu, iar aici stă restanța.
- **Versiuni** — un document poate fi înlocuit cu o versiune nouă; cele vechi se păstrează.
- **Clasare în masă** — selectați documente și *Clasează și în…* (adaugă un dulap) sau *Mută
  în…* (le clasează în dulapul nou și le scoate din acesta).
- **Cine a citit** — proprietarul unui document, sau cineva cu dreptul asupra istoricului de
  modificări, poate vedea cine a luat o copie. A avea voie să citiți un document **nu** face ca
  cititorii lui să vă aparțină. Propriul istoric de lectură este sub contul dumneavoastră, iar
  nimeni nu poate cere istoricul altcuiva numindu-l.

---

Urmează: [Partajarea muncii dumneavoastră](share-your-work.md) ·
Referință: [Documente și dulapuri](../features/documents-and-cabinets.md)
