# Protecția locațiilor

🇬🇧 [English](../../admin/location-protection.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Permisiuni](permissions.md)

---

Mecanismul care permite unei instalări să spună **„această peșteră există și aveți voie să citiți
despre ea, dar nu aveți voie să știți unde este."**

Este motivul specific speologic pentru care există această aplicație în loc să se folosească un
SIG general, și merită înțeles ca lumea chiar dacă nu administrați nimic.

---

## Ideea

**Că o peșteră există** și **unde se află o peșteră** sunt fapte diferite, cu audiențe diferite.

O peșteră marcată cu **Locație protejată** rămâne lizibilă: numele, codul, descrierea, istoricul,
documentele, turele care au numit-o. Ce se reține este **poziția exactă** — și tot din care
poziția exactă ar putea fi reconstituită.

Vederea unei poziții protejate exacte este o acțiune proprie de permisiune: **Locație exactă**. Se
acordă ca orice altă acțiune, la orice cuprins. Vedeți [Permisiuni](permissions.md).

## Stabilirea ei

Pe o peșteră sau un element, în secțiunea **Acces**: **Locație protejată**.

> *„Coordonatele exacte și câmpurile de localizare precisă sunt ascunse utilizatorilor fără
> permisiune explicită."*

Se mai poate stabili:

- pe formularul **Intrare nouă** al hărții (*Protejează locația exactă*),
- în masă la un [import GPS](../features/geodata.md) — *Marchează tot ce se creează drept locație
  protejată*,
- **prin moștenire**: conținerea transmite protecția **în jos**, deci protejarea unei zone
  carstice protejează elementele dinăuntrul ei.

---

## Fiecare cale pe care o acoperă

Acesta este tabelul important, pentru că o protecție care acoperă harta dar nu exportul nu este o
protecție.

| Cale | Ce primește un cititor fără drept |
|---|---|
| **Harta** | O poziție aproximativă, marcată *„Locație aproximativă — coordonatele exacte sunt protejate"*, sau *„Locație nedivulgată"* pentru o geometrie |
| **Tabele și liste** | Câmpurile de localizare precisă sunt reținute |
| **Exporturi** (GeoJSON, GPX, KML, CSV, shapefile) | Aceeași protecție ca ecranul |
| **Linkuri și vederi partajate** | Aceeași protecție. Partajarea nu este o cale de ocolire |
| **Fotografii** | **Originalul** unei poze al cărei punct de capturare ar localiza peștera este refuzat. Randarea se arată |
| **Pagini și rapoarte de tură** | Peșterile pe care nu le puteți deschide **nu sunt numite**; vi se spune *câte* nu sunt numite |
| **E-mailuri de notificare** | **Nu numesc niciodată o peșteră.** Nici mesajele de tură, nici alarma de echipă întârziată |
| **Calendarul** | Un rând **nu numește niciodată o peșteră**, nici un număr de peșteri omise |
| **Istoricul modificărilor** | O valoare protejată se citește *„Valoare ascunsă (locație protejată)"* |
| **Cea mai mică distanță** | Refuzată dacă nu puteți localiza **ambele** peșteri |
| **Verificarea duplicatelor la import** | Se compară doar cu obiectele a căror poziție exactă o puteți vedea |
| **Originea unui `.stl`** | Originea precompletată spune că este deliberat aproximativă și cere punctul adevărat |
| **Paginile QR** | Nicio peșteră, niciun loc, nicio poziție — pentru nimeni, cu sau fără cont |
| **Sincronizarea cu telefonul** | O poziție pe care nu aveți voie s-o aveți este **pur și simplu absentă** |
| **Legături** | O legătură arată fiecărui cititor doar capetele pe care le poate vedea |

**Nimic din toate acestea nu este făcut de client.** Reținerea se face pe server. Un browser
modificat, un apel direct la interfața de programare și o pagină extrasă automat primesc toate
același răspuns.

---

## Ce *nu* se reține

În mod deliberat:

- **Că peștera există**, numele și codul ei.
- **Documentele ei.** Documentele unei peșteri protejate sunt întotdeauna servite — nu sunt
  niciodată ascunse sau scoase.
- **Că o listă este scurtă.** Primiți *„n care nu vă sunt arătate"*, nu o listă tacit incompletă.
  Ascunderea numărului ar fi propriul ei fel de scurgere.

### Setarea de asociere a atașamentelor

Există o singură excepție deliberată și este un comutator:

**Administrare → Mesagerie → Protecția locațiilor → „Arată atașamentele peșterilor protejate
tuturor celor care le pot citi."**

Oprită implicit. Protecția locațiilor reține în mod normal *faptul că un document indică acea
peșteră anume*, păstrând documentul lizibil. Pornirea comutatorului divulgă asocierea.

> **Pornirea lui nu dezvăluie niciodată o poziție.** Urmărirea linkului tot dă vederea protejată.

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

**Spuneți-le membrilor despre acestea două.** Sunt cel mai probabil mod în care se divulgă în
practică o intrare protejată.

---

## Stabilirea politicii clubului

Aplicația vă dă mecanismul. Politica este a dumneavoastră și merită scrisă:

- **Când este protejată o locație?** După clasa de protecție? La cererea proprietarului de teren?
  După stadiul explorării? O regulă pe care o puteți aplica consecvent bate judecata caz cu caz.
- **Cine deține *Locație exactă*, și la ce cuprins?** Tot, un set de elemente, un subarbore?
- **Ce se întâmplă când cineva este invitat pe o tură la o peșteră pe care nu o poate localiza?**
  Aplicația anunță proprietarul peșterii și administratorii compleți și spune explicit că nu a
  fost anunțat nimeni altcineva. Cineva trebuie să acționeze pe baza acelui mesaj — hotărâți cine.

---

Înrudite: [Permisiuni](permissions.md) ·
[Partajare și pagini publice](../features/sharing-and-public-pages.md) ·
[Ture](../features/trips.md)
