# Înregistrări de pe telefon și ture care au nevoie de o privire

🇬🇧 [English](../../features/phone-recordings.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Telefon și sincronizare offline](mobile-sync.md) ·
[Persoane, speologi și cluburi](people-and-clubs.md#utilizatori-de-telefon) ·
[Ture](trips.md) ·
[Setările sincronizării cu telefonul](../admin/messaging.md#sincronizare-cu-telefonul)

---

Partea de server a acestui lucru este construită; aplicația de telefon SpeleoLoc, care trimite
înregistrările, este un proiect separat, iar o versiune care se sincronizează cu SilexGIS este în lucru și nu e încă publicată pentru uz
general (vezi [Telefon și sincronizare offline](mobile-sync.md)). Până atunci înregistrările
ajung aici doar de la versiunile aflate în testare.

Un telefon cu SpeleoLoc poate **înregistra o tură**: în ce peșteră, când a început și s-a încheiat și
câte o scanare la fiecare loc marcat pe lângă care a trecut. Înregistrarea nu este încă raportul de
tură al clubului. Pagina de față este despre cele două locuri unde o întâlnești: **pagina peșterii**,
care listează înregistrările făcute în acea peșteră, și **pagina turei**, care îți spune când un raport
făcut dintr-o înregistrare are nevoie să-l verifice cineva.

---

## Înregistrările pe pagina peșterii

Deschide o peșteră. Dacă telefoanele au înregistrat ture în ea, sub turele peșterii apare un card
**Înregistrări de pe telefon**. Fiecare rând arată numele înregistrării, când a început, câte scanări
are și în ce stare se află:

| Stare | Ce înseamnă |
|---|---|
| **În așteptare** | Nu s-a făcut încă niciun raport de tură din ea. O înregistrare **încă deschisă pe telefon** așteaptă ca telefonul s-o închidă. Una a cărei transformare automată a fost oprită (vezi mai jos) te așteaptă pe tine. |
| **Transformată în tură** | Există un raport de tură. *Deschide tura* te duce la el. |
| **Necesită atenție** | Există un raport de tură, dar transformarea nu a putut stabili tot ce e în el. *Deschide tura* arată ce. |

Vezi o înregistrare doar dacă ai voie s-o citești și doar dacă ai voie să afli despre peșteră și unde
se află. Dacă peșterea are poziția protejată față de tine, cardul nu apare deloc.

### Cum devine o înregistrare tură

Când o înregistrare a fost închisă pe telefon și trimisă, instalația face singură un raport de tură din
ea, **atunci când nimic nu e în dubiu**: locul fiecărei scanări dă exact o stație de ridicare, iar
fiecare persoană care a scanat este asociată unui speolog (vezi
[Utilizatori de telefon](people-and-clubs.md#utilizatori-de-telefon)).

Când ceva e în dubiu, există două rezultate, alese în
[setările sincronizării cu telefonul](../admin/messaging.md#sincronizare-cu-telefonul) ale administratorului:

- **Transformă turele cu probleme pornit** (implicit): tura se face din ce a putut fi stabilit și este
  **semnalată**, cu motivele scrise pe ea. Vezi *Ture care au nevoie de o privire* mai jos.
- **Oprit**: nu se face nicio tură; înregistrarea **așteaptă** pe pagina peșterii o persoană.

### Transformă în tură, manual

La o înregistrare în așteptare pe care ai voie s-o modifici, **Transformă în tură** deschide același ecran
de verificare ca importul unui export SpeleoLoc, dar citind această înregistrare în loc de un fișier.
Înregistrarea este numită sus; nu ai nimic de ales dintr-o arhivă.

1. Spune unde merg pozițiile: **o tură existentă** sau **una nouă**.
2. Alege **ridicarea topografică** (prin peștera ei) ale cărei stații sunt pozițiile.
3. Stabilește fiecare scanare: stația este propusă după adâncimea locului scanat, iar tu o confirmi sau
   alegi alta. O scanare pe care nu o leagă nimeni de un speolog are nevoie de utilizatorul de telefon
   asociat, în panoul din dreapta.
4. **Înregistrează** scanările dorite.

Nu se scrie nimic până la ultimul buton, iar o înregistrare devine un singur raport: odată ce are unul,
butonul dispare. Dacă încărcările contului tău sunt ținute pentru verificare, transformarea așteaptă un
verificator. Verificarea **nu se salvează între vizite** pe această cale: dacă pleci de pe pagină, o iei
de la capăt.

---

## Ture care au nevoie de o privire

O tură făcută dintr-o înregistrare în care ceva a fost în dubiu arată sus, pe pagina ei, o notificare.
Aceasta spune, pe înțeles și cu câte un număr, ce nu a putut fi stabilit:

- scanări **fără niciun loc marcat** sau la unul pe care această instalație nu-l mai ține;
- scanări al căror loc **nu dă o singură stație de ridicare**;
- scanări făcute de un **utilizator de telefon care nu este încă asociat unui speolog**;
- scanări **datate în viitor**, ceea ce nicio poziție nu poate fi;
- modificări făcute înregistrării **pe un telefon după** ce a fost făcută tura. Tura nu este rescrisă, ci
  semnalată.

Notificarea o vede oricine poate citi tura. Motivele spun câte scanări, niciodată o stație sau un loc.

Îndreaptă ce poți cu uneltele care există deja — asociază utilizatorul de telefon, corectează
ridicarea — apoi apasă **Marchează ca rezolvată**. Butonul este pentru cei care pot modifica tura. Dacă
altcineva a schimbat semnalul cât îl citeai, ți se cere să-l citești din nou mai întâi.

După ce este rezolvată, notificarea rămâne pe pagină ca o notă discretă, cu data, iar **motivele rămân
vizibile**, așa că o tură care a avut odată nevoie de verificare se citește în continuare așa. Dacă
telefonul modifică din nou înregistrarea, aceeași notificare este ridicată din nou.

---

Înrudite: [Telefon și sincronizare offline](mobile-sync.md) ·
[Persoane, speologi și cluburi](people-and-clubs.md) · [Ture](trips.md) ·
[Notificări](notifications.md)
