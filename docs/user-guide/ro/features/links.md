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
elemente aflate în interiorul altor elemente.

---

## Ce poate fi legat

| Fel | |
|---|---|
| Element | Inclusiv peșteri, intrări și poligonații |
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
înregistrare și creând punctul odată cu legătura. Îi alegeți numele, altitudinea și audiența
acolo și atunci.

## Relația

Formularea vine dintr-o listă pe care instalarea o deține și o poate extinde:

**Același obiect ca · Înrudit cu · Conține / Conținut în · Documentat de / Documentează ·
Sursă pentru / Derivat din · Adiacent cu · Original al / Duplicat al · Necesită clarificare**

plus relațiile specifice turelor care alimentează secțiunea *Ce a făcut tura* (*A lucrat în, A
vizat, A vizitat, A cartat, A descoperit, A săpat la, A fotografiat, A căutat fără să găsească,
A lăsat continuare, Continuă din*), și *Text al / Are text*.

**O relație care se citește într-un sens se citește corect din ambele capete.** O singură
legătură spune „Conține" pe o pagină și „Conținut în" pe cealaltă. Când o relație este
direcțională, trebuie să marcați **din care element se citește** — *elementul principal* — iar
fereastra previzualizează ambele citiri înainte să salvați.

Un administrator poate adăuga relații proprii. Cele care vin cu produsul își păstrează codul și
felul în care se citesc și nu pot fi șterse. O relație pe care legăturile o înregistrează deja nu
poate fi redefinită sau ștearsă. Vedeți
[Vocabulare](../admin/vocabularies.md#configurare--relații-între-elemente).

## Indicarea unei părți, nu a întregului

Un capăt de legătură poate fi o **ancoră** într-o parte a unui lucru:

| Ancoră | |
|---|---|
| **Întregul** | Implicit |
| **O pagină** / **un interval de pagini** | Dintr-un document |
| **Un pasaj de text** | Selectat în document |
| **O regiune dintr-o imagine** | |
| **Un moment** / **o secvență dintr-o înregistrare** | În secunde |
| **O stație topografică** / **un șir de stații** / **o topografie** / **un șir de topografii** | Dintr-un model 3D |
| **Un punct din model** | |
| **Un punct de traseu** / **un șir de puncte** | Dintr-un fișier de geodate |

Unele dintre acestea au nevoie de un vizualizator care le poate selecta, iar fereastra o spune
limpede: *„Indicarea acestui fel de parte sosește odată cu vizualizatorul care o poate selecta."*

### Ancorele de text supraviețuiesc editării

Pentru un pasaj de text: deschideți documentul, trageți peste pasaj. **Ceea ce se stochează este
pasajul însuși**, nu o poziție în caractere, așa că legătura îl regăsește și după ce documentul se
schimbă.

Dacă nu mai poate fi găsit exact, legătura spune care:

- **Măsurat față de o versiune mai veche, regăsit în cea curentă**,
- **Măsurat față de o versiune mai veche — arătat pe întreaga resursă**,
- **Această parte nu mai există în resursă.**

---

## Citirea unei legături

Fiecare înregistrare arată **Elemente legate (n)**. Fiecare rând numește membrii și relația.

- O legătură cu un singur membru vizibil pentru dumneavoastră este marcată **Incompletă** — *„O
  legătură spune ceva abia când unește două sau mai multe."*
- Un membru pe care nu aveți voie să-l citiți apare ca **Element restricționat**: *„Această
  legătură numește ceva ce nu aveți voie să citiți."*
- Dacă nimic dintr-o legătură nu vă este vizibil, o spune.

> **O legătură nu este niciodată lucrul care divulgă o peșteră protejată.** Arată fiecărui
> cititor doar capetele pe care are voie să le vadă.

Fiecare legătură are **o adresă proprie** — un cod scurt de lipit într-un mesaj sau într-un
raport, care deschide pagina legăturii.

## Crearea uneia

**Leagă…** pe orice înregistrare. Alegeți felul elementului, găsiți-l, alegeți relația, adăugați
opțional o **notă** („De ce aparțin împreună") și o **descriere**, și spuneți din ce element se
citește relația, dacă este direcțională.

Pe pagina legăturii puteți adăuga membri, îi puteți scoate, reordona, schimba care este elementul
principal, și puteți edita relația și descrierea.

**Ștergerea unei legături nu șterge nimic din ce leagă**, iar confirmarea o spune.

---

Înrudite: [Documente și dulapuri](documents-and-cabinets.md#text-adnotat-cu-legături) ·
[Fenomene de suprafață](surface-features.md) · [Vocabulare](../admin/vocabularies.md)
