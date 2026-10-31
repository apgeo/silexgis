# Protecția locațiilor

🇬🇧 [English](../../admin/location-protection.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Permisiuni](permissions.md)

---

Mecanismul care permite unei instalări să spună **„această peșteră există și ai voie să citești
despre ea, dar nu ai voie să știi unde este."**

Este motivul specific speologic pentru care există această aplicație în loc să se folosească un
SIG general, și merită înțeles ca lumea chiar dacă nu administrezi nimic.

---

## Ideea

**Că o peșteră există** și **unde se află o peșteră** sunt fapte diferite, cu audiențe diferite.

O peșteră marcată cu **Locație protejată** rămâne lizibilă: numele, codul, descrierea, istoricul,
documentele, turele care au numit-o. Ce se reține este **poziția exactă** — și tot din care
poziția exactă ar putea fi reconstituită.

Vederea unei poziții protejate exacte este o acțiune proprie de permisiune: **Locație exactă**. Se
acordă ca orice altă acțiune, la orice cuprins. Vezi [Permisiuni](permissions.md).

## Stabilirea ei

Pe o peșteră sau un obiect, în secțiunea **Acces**: **Locație protejată**.

> *„Coordonatele exacte și câmpurile de localizare precisă sunt ascunse utilizatorilor fără
> permisiune explicită."*

Se mai poate stabili:

- pe formularul **Intrare nouă** al hărții (*Protejează locația exactă*),
- în masă la un [import GPS](../features/geodata.md) — *Marchează tot ce se creează ca locație
  protejată*,
- **prin moștenire**: conținerea transmite protecția **în jos**, deci protejarea unei zone
  carstice protejează obiectele dinăuntrul ei.

---

## Fiecare cale pe care o acoperă

Acesta este tabelul important, pentru că o protecție care acoperă harta dar nu exportul nu este o
protecție.

| Cale | Ce primește un cititor fără drept |
|---|---|
| **Harta** | O poziție aproximativă, marcată *„Locație aproximativă — coordonatele exacte sunt protejate."*, sau *„Localizare nedivulgată — geometria exactă este protejată."* pentru o geometrie |
| **Tabele și liste** | Câmpurile de localizare precisă sunt reținute |
| **Exporturi** (GeoJSON, GPX, KML, CSV, shapefile) | Aceeași protecție ca ecranul |
| **Linkuri și vederi partajate** | Aceeași protecție. Partajarea nu este o cale de ocolire. [Linkul unei vederi partajate](../features/saved-views-and-windows.md#vederi-salvate) merge mai departe: nu desenează **deloc o peșteră sau un obiect protejat** — nici aproximativ, nici numărat într-un grup — chiar și acolo unde orice cont îl poate vedea exact, pentru că nimic din ce li se acordă conturilor nu urmează un link |
| **Fotografii** | O poză al cărei punct de capturare ar localiza peștera se dă ca **imagine la dimensiune completă, fără datele de locație**, marcată *Locație eliminată*; fișierul păstrat nu se schimbă niciodată. Doar acolo unde nu se poate face o asemenea copie (un film, un format care nu poate fi rescris) originalul este refuzat. Randarea se arată întotdeauna |
| **Pagini și rapoarte de tură** | Peșterile pe care nu le poți deschide **nu sunt numite**; ți se spune *câte* nu sunt numite. Raportul generat al unei ture reține la fel o stație și o adâncime — dar vezi limita de mai jos pentru un raport **deja clasat** |
| **E-mailuri de notificare** | **Nu numesc niciodată o peșteră.** Nici mesajele de tură, nici alarma de echipă întârziată |
| **Calendarul** | Un rând **nu numește niciodată o peșteră**, nici un număr de peșteri omise |
| **Istoricul modificărilor** | O valoare protejată se citește *„Valoare ascunsă (locație protejată)"* |
| **Cea mai mică distanță** | Refuzată dacă nu poți localiza **ambele** peșteri |
| **Roca de deasupra** | Refuzată **în întregime** dacă nu poți localiza peștera — nu o curbă goală, nimic. Curba este poziția galeriei desenată față de terenul de deasupra ei. Are nevoie și de dreptul de a citi datele de elevație |
| **Adâncime și înălțime între intrări** | Refuzată **în întregime** dacă nu poți localiza peștera. O intrare pe care nu o poți localiza este lăsată în afara oricărei cifre și a oricărei numărători, iar nivelul exurgenței se ia doar din izvoarele pe care le poți localiza |
| **Altitudinile intrărilor față de teren** | Numărate și ordonate peste intrările pe care le poți localiza și peste niciuna alta, deci ordinea și totalurile nu se schimbă când se adaugă o intrare protejată. Nu se întoarce nicio poziție. O singură peșteră este refuzată în întregime dacă nu o poți localiza, iar completarea răspunde la o intrare ascunsă ca *negăsită*. Are nevoie și de dreptul de a citi datele de elevație |
| **Profil de adâncime** | Refuzat **în întregime** dacă nu poți localiza peștera, cu răspunsul *negăsit*. Altitudine în funcție de distanța de-a lungul galeriilor; nu se întoarce nicio poziție |
| **Profilul reliefului de-a lungul unei linii** | Nu știe nimic despre peșteri: linia este doar coordonatele pe care le dai. Cere dreptul de a citi datele de elevație |
| **Intrări și doline față de relief** | Numărate peste intrările și dolinele pe care le poți localiza și peste niciuna alta, deci nicio numărătoare, pondere sau raport nu se schimbă când se adaugă una protejată. Nu se întoarce nicio poziție. Cere dreptul de a citi datele de elevație |
| **Verificarea duplicatelor la import** | Se compară doar cu obiectele a căror poziție exactă o poți vedea |
| **Originea unui `.stl`** | Originea precompletată spune că este deliberat aproximativă și cere punctul adevărat |
| **Paginile QR** | Nicio peșteră, niciun loc, nicio poziție — pentru nimeni, cu sau fără cont |
| **Sincronizarea cu telefonul** | O poziție pe care nu ai voie s-o ai este **pur și simplu absentă** |
| **Legături** | O legătură arată fiecărui cititor doar capetele pe care le poate vedea |
| **Ture publicate** | O peșteră protejată **nu poate fi publicată deloc**, iar protejarea ei ulterioară închide linkurile deja date. O limită: un fișier de topografie înregistrat la o peșteră neprotejată poate desena și o vecină protejată, iar asta nu se poate vedea în interiorul fișierului — aplicația doar [avertizează după poziție](../features/live-tracking.md#peșteră-protejată-în-apropiere) |

**Nimic din toate acestea nu este făcut de client.** Reținerea se face pe server. Un browser
modificat, un apel direct la interfața de programare și o pagină extrasă automat primesc toate
același răspuns.

### O limită care merită știută: un raport deja clasat pe o tură

**Un raport clasat înainte ca o peșteră să fie protejată poate numi în continuare stațiile acelei
peșteri pentru oricine poate citi tura. Protejarea peșterii nu retrage copia aceea, iar modul de a
o înlătura este să ștergi copia clasată.**

O tură poate fi [urmărită de la suprafață](../features/live-tracking.md), iar modelul de raport al
unui club poate tipări jurnalul urmăririi, sau lista stațiilor topografiei, în raportul turei. O
copie **clasată pe tură** este un fișier: a tipărit stațiile care puteau fi comunicate oricărui
cont în ziua în care a fost făcută și continuă să le spună și după aceea. Orice suprafață vie —
harta, fila de urmărire, un export, un raport făcut din nou — întreabă iar la fiecare citire și
reține locul cum trebuie; doar un fișier deja clasat nu poate.

Așadar, când protejezi o peșteră care a fost urmărită de la suprafață:

1. Deschide turele urmărite în ea (pagina peșterii le listează) și vezi ce este clasat pe
   fiecare.
2. Șterge orice raport clasat al cărui model a tipărit urmărirea sau lista stațiilor.
3. Raportează tura din nou dacă clubul vrea o copie acolo — cea nouă nu numește nicio stație.

Scoaterea automată a acelor copii de pe ture a fost construită și apoi oprită: trebuia să treacă
mult mai larg decât problema (un fișier nu consemnează nimic despre faptul că modelul lui a
tipărit vreodată un loc, așa că ar fi plecat fiecare raport generat al fiecărei ture urmărite
vreodată în peșteră, inclusiv al turelor șterse), iar judecata a fost că logica costă mai mult
decât breșa pe care o acoperă. **Hotărârea va fi reluată**, iar pagina aceasta o va spune când se
schimbă.

---

## Ce *nu* se reține

În mod deliberat:

- **Că peștera există**, numele și codul ei.
- **Documentele ei.** Documentele unei peșteri protejate sunt întotdeauna servite — nu sunt
  niciodată ascunse sau scoase.
- **Că o listă este scurtă.** Primești *„n nearătate ție"*, nu o listă tacit incompletă.
  Ascunderea numărului ar fi propriul ei fel de scurgere.

### Setarea de asociere a atașamentelor

Există o singură excepție deliberată și este un comutator:

**Administrare → Mesagerie → Protecția locațiilor → „Arată atașamentele peșterilor protejate
tuturor celor care le pot citi."**

Oprită implicit. Protecția locațiilor reține în mod normal *faptul că un document indică acea
peșteră anume*, păstrând documentul lizibil. Pornirea comutatorului divulgă asocierea.

> **Pornirea lui nu dezvăluie niciodată o poziție.** Urmărirea linkului tot dă vederea protejată.

### Setarea fluxului de calendar

Al doilea comutator de pe aceeași filă — **„Permite membrilor să se aboneze la calendarul lor din
exterior"** — este aici din același motiv: hotărăște cât de departe pot călători datele clubului.

Oprit implicit. Pornit, fiecare membru poate crea adrese secrete pe care un calendar de pe telefon
sau de pe calculator le interoghează fără autentificare ([Abonarea la
calendar](../features/calendar-feed.md)). Un flux poartă **doar titlurile, datele și legăturile** a
ceea la care participă membrul — niciodată un loc, o poziție sau un participant — și este citit prin
permisiunile proprii ale membrului la fiecare interogare, așa că o peșteră protejată nu este mai
accesibilă printr-un flux decât prin pagina de calendar. Oprirea lui din nou oprește toate adresele
deja distribuite.

### Setările fișierelor foto

Alte trei comutatoare de pe aceeași filă hotărăsc ce călătorește *în interiorul* unui fișier
fotografic. Niciunul nu schimbă cui i se poate spune poziția unei fotografii — aceea urmează regulile
de mai sus.

- **Păstrează locația în fișierele foto.** Pornit implicit. *Pornit:* cine are voie să vadă unde a
  fost făcută o poză o descarcă exact cum a fost încărcată, cu GPS cu tot. *Oprit:* toți descarcă
  imaginea la dimensiune completă fără datele de locație (GPS, blocul XMP încorporat și numele de
  locuri din IPTC; detaliile aparatului și ale expunerii rămân). Oricum, cine *nu* are voie să vadă
  unde a fost făcută primește doar copia fără locație. Copia se face prima dată când este cerută și
  se păstrează lângă miniaturi; fișierul păstrat nu se atinge niciodată.
- **Cu opțiunea de locație dezactivată, permite totuși celor care au voie să vadă locația să descarce
  filmele, înregistrările și imaginile care nu pot fi rescrise.** Oprit implicit și contează doar cât
  timp comutatorul de mai sus este oprit. Filmele, înregistrările și imaginile în formate pe care
  aplicația nu le poate rescrie (orice în afară de JPEG, PNG și WebP) nu au o copie fără locație, deci
  cu comutatorul de mai sus oprit nimeni nu le-ar putea descărca. Pornit, cititorii care au voie să
  vadă locația le primesc așa cum sunt păstrate.
- **Spune telefoanelor mărimea și amprenta fotografiilor trimise fără locație.** Când aplicația de pe
  telefon cere o fotografie pe care o va primi fără locație, serverul face copia pe loc și îi spune
  aplicației mărimea și amprenta SHA-256, ca aplicația să poată arăta mărimea și verifica dacă
  descărcarea a ajuns întreagă. Cererile acestea sunt mai lente (până la 200 de fotografii pot fi
  rescrise într-una) și fiecare copie ocupă spațiu pe disc.

O fotografie dată în acest fel este marcată **Locație eliminată** oriunde este arătată sau oferită la
descărcare.

---

## Lucruri care în mod deliberat *nu* sunt aproximate

Două, și amândouă poartă un avertisment pe propriul formular:

**Schița unei ture.** *„Locație exactă — schița unei ture nu este niciodată aproximată. Toți cei
care pot citi această tură văd această formă exact cum a fost desenată, indiferent ce protecție
poartă peșterile pe care tura le numește."*

**Punctul de întâlnire al unei ture.** *„Poziție exactă — locul unde se adună echipa nu este
niciodată aproximat. Un punct de întâlnire lângă o intrare păzită localizează acea intrare pentru
toți cei invitați."*

Acestea nu sunt scăpări. O schiță și un punct de întâlnire sunt lucruri desenate de cineva pentru
o audiență anume, iar aproximarea lor le-ar face inutile pentru scopul în care au fost desenate.
Răspunsul aplicației este să **avertizeze zgomotos în momentul desenării**, nu să degradeze tacit
datele.

**Spune-le membrilor despre acestea două.** Sunt cel mai probabil mod în care se divulgă în
practică o intrare protejată.

---

## Stabilirea politicii clubului

Aplicația îți dă mecanismul. Politica este a ta și merită scrisă:

- **Când este protejată o locație?** După clasa de protecție? La cererea proprietarului de teren?
  După stadiul explorării? O regulă pe care o poți aplica consecvent bate judecata caz cu caz.
- **Cine deține *Locație exactă*, și la ce cuprins?** Tot, un set de obiecte, un subarbore?
- **Ce se întâmplă când cineva este invitat pe o tură la o peșteră pe care nu o poate localiza?**
  Aplicația anunță proprietarul peșterii și administratorii deplini și spune explicit că nu a
  fost anunțat nimeni altcineva. Cineva trebuie să acționeze pe baza acelui mesaj — hotărăște cine.

---

Înrudite: [Permisiuni](permissions.md) ·
[Partajare și pagini publice](../features/sharing-and-public-pages.md) ·
[Ture](../features/trips.md)
