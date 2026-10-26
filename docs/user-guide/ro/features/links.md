# Legături între înregistrări

🇬🇧 [English](../../features/links.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Noțiuni de bază](../core-concepts.md#4-legăturile-spun-cum-se-raportează-două-lucruri-unul-la-altul)

---

O peșteră și raportul care o descrie. O tură și fotografiile pe care le-a produs. Două
înregistrări care s-au dovedit a fi aceeași gaură. O legătură numește **două sau mai multe
lucruri de orice fel** și spune **cum se raportează unul la altul**.

Acesta este mecanismul general.
[Conținerea](surface-features.md#ierarhie-părinți-și-copii) este celălalt, și se referă anume la
obiecte aflate în interiorul altor obiecte.

---

## Ce poate fi legat

| Fel | |
|---|---|
| Obiect | Inclusiv peșteri, intrări și drumuiri |
| Document | |
| Jurnal de tură | |
| Speolog | |
| Grup de speologie | |
| Vedere salvată | |
| Model topo 3D | |
| Fișier de geodate | |
| Dulap | |
| Tabără | |

O legătură poate și **marca un punct nou pe hartă** — numind un loc care nu avea încă
înregistrare și creând punctul odată cu legătura. Îi alegi numele, altitudinea și audiența
acolo și atunci.

## Relația

Formularea vine dintr-o listă pe care instalarea o deține și o poate extinde:

**Același obiect cu · În legătură cu · Conține / Conținut în · Documentat de / Documentează ·
Sursă pentru / Derivat din · Adiacent cu · Original al / Duplicat al · Necesită clarificare**

plus relațiile specifice turelor care alimentează secțiunea *Ce a făcut tura* (*A lucrat în, A
vizat, A vizitat, A cartat, A descoperit, A săpat la, A fotografiat, A căutat, fără rezultat,
A lăsat continuare, Continuă tura*), și *Textul lui / Are textul*.

**O relație care se citește într-un sens se citește corect din ambele capete.** O singură
legătură spune „Conține" pe o pagină și „Conținut în" pe cealaltă. Când o relație este
direcțională, trebuie să marchezi **din care element se citește** — *membrul principal* — iar
fereastra previzualizează ambele citiri înainte să salvezi.

Un administrator poate adăuga relații proprii. Cele care vin cu produsul își păstrează codul și
felul în care se citesc și nu pot fi șterse. O relație pe care legăturile o înregistrează deja nu
poate fi redefinită sau ștearsă. Vezi
[Vocabulare](../admin/vocabularies.md#configurare--relații-între-obiecte).

## Indicarea unei părți, nu a întregului

Un capăt de legătură poate fi o **ancoră** într-o parte a unui lucru:

| Ancoră | |
|---|---|
| **Întregul** | Implicit |
| **O pagină** / **un interval de pagini** | Dintr-un document |
| **Un fragment de text** | Selectat în document |
| **O regiune dintr-o imagine** | Desenată chiar pe imagine — un dreptunghi, un cerc, un contur sau un punct. Când citești una, toate regiunile deja desenate pe imagine sunt pictate, cea pe care ai urmat-o mai apăsat decât restul. Atât desenarea, cât și alegerea merg și [de la tastatură](#regiunile-unei-imagini-de-la-tastatură) |
| **Un moment din înregistrare** / **un interval din înregistrare** | În secunde |
| **O stație topografică** / **un șir de stații topografice** / **o topografie** / **un șir de topografii** | Dintr-un model 3D |
| **Un punct din model** | |
| **Un punct de traseu** / **un șir de puncte** | Dintr-un fișier de geodate |
| **Un moment al turei** | Al unei ture [urmărite de la suprafață](live-tracking.md#echipa-pe-topografie) — felul în care o fotografie este pusă pe un moment al reluării ei. O legătură care spune că o fotografie *documentează* un moment al turei cere dreptul de a **edita** acea tură, pentru că hotărăște ce arată reluarea turei |

Unele dintre acestea au nevoie de un vizualizator care le poate selecta, iar fereastra o spune
limpede: *„Indicarea acestui tip de fragment vine odată cu vizualizatorul care îl poate selecta."*

### Regiunile unei imagini de la tastatură

O regiune poate fi desenată și aleasă fără mouse.

**Desenarea.** Apasă **Tab** până când imaginea primește focusul; în mijlocul ei apare un
cursor.

| Tastă | Ce face |
|---|---|
| **Săgețile** | Mută cursorul cu un pas mic pe imagine |
| **Shift** + săgeată | Un pas mai mare |
| **Enter** sau **Space** | Apasă aici: fixează un **punct**; prima și a doua apăsare dau cele două colțuri opuse ale unui **dreptunghi**, sau centrul unui **cerc** și apoi un loc pe marginea lui; la un **contur**, fiecare apăsare adaugă un colț |
| **Enter** din nou pe ultimul colț | Închide conturul |
| **Backspace** | Renunță la ultimul colț al unui contur |
| **Escape** | Renunță la forma începută. Dacă nu e nimic început, închide fereastra în care se află imaginea |

Rândul de ajutor de sub alegerea formei spune același lucru cu cuvintele ecranului. O regiune
desenată de la tastatură este exact regiunea pe care ar fi desenat-o aceeași tragere cu mouse-ul.

**Alegerea.** Când citești o imagine, fiecare regiune deja desenată pe ea este în ordinea **Tab**,
numită după relația ei (sau *Regiunea n* când nu are una). Regiunea cu focus este desenată mai
apăsat; **Enter** sau **Space** o deschide la fel ca un clic. O regiune desenată în interiorul
alteia vine după ea, așa că cea din interior poate fi mereu atinsă.

### Ancorele de text supraviețuiesc editării

Pentru un pasaj de text: deschide documentul, trage peste pasaj. **Ceea ce se stochează este
pasajul însuși**, nu o poziție în caractere, așa că legătura îl regăsește și după ce documentul se
schimbă.

Dacă nu mai poate fi găsit exact, legătura spune care:

- **Măsurat pe o versiune mai veche, regăsit în cea curentă**,
- **Măsurat pe o versiune mai veche — arătat pe întreaga resursă**,
- **Această parte nu mai există în resursă.**

---

## Citirea unei legături

Fiecare înregistrare arată **Elemente legate (n)**. Fiecare rând numește membrii și relația.

- O legătură cu un singur membru vizibil pentru tine este marcată **Incompletă** — *„O
  legătură spune ceva abia când unește două sau mai multe."*
- Un membru pe care nu ai voie să-l citești apare ca **Element restricționat**: *„Legătura
  numește ceva ce nu ai voie să citești."*
- Dacă nimic dintr-o legătură nu îți este vizibil, o spune.
- Un membru care este o fotografie, o peșteră sau o persoană arată **o imagine mică** —
  fotografia, fotografia reprezentativă a peșterii, portretul persoanei. Celelalte feluri nu arată
  niciuna, pentru că o imagine a unei vederi salvate, a unui fișier de geodate sau a unui model
  topografic ar trebui să deseneze poziții.
- Urmarea unei legături către o stație topografică sau o topografie **mută vizualizarea 3D**
  acolo, în loc să reîncarce topografia, așa că păstrezi locul la care te uiți.

> **O legătură nu este niciodată lucrul care divulgă o peșteră protejată.** Arată fiecărui
> cititor doar capetele pe care are voie să le vadă.

Fiecare legătură are **o adresă proprie** — un cod scurt de lipit într-un mesaj sau într-un
raport, care deschide pagina legăturii.

## Crearea uneia

**Leagă…** pe orice înregistrare. Alege felul elementului, găsește-l — în selectorul descris la
[Căutare și filtre](search-and-filters.md#selectorul), care ajunge și la dulapuri, modele
topografice și fișiere de geodate —, alege relația, adaugă
opțional o **notă** („De ce stau împreună") și o **descriere**, și spune din ce element se
citește relația, dacă este direcțională.

Pe pagina legăturii poți adăuga membri, îi poți scoate, reordona, schimba care este membrul
principal, și poți edita relația și descrierea.

**Ștergerea unei legături nu șterge nimic din ce leagă**, iar confirmarea o spune.

---

Înrudite: [Documente și dulapuri](documents-and-cabinets.md#text-adnotat-cu-legături) ·
[Fenomene de suprafață](surface-features.md) · [Vocabulare](../admin/vocabularies.md)
