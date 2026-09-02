# Operațiuni de întreținere

🇬🇧 [English](../../admin/maintenance.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Încărcări](../features/uploads.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md)

---

O parte din muncă se face **când sosește un fișier**: i se citește textul, un document de birou
este paginat pentru citire, poziția unei fotografii este scoasă din datele aparatului.

Ceea ce ridică o întrebare pe care orice arhivă în creștere ajunge s-o pună: *dar cu tot ce a
sosit înainte ca acelea să funcționeze?*

Trei **operațiuni de recuperare** răspund. Fiecare parcurge rândurile existente ale instalării și
face munca ce nu s-a făcut la vremea ei.

---

> **Acestea nu au încă o pagină proprie.** Se declanșează prin interfața de programare a
> aplicației, nu dintr-un buton. Dacă nu vă simțiți în largul dumneavoastră făcând asta, pagina
> este totuși cea către care să vă îndreptați administratorul — spune ce operațiune rezolvă ce
> simptom.

---

## Cele trei operațiuni

### Citirea textului

Citește textul fișierelor stocate pe care **nu le-a citit nimic**, sau pe care **un cititor mai
nou ar trebui să le citească din nou**.

**Rulați-o când:**

- documente încărcate înainte să observați că există căutare în text nu apar în rezultate,
- un lot de fișiere arată *„Nu poate citi încă textul acestui format"*, iar instalarea a fost
  actualizată între timp,
- ați importat în masă o arhivă dintr-un dosar de pe server și vreți s-o faceți căutabilă.

După aceea, starea **Text** a fiecărui document ar trebui să treacă la *Textul a fost citit*.
Fișierele care chiar nu conțin text — scanări, PDF-uri doar-imagine — vor spune tot asta, pe bună
dreptate. **Nimic nu recunoaște text dintr-o fotografie**, iar o operațiune de întreținere nu
schimbă asta.

### Conversia documentelor

Face **o copie lizibilă pentru fiecare document de birou care nu are încă una** — paginile pe care
browserul le arată pentru fișiere Word, Excel, PowerPoint și OpenDocument.

**Rulați-o când:**

- ați instalat serviciul opțional de conversie *după* ce ați încărcat o arhivă,
- documentele arată la nesfârșit *„Acest document se pregătește pentru citire aici"*, sau *„Această
  instalare nu poate pagina documente de birou"*, iar asta a fost rezolvată între timp.

Aceasta este cea mai probabilă operațiune de care are nevoie un club, pentru că serviciul de
conversie este opțional și se adaugă de obicei mai târziu.

### Recuperarea pozițiilor fotografiilor

Pune **poziția înregistrată de un aparat** pe fotografiile deja stocate fără una.

**Rulați-o când:**

- fotografiile geolocalizate nu apar pe stratul *Fotografii geolocalizate* al hărții,
- ați importat o arhivă foto mare înainte ca asta să funcționeze.

Citește ce este în fișiere. Nu inventează poziții și nu scrie nimic înapoi în fișierele
dumneavoastră de imagine.

---

## Ce au în comun

- **Niciuna nu primește vreo intrare proprie.** Munca este o proprietate a rândurilor instalării,
  deci singurul lucru care variază este care operațiune rulați.
- **Se pun la coadă ca sarcini de fundal**, ca un import sau o conversie raster. Nu blochează pe
  nimeni.
- **Au nevoie de dreptul de *Rulare* pe domeniul Jobs** — vedeți
  [Permisiuni](permissions.md#acțiuni).
- **Se pot rula de mai multe ori în siguranță.** O operațiune face munca nefăcută; nu reface munca
  făcută.

## Urmărirea uneia

O sarcină pusă la coadă are o stare pe care o puteți citi — cel care a cerut-o își poate vedea
propriile sarcini, iar cine are **Citire** pe domeniul Jobs le poate vedea pe oricare.

Membrii care au cerut să fie anunțați când **încărcările lor termină de procesat** vor primi
notificarea pe măsură ce munca se așază. Vedeți [Notificări](../features/notifications.md).

---

## Nu sunt operațiuni: lucrurile care se repară singure

Prin contrast, acestea nu au nevoie de întreținere:

| | |
|---|---|
| **Limba unui document** | Detectată la citirea textului și corectabilă manual pe document oricând — ceea ce îl reindexează pe loc |
| **Statisticile calculate ale unei peșteri** | Nu se stochează niciodată. Se calculează din topografie de fiecare dată, deci încărcarea unei topografii mai bune le corectează |
| **Primele vizite și orele în subteran** | Nu se stochează niciodată. Tastarea unei ture mai vechi din arhivă *corectează* cifrele, în loc să lase un indicator învechit |
| **Continuările unei tabere** | O lectură a ce spun deja turele, deci înregistrarea unei continuări o dată o înregistrează peste tot |

Acesta este un tipar deliberat: **acolo unde o cifră se poate deduce, se deduce.** Operațiunile
există doar pentru munca ce chiar trebuie făcută o dată și stocată — citirea unui fișier,
conversia unui fișier, sau scoaterea unei coordonate dintr-unul.

---

Înrudite: [Încărcări](../features/uploads.md) ·
[Documente și dulapuri](../features/documents-and-cabinets.md) ·
[Fotografii](../features/photographs.md)
