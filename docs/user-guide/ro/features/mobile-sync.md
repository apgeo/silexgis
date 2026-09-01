# Telefon și sincronizare offline

🇬🇧 [English](../../features/mobile-sync.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Contul și setările](account-and-settings.md) ·
[Protecția locațiilor](../admin/location-protection.md)

---

Clientul web are nevoie de conexiune. **Offline-ul este rostul aplicației de telefon.**

SilexGIS servește o interfață de sincronizare pe rânduri pentru **SpeleoLoc**, o aplicație de
navigare în peșteri pentru telefoane. Un telefon se autentifică drept aplicație instalată,
numește peșterile pe care le poartă, le citește pagină cu pagină și își scrie propriile modificări
înapoi — cu serverul arbitrând fiecare rând.

Pagina aceasta este partea dumneavoastră: ce configurați, ce poate și ce nu poate primi un
dispozitiv, și ce se întâmplă cu ce trimite înapoi.

---

## Selecții de sincronizare

**Setări → Sincronizare.** O **selecție** numește peșterile pe care le poartă un telefon offline.

> *„O selecție numește peșteri — nu le preia niciodată — deci încheierea uneia aici oprește un
> dispozitiv să le mai ceară și nu schimbă nimic despre peșterile însele."*

### Crearea uneia

| Câmp | |
|---|---|
| **Nume** | |
| **Peșteri** | Căutate după nume și adăugate în selecție |
| **Înregistrările noi sunt vizibile pentru** | Ce primesc înregistrările proprii ale unui dispozitiv când le trimite înapoi |
| **Grup de speologie** | Obligatoriu dacă vizibilitatea de încărcare este *grup de speologie* |

Numărul este afișat: *„Peșteri purtate: n."*

**Vizibilitatea la încărcare este implicit doar pentru dumneavoastră, până o lărgiți.** Dacă
alegeți *grup de speologie* trebuie să numiți grupul — *„Fără unul, înregistrările vizibile unui
grup de speologie nu sunt vizibile nimănui"* — și trebuie să fiți membru al lui, altfel un
dispozitiv nu poate crea înregistrări pentru el.

### Ce merge prost și ce înseamnă

- **O peșteră pe care nu o mai puteți citi** apare ca atare în listă. Scoateți-o și salvați din
  nou.
- **„Această selecție a fost modificată în altă parte"** — pe un dispozitiv sau în altă filă.
  Selecția poartă o revizie, deci o modificare făcută din două locuri nu se suprascrie tacit.
  Redeschideți-o și faceți modificarea din nou.

### Revocarea

**Revocă** oprește un dispozitiv să mai poată cere acele peșteri. **Nimic din ce se află deja pe
dispozitiv nu este șters** — încheiați un drept, nu ajungeți în telefonul cuiva.

### Ale cui sunt selecțiile

O selecție este **a dumneavoastră**. Dacă un administrator complet poate citi selecția altcuiva
este o setare a instalării, iar aceasta este oprită dacă nu este pornită anume.

---

## Ce vorbește acest server

Pagina de Sincronizare raportează ce oferă instalarea:

- **versiunea protocolului** — o afirmație despre cod, nu o setare pe care o instalare o poate
  coborî,
- **limitele** — rânduri per pagină de descărcare, rânduri per încărcare,
- ce este **disponibil**, anunțat pe nume: *descărcare*, *încărcare*. Un dispozitiv care
  întâlnește un server care servește doar o parte dintre ele **ia părțile care există**, în loc să
  eșueze la primul transfer. Unele instalări arată *„Doar selecții — acest server nu mută încă
  rânduri."*

---

## Ce nu poate primi un dispozitiv

Rândurile pe care nu aveți voie să le aveți sunt **absente** — nu aproximate.

Asta diferă de restul aplicației în mod intenționat, iar motivul merită înțeles:

> În altă parte, cine nu poate localiza o peșteră protejată primește un punct aliniat la o grilă,
> marcat *aproximativ*. O coordonată aliniată pe o hartă este privită o dată. **O coordonată
> aliniată livrată unui telefon este scrisă în clar, păstrată atâta timp cât aplicația este
> instalată și repartajată unor oameni pe care acest server nu i-a autentificat niciodată.** Iar
> aceste rânduri vin în număr mare: o împrăștiere de locuri protejate aliniate în aceleași câteva
> pătrate de grilă **conturează peștera pe care protecția există ca s-o ascundă.**

Trei consecințe:

**Decizia este per rând, niciodată per peșteră.** Un loc din interiorul unei peșteri poate fi
propria lui rădăcină de protecție, independent de peștera care îl conține — deci un filtru care ar
întreba doar despre peșteră ar preda un punct interior protejat independent cuiva îndreptățit la
peșteră, dar nu și la punct.

**Rândurile reținute sunt scoase înainte de tăierea paginii**, nu după. O pagină care s-ar scurta
i-ar spune unui dispozitiv *câte* rânduri nu a avut voie să aibă.

**Eșuează închis.** Un identificator fără niciun rând în spate este raportat ca reținut, nu ca
inexistent.

Vedeți [Protecția locațiilor](../admin/location-protection.md).

---

## Ce se întâmplă cu ce trimite un dispozitiv înapoi

Un telefon încarcă un lot de rânduri, aplicate în ordinea dată, **arbitrate rând cu rând** față de
ce deține deja serverul. Trei proprietăți îl țin laolaltă, și fiecare există din cauza unui mod
anume în care se strică un dispozitiv offline:

| Proprietate | Pentru că |
|---|---|
| Lotul poartă **un identificator generat de dispozitiv** | Un răspuns pierdut pe drumul înapoi costă o retrimitere, nu un cadastru duplicat |
| Un rând poartă **identificatorul dat de dispozitiv**, adoptat ca atare | Nicio parte nu trebuie vreodată să traducă numele celeilalte |
| Un rând poartă **revizia de server pe care a văzut-o ultima dată dispozitivul** — singurul lucru comparat | Niciodată ceasul propriu al dispozitivului, care este nesincronizat, resetabil de cine ține telefonul și frecvent greșit cu ore |

### Fiecare încărcare este un lot de import

Aceasta este partea de reținut ca utilizator.

**O încărcare de pe telefon este înregistrată exact ca un fișier importat.** Apare la **Geodate →
Importuri**, marcată ca încărcare de pe telefon — ceea ce înseamnă că un speolog poate vedea ce a
pus un telefon în cadastru și poate **lua totul înapoi printr-un singur act**, prin același ecran
și același buton **Anulează** care întoarce un GPX greșit.

Vedeți [Geodate → Importuri](geodata.md#importuri).

### Valori implicite și avertismente de duplicat

Un dispozitiv care nu numește un fel primește o valoare implicită rezonabilă — o peșteră aterizează
ca peșteră, o intrare ca intrare naturală, un loc din interiorul unei peșteri ca loc.

Iar fiecare rând creat este informat despre **cele mai apropiate câteva lucruri deja aflate în
cadastru**, ca un speolog care decide dacă tocmai a reintrat într-o peșteră pe care clubul o are
deja să primească vecinii cei mai apropiați — nu un recensământ al masivului.

---

## Autentificarea unui dispozitiv și retragerea ei

Un telefon se autentifică **ca el însuși** — ca aplicație instalată cu înregistrare proprie, nu
primind parola dumneavoastră și nici împrumutând-o pe a browserului. Credențialul lui de
reîmprospătare durează considerabil mai mult decât o sesiune de browser (45 de zile implicit, iar
un operator o poate stabili între 1 și 365).

Fereastra aceea lungă este apărabilă doar pentru că poate fi luată înapoi, deci:

> **Schimbarea sau resetarea parolei revocă fiecare jeton și fiecare autorizație de pe cont —
> fiecare client, fiecare dispozitiv, inclusiv cel pe care îl folosiți.**

Acesta este răspunsul la un telefon pierdut, și este o singură acțiune la care ajunge orice
speolog, fără administrator. Sunteți deconectat peste tot și vă reconectați acolo unde încă aveți
dispozitivul.

---

## Pentru cine scrie un client

Contractul de comunicație este documentat separat și nu este documentație de utilizator:

- [`docs/speleoloc-sync/`](../../../speleoloc-sync/README.md) — de unde se începe
- Schimburile înregistrate sub `contract/speleoloc-sync/` sunt specificația comunicației și sunt
  comparate octet cu octet de suita de teste

---

## Tot pe telefon: aplicația web însăși

Clientul web funcționează pe telefon. Spațiul de lucru al hărții se adaptează la atingere,
**inclusiv editarea completă a geometriei cu degetul**, iar aplicația **se instalează pe ecranul
de pornire**.

Nu este offline. Dar pentru citirea unui document la un punct de plecare, răspunsul la o invitație
sau bifarea unei liste de verificare, funcționează.

---

Înrudite: [Protecția locațiilor](../admin/location-protection.md) ·
[Geodate](geodata.md#importuri) ·
[Contul și setările](account-and-settings.md)
