# Documente și dulapuri

🇬🇧 [English](../../features/documents-and-cabinets.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux:
[Clasarea arhivei clubului](../workflows/file-the-archive.md)

---

Arhiva clubului: rapoarte, autorizații, buletine, corespondență, scanări, foi de calcul, audio,
video. Clasate într-un arbore de **dulapuri**, căutabile după ce scrie în ele, lizibile fără
descărcare și comentabile pe loc.

---

## Dulapuri

**Bibliotecă → Dulapuri.** Un arbore de clasare — *Arhiva clubului / Buletine / 1987*.

Un dulap este și **unitatea pe care se acordă permisiuni**. O singură regulă poate da unui comitet
toată arhiva, în loc de o regulă per document. Domeniul: *un dulap și tot ce este clasat sub el*.

Consecințe de spus limpede:

- **Un document poate sta în mai multe dulapuri deodată.** Nu există întrebarea
  mută-sau-copiază; nimic nu rămâne orfan pentru că aparține în două locuri. Fiecare dulap pe
  care îl numește o regulă ajunge la el.
- **Clasarea mută cine poate citi.** Punerea unui document pe un raft, sau scoaterea lui,
  schimbă accesul. Aplicația o spune chiar lângă control.
- Un dulap către care încă arată reguli nu poate fi șters până nu sunt scoase acele reguli. Nici
  unul care încă ține dulapuri sau documente.

### Ce aterizează aici

Valori implicite aplicate a tot ce se încarcă într-un dulap:

- **Tip de document** — sau *la fel ca dulapul de deasupra*,
- **Cine poate citi**,
- **Etichete** aplicate a tot ce se clasează aici.

### Metadate așteptate aici

O **listă de verificare, nu o regulă.** Încărcările nu sunt refuzate niciodată pentru lipsa lor.
Documentele cărora le lipsesc sunt marcate **Incomplet** și spun ce *încă lipsește*, ca restanța
să fie găsibilă.

### Neclasate

Tot ce ai adăugat și nu este încă într-un dulap. Clasarea este o decizie de mai târziu, iar aici
stă grămada netriată.

### Clasare în masă

Selectează documente, apoi **Depune și în…** (adaugă un dulap, fără să mute nimic) sau **Mută
în…** (le clasează în dulapul nou și le scoate din acesta). Rezultatele parțiale sunt raportate:
*„n depuse; m nu au putut fi."*

---

## Tipuri de documente

**Configurare → Tipuri de documente.** Un document are un tip — raport topografic, autorizație,
raport de tură, buletin… — și **fiecare tip decide ce detalii i se cer documentelor lui**. Asta
permite ca o arhivă să fie organizată în felul în care clasează efectiv un club.

Vezi [Vocabulare](../admin/vocabularies.md#configurare--tipuri-de-documente).

---

## Pagina unui document

Ce este, care versiune este curentă, unde este clasat, cât a avansat citirea textului, și
documentul însuși.

| Câmp | |
|---|---|
| **Titlu** · **Tip de document** · **Autor** | |
| **Cine poate citi** | Și un **grup de speologie** de care poate fi legat |
| **Clasat în** | Dulapurile în care stă |
| **Format** · **Mărime** · **Versiune** · **Ultima modificare** | |
| **Limbă** | Română, engleză sau *Necunoscută* |
| **Proprietăți de document** | Ce cere tipul lui |

Documentele poartă **versiuni**: încarci una nouă și cele vechi se păstrează.

---

## Ștergerea și restaurarea unui document

Pe pagina unui document, **Șterge** (afișat oricui poate edita documentul) cere confirmare.
Confirmarea spune ce urmează să se întâmple, în termenii acestei instalații: nimic nu este
eliminat, documentul dispare din toate listele, căutările, galeriile și legăturile și poate fi
restaurat din **Documente șterse**.

- **Cine poate șterge și restaura.** Exact cei care pot edita documentul respectiv. Oricine altcineva
  l-ar fi putut citi primește un refuz; cine nu l-ar fi putut citi află că nu există, ca pentru
  orice document.
- **Ce este un document șters.** Absent peste tot: dulapuri, căsuța documentelor neclasate,
  rezultatele căutării (inclusiv în textul lui), galerii de fotografii și albume, legăturile care îl
  numesc și propria adresă, care răspunde *negăsit*.
- **Documente șterse** se ajunge din pagina **Dulapuri** sau din adresa unui document șters, care
  oferă aceeași legătură oricui poate edita documente. Listează documentele pe care le poți
  restaura, cele mai recente primele, cu cine le-a șters și, acolo unde instalația le elimină după
  un timp, cât a mai rămas.
- **Restaurează** cere confirmare și aduce documentul înapoi **întreg**: toate versiunile,
  observațiile, atașamentele, legăturile și locurile în dosare pe care le avea, și îl deschide.
  Nimeni nu este anunțat.
- **Audit.** Ștergerea și restaurarea sunt înregistrate în jurnalul de audit ca modificări ale
  documentului, cu cine a făcut-o și când.
- **Nimic nu se elimină definitiv decât dacă cel care administrează instalația hotărăște asta.**
  Implicit documentele șterse se păstrează, rămânând restaurabile, fără limită. Un operator poate
  seta un număr de zile după care un document șters și fișierele lui sunt eliminate definitiv —
  vezi `SILEXGIS__DocumentRetention__RetentionDays` în
  [ghidul de instalare](../../../INSTALL.md#configuration-reference). Fotografiile șterse apar în
  aceeași listă.

---

## Citirea fără descărcare

Vizualizatorul arată:

- **PDF-uri** pagină cu pagină, ca imagini desenate de server, cu apropiere și navigare între
  pagini,
- **fotografii**,
- **text simplu și Markdown** (fișierele mari arată prima parte și oferă descărcarea),
- **audio și video** redate în browser.

**Nimic nu este transcodat și nicio pagină scanată nu este inventată.** Acolo unde un format nu
poate fi afișat, codecul nu este unul pe care browserul îl redă, serviciul de paginare este
indisponibil, sau fișierul nu conține text deloc, pagina spune **care dintre acestea este** și
oferă descărcarea. Nu rămâi niciodată să te uiți la un panou gol întrebându-te.

Funcționează pe telefon. Și cineva care are voie să citească raportul unei peșteri protejate îl
poate citi acolo **fără să poată vreodată descărca fișierul**.

Poți **selecta text pe o pagină de PDF** ca să-l copiezi sau ca să îndrepți o
[legătură](links.md) spre acel pasaj.

---

## Textul dinăuntru

Textul unui document încărcat este citit în fundal. Acceptate: **PDF, Word, Excel, PowerPoint,
LibreOffice, Rich Text, text simplu, Markdown, CSV** — inclusiv Word și PowerPoint dinainte de
2007, și fișiere în paginile de cod central-europene mai vechi de care sunt pline arhivele
românești.

Starea **Text** este una dintre:

| Stare | Înseamnă |
|---|---|
| *Se citește textul…* | În curs |
| *Textul a fost citit* | Căutabil |
| *Documentul nu are strat de text* | Poze cu pagini — nu există cuvinte în el |
| *Acest fel de fișier nu conține text de citit* | de ex. o imagine sau o înregistrare |
| *Textul nu a putut fi citit* | Eșuat |
| *Nimic de aici nu știe încă să citească textul acestui format* | Neacceptat |

**Nimic nu recunoaște text dintr-o fotografie.** Paginile scanate și PDF-urile doar-imagine nu au
text de căutat, iar pagina o spune în loc să te lase să aștepți.

> Dacă un lot întreg de documente a fost încărcat înainte ca cititorul de text sau convertorul de
> documente de birou să funcționeze, un administrator poate trece din nou peste ele — vezi
> [Operațiuni de întreținere](../admin/maintenance.md).

Un document **protejat cu parolă** este raportat ca blocat, nu citit ca prostii.

**Limba** este dedusă din textul propriu al documentului și lăsată nestabilită în loc de ghicită
când textul nu spune limpede. Oricine poate edita documentul o poate corecta, ceea ce îl
reindexează pe loc.

## Căutarea în interiorul documentelor

Aceeași casetă de căutare care găsește peșteri și ture găsește documente **după ce scrie în ele**,
citând propoziția care s-a potrivit.

- **Insensibilă la diacritice în ambele sensuri**: `pestera` găsește `peșteră`, iar rezultatul
  este citat înapoi scris așa cum l-a scris autorul.
- Cuvintele sunt **lematizate în limba proprie a documentului** — română și engleză din start, și
  orice limbă pentru care PostgreSQL are un lematizator, adăugând un singur rând.
- Un rezultat **deschide pagina documentului la pagina pe care s-a găsit expresia** (sau foaia,
  sau diapozitivul).
- Găsești doar documente pe care ai voie să le citești.
- **Versiunile înlocuite** sunt căutabile doar de cei care le-ar fi putut înlocui — un paragraf
  scos într-o versiune nouă nu rămâne găsibil în cea veche.

Vezi [Căutare și filtre](search-and-filters.md).

---

## Discuție

Fiecare pagină de document poartă o discuție.

- **Răspunsurile merg un singur nivel în adâncime**, ca firul să rămână lizibil.
- Cine a scris o observație o poate corecta; el sau un administrator o poate retrage.
- Oricine poate citi documentul poate participa — și **nimeni altcineva nu poate măcar afla că
  discuția există**, pentru că o observație este accesibilă doar prin documentul pe care stă.
- Observațiile sunt **cuvinte simple**, afișate ca acele cuvinte care au fost tastate.

Două comutatoare separate de notificare: *Cineva răspunde la un comentariu scris de mine* și
*Cineva comentează la ceva ce îmi aparține*. Așa că oprirea traficului de pe un document aglomerat nu
te costă niciodată răspunsurile care îți erau adresate. Nu ești niciodată anunțat despre propria
observație, iar dacă ceva este și răspuns pentru tine și comentariu la propria încărcare,
afli o singură dată.

**Niciun mesaj nu repetă vreodată ce s-a spus** — numește documentul și trimite la el.

---

## Text adnotat cu legături

Proză scrisă **în aplicație**, peste care legăturile marchează pasaje. Urmărirea unui pasaj
evidențiat **mută vederile deschise** — harta, scena 3D, vizualizatorul topografic, vederea de
imagine — către ce este acel pasaj.

Acesta este mecanismul pentru o relatare scrisă a unei explorări care conduce harta în timp ce o
citești: *„am urmat cursul de apă până la al treilea aven"*, și harta merge acolo.

### Este un document obișnuit

Nu există o lume nouă aici. Un text adnotat cu legături **este un [document](#pagina-unui-document)
ca oricare altul** — cine poate citi, de ce club aparține, unde este clasat, istoricul lui,
discuția lui, dacă este căutabil. Toate acestea rămân neschimbate.

Diferă doar **formatul**: blocuri de text simplu al căror șir de caractere este *definit*, nu
extras, așa că un pasaj selectat în browser și un pasaj stocat într-o legătură sunt măsurate
față de același șir, **la caracter**.

> **Nimic din ce tastează cineva nu ajunge vreodată pe pagină ca marcaj.** Textul unui bloc
> devine text; accentuarea lui devine elemente alese dintr-un set închis. De aceea formatul stocat
> este blocuri de text simplu și nu un fragment de HTML — nu există nimic în el care să poată fi
> tentat să interpreteze ce a scris cineva.

### Unde le găsești și le scrii

Pe panoul unei înregistrări, secțiunea **Text** listează textele adnotate scrise despre ea. Acolo
unde o înregistrare nu are încă nimic scris despre ea, secțiunea oferă **Scrie unul**, denumit
după înregistrare.

Care dintre documentele legate ale unei peșteri este text adnotat și care este o scanare a unui
raport din 1974 este **declarația serverului**, nu o ghicitoare — așa că atașamentele obișnuite
nu îți arată niciodată o notificare de refuz.

### Citirea

| Control | |
|---|---|
| **Unde duc legăturile** | Oprești individual harta, scena 3D, vizualizatorul topografic sau vederea de imagine, ca s-o lași pe una nemișcată |
| **Deschide într-o fereastră separată** | Ca să stea lângă harta pe care o conduce |
| **Arată** | Mută vederile deschise către ținta legăturii, fără să părăsești textul |
| **Arată în…** | Inclusiv *altă fereastră* |
| **Deschide** | Merge la lucrul însuși |

Dacă nu este nimic deschis de mutat: *„Nu este deschisă nicio hartă, scenă sau vizualizator, deci
urmărirea unui pasaj nu are ce muta. Deschide una sau desprinde textul lângă ea."*

### Scrierea legăturilor

**Editează legăturile**, apoi selectează orice parte a textului și leag-o de o peșteră, un
document, o tură sau orice altceva. Ștergerea unei legături oprește evidențierea pasajului;
**lucrurile pe care le lega nu sunt afectate**.

**Nu se leagă singur de nimic.** Atașarea textului la scanarea a cărei lectură este, sau la
peștera pe care o descrie, este o [legătură](links.md) obișnuită, scrisă în modul obișnuit.

### Revizii și pasaje care se mută

Înlocuirea corpului scrie **o revizie nouă** și remăsoară fiecare legătură peste ea.

- Pasajele care supraviețuiesc rămân **exacte**.
- Pasajele dispărute se citesc ca **degradate**, iar o fișă spune **S-ar putea să se fi mutat** — *„Aceste
  cuvinte nu se află unde le-a înregistrat legătura, deci evidențierea poate fi pe alt
  pasaj."*

Ești anunțat când o evidențiere este nesigură, în loc să ți se arate una sigură în locul
greșit.

### Și peste un PDF

Același cititor funcționează peste **stratul de text al unui PDF**, nu doar peste texte scrise
aici. Așa că un pasaj dintr-un raport scanat care poartă text real poate fi indicat exact ca un
pasaj dintr-o relatare tastată — vezi
[Legături](links.md#ancorele-de-text-supraviețuiesc-editării).

---

## Cine a citit un document

**Proprietarul** unui document, sau cineva cu dreptul asupra istoricului de audit, poate vedea
cine a luat o copie din el.

Faptul că ai voie să *citești* un document **nu** face ca cititorii lui să-ți aparțină — un
membru de club ar putea altfel afla care alți membri s-au uitat la o anumită topografie, ceea ce
este un fapt despre ei.

Propriul istoric de lectură este în contul tău. **Nimeni nu îl poate obține pe al
altcuiva numindu-l.** Mai multe la [Istoric și audit](history-and-audit.md#istoricul-de-acces--cine-a-citit-un-document).

---

Flux: [Clasarea arhivei clubului](../workflows/file-the-archive.md) ·
Înrudite: [Încărcări](uploads.md) · [Legături](links.md) ·
[Permisiuni](../admin/permissions.md)
