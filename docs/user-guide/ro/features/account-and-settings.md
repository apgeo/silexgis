# Contul și setările

🇬🇧 [English](../../features/account-and-settings.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Notificări](notifications.md) ·
[Coordonate](coordinates.md) · [Telefon și sincronizare offline](mobile-sync.md)

---

**Setări** are zece pagini: Profil · Cont · E-mail · Notificări · Securitate · Accesibilitate ·
Filtre salvate · Coordonate · Sincronizare · Avansat. Se ajunge la ele din meniul contului, din
dreapta sus, unde este și **Deconectare**.

---

## Profil

### Identitate

**Nume de utilizator** · **Adresă de e-mail** · **Prenume** · **Nume** · **Nume afișat**

> **Numele afișat** apare lângă tot ce adăugați. Fără el apăreți sub un nume generat.
> Stabiliți-l devreme.

### Despre dumneavoastră

**Descriere** (o scurtă prezentare) · **Telefon** · **Club de speologie** · **Poză de profil**

Poza de profil poate fi încărcată (sub 5 MB) sau aleasă din setul inclus — liliac, cască, lampă
frontală, coardă, carst, stalactită, busolă, sifon.

Numărul de telefon se schimbă din **Securitate**, pentru că i se trimite întâi un cod prin SMS.

**Clubul de speologie** se alege din lista grupurilor de speologie ale instalării; tastarea o
restrânge, fără să conteze literele mari sau diacriticele. Dacă clubul nu este încă în listă,
aceasta oferă **Creează «nume»** — alegerea lui adaugă clubul la grupurile de speologie și îl
selectează, fără să părăsiți formularul. Un nume care diferă de un club existent doar prin litere
mari sau punctuație nu este un club nou: vi se oferă cel existent. Oferta li se face tuturor celor
care pot adăuga grupuri de speologie — oricărui cont, dacă instalarea nu a retras acest drept.

### Adrese

Unde puteți fi găsit. Fiecare adresă are o **etichetă**, **țară**, **oraș**, **strada și restul**
și, opțional, **un punct pe hartă** pe care îl alegeți sau îl tastați.

### Audiențe per câmp

**Pictograma mică de lângă un câmp stabilește cine îl poate vedea**: *Doar eu · Grupurile mele de
speologie · Membri autentificați*. Este per câmp, nu o singură setare pentru tot profilul — deci
numele poate fi vizibil tuturor, iar adresa nu.

---

## Cont

**Nume de utilizator** — vă autentificați întotdeauna cu adresa de e-mail, deci schimbarea lui
schimbă doar felul în care sunteți listat.

**Conturile nu se șterg de aici.** *„Tot ce ați adăugat rămâne la club, așa că întrebați un
administrator."*

**Exportați-vă datele** — o copie a profilului, adreselor, setărilor, filtrelor salvate și o listă
a ce ați adăugat.
Se pune la coadă, se pregătește în fundal, apoi se descarcă.

**Flux de calendar** — se afișează doar când un administrator a pornit fluxurile. Creează adresa
secretă la care se abonează un calendar de pe telefon sau de pe calculator, le listează pe cele pe
care le aveți după nume și dată și revocă una. Vedeți [Abonarea la calendar](calendar-feed.md).

---

## E-mail

Adresa dumneavoastră și dacă este **confirmată**. Dacă nu, **Trimite un link de confirmare**.

**Schimbarea adresei**: dați adresa nouă, un link pleacă spre ea și **nu se schimbă nimic până nu
deschideți acel link**. Până atunci continuați să vă autentificați cu cea curentă. Pagina arată
schimbarea în așteptare și vă lasă să **retrimiteți** (cu o scurtă pauză) sau să **anulați
schimbarea**.

---

## Notificări

Vedeți [Notificări](notifications.md) — are pagina ei.

---

## Securitate

### Parolă

Schimbați parola cu care vă autentificați: **Parola actuală**, apoi cea nouă de două ori. O parolă
are nevoie de cel puțin 10 caractere.

> *„Schimbarea parolei vă deconectează peste tot, inclusiv aici. Apoi vă autentificați din nou cu
> cea nouă."*

Un cont care se autentifică doar printr-un furnizor extern nu are parolă aici, iar cardul o
spune. Primiți un e-mail de fiecare dată când parola este schimbată sau resetată — o singură
dată — astfel încât o schimbare pe care nu ați făcut-o să nu treacă neobservată.

### Autentificare în doi pași

Cere o a doua dovadă de identitate la autentificare. Trei metode:

| Metodă | |
|---|---|
| **Aplicație de autentificare** | Un cod de șase cifre dintr-o aplicație de pe telefon. **Funcționează fără semnal** — cea mai puternică |
| **Cod pe e-mail** | La adresa confirmată |
| **Cod prin SMS** | La numărul confirmat |

Fiecare poate fi pornită sau oprită individual, iar una poate fi făcută **implicită**. O metodă
pe care instalarea nu a activat-o este marcată ca atare; una care are nevoie de o adresă sau un
număr confirmat spune care.

Configurarea unei aplicații de autentificare: scanați pătratul QR, sau adăugați cheia manual, apoi
tastați codul pe care îl arată.

**Codurile de recuperare** sunt afișate **o singură dată**. Păstrați-le atunci. Pagina vă spune
câte au mai rămas.

Ați pierdut și aplicația de autentificare, și codurile de recuperare? Un administrator care
gestionează conturile vă poate **reseta al doilea factor**: toate metodele sunt dezactivate, sunteți
deconectat peste tot și anunțat prin e-mail, apoi vă autentificați doar cu parola și configurați
din nou un al doilea factor. Vedeți [Conturi](../admin/README.md#conturi).

Prea multe coduri greșite blochează contul pentru o vreme.

### Număr de telefon

Adăugați sau schimbați numărul — vi se trimite un cod prin SMS și confirmați. Introduceți-l în
formă internațională (`+40712345678`). Este folosit **doar pentru coduri de autentificare**.

Ștergerea numărului oprește odată cu el și codurile prin SMS, și o spune. **Dacă codurile prin SMS
sunt singurul factor secundar**, pagina întreabă a doua oară — *„Ștergeți singurul factor
secundar?"* — pentru că autentificarea în doi pași se oprește atunci și contul rămâne protejat doar
de parolă; primiți un e-mail care o spune. Dacă instalarea nu are o poartă SMS, codurile prin SMS
pur și simplu nu sunt disponibile, iar pagina spune de ce.

### Pierderea unui dispozitiv

> **Schimbarea sau resetarea parolei revocă fiecare jeton și fiecare autorizație de pe contul
> dumneavoastră — fiecare client, fiecare dispozitiv, inclusiv cel pe care îl folosiți.**

Acesta este răspunsul la un telefon pierdut sau furat, și este **o singură acțiune la care ajunge
orice speolog**, fără să aștepte un administrator. Sunteți deconectat peste tot și vă
reconectați pe dispozitivele pe care încă le aveți.

Funcționează așa pentru că persoana care acționează nu-și poate lista propriile sesiuni și nu
poate numi dispozitivul care i-a fost luat — deci singurul lucru onest de revocat este toate.

Vedeți [Telefon și sincronizare offline](mobile-sync.md#autentificarea-unui-dispozitiv-și-retragerea-ei).

### Conturi legate

Dacă instalarea oferă furnizori externi, vă puteți autentifica cu **Google, GitHub sau alt
furnizor OIDC** pe lângă parolă. Legați și dezlegați de aici.

Un furnizor dovedește cine sunteți la el și nimic mai mult: **cu autentificarea în doi pași
pornită, vi se cere în continuare codul** după autentificarea prin el.

---

## Accesibilitate

### Aspect (urmează contul pe orice dispozitiv)

| | |
|---|---|
| **Temă** | Ca sistemul · Luminoasă · Întunecată |
| **Densitate** | Confortabilă · Compactă |
| **Reduce animațiile** | Oprește animațiile și deplasarea hărții |
| **Hartă de ansamblu pe hartă** | Micul cadru care arată unde se află vederea hărții într-o zonă mai largă |
| **Limbă** | English · Română — aceeași alegere ca în comutatorul din bara de sus; vedeți [Limba](#limba) |

Ambele teme folosesc culorile proprii ale bibliotecii de interfață. Când vă deplasați într-o
pagină cu tastatura, un inel marchează controlul care are focusul, în tema luminoasă ca și în
cea întunecată; prima apăsare pe Tab oferă **Sari la conținutul principal**, iar pe pagina hărții
**Sari la hartă**. Apăsați **?** pentru lista scurtăturilor de tastatură.

### Doar acest browser

*„Acestea rămân pe acest dispozitiv — un telefon și o stație de lucru vor răspunsuri diferite."*

| | |
|---|---|
| **Deschide aplicația pe** | Hartă · Panou |
| **Ascunde controalele de pe hartă** | |

**Resetează** readuce cele două setări de mai sus împreună cu tema, densitatea și reducerea
animațiilor. Limba și harta de ansamblu rămân cum sunt, iar numerele de la [Avansat](#avansat) nu sunt atinse —
acelea au propriul drum înapoi acolo.

---

## Filtre salvate

| | |
|---|---|
| **Partajați filtrele noi cu** | Doar eu · Membrii unuia dintre grupurile dumneavoastră de speologie · Toți cei autentificați |

De aici **pornește** un filtru pe care îl salvați: fiecare fereastră de salvare îl preselectează,
iar dumneavoastră puteți alege altfel de fiecare dată. Este **Doar eu** până îl schimbați și vă
urmează contul pe orice dispozitiv. Partajarea le dă celorlalți doar întrebarea — fiecăruia i se
răspunde cu ce are el însuși dreptul să vadă. Dacă grupul de speologie ales aici nu mai este unul
cu care puteți partaja, filtrele noi pornesc private până alegeți din nou. Vedeți
[Filtre salvate](search-and-filters.md#filtre-salvate).

---

## Coordonate

Cum sunt scrise pozițiile oriunde sunt afișate sau copiate. *„Aceste setări vă urmează contul."*

| | |
|---|---|
| **Coordonate afișate ca** | Formatul în care este scrisă fiecare poziție — grade zecimale, grade și minute, grade–minute–secunde, UTM, MGRS, Stereo 70, caroiajele elvețiene sau Plus Code |
| **Listate și la „Alte formate"** | Formatele în care mai poate fi deschisă o poziție afișată în detaliu — o intrare selectată, intrările unei peșteri —, fiecare cu butonul ei de copiere |
| **Setările fiecărui format** | De pildă zona UTM păstrată lângă marginea unei zone, cu o notă despre cât vă puteți baza pe fiecare format |

Un punct de exemplu arată pe loc fiecare format ales, pe măsură ce le schimbați. Un format care
nu are referință pentru un loc — un caroiaj național în afara țării lui — este omis acolo. Tot
ce privește formatele, pozițiile protejate și scrierea unei poziții în oricare dintre ele este la
[Coordonate și convertorul](coordinates.md).

---

## Sincronizare

Pentru aplicația de telefon. Vedeți
[Telefon și sincronizare offline](mobile-sync.md).

---

## Avansat

Numere care hotărăsc cât cere aplicația de la calculatorul din fața dumneavoastră. Se păstrează
**doar în acest browser** și nu urmează contul — un telefon și o stație de lucru cer răspunsuri
diferite.

### Pereții peșterilor în vizualizarea 3D

Trei numere limitează vizualizarea 3D atunci când desenează
[pereții tuturor peșterilor din vedere](3d-view.md#pereții-tuturor-peșterilor-din-vedere):

| | |
|---|---|
| **Arată de la zoom-ul** | Nivelul de zoom de la care acest mod desenează ceva; sub el scena vă cere să apropiați vederea. De la 1 la 22 |
| **Peșteri cel mult** | Pentru câte peșteri se desenează pereții deodată, cele mai apropiate de mijlocul vederii întâi |
| **Megaocteți cel mult** | Cât model de pereți se descarcă și se păstrează pentru ele. Un browser are nevoie de aproximativ dublu în memoria grafică |

**Un câmp gol urmează valoarea implicită a instalării**, afișată cu gri în locul lui — 14, 12 și
64 MB, dacă administratorul nu le-a schimbat. Un număr scris de dumneavoastră înlocuiește
valoarea implicită în acest browser: măriți-l pe un calculator cu o placă grafică adevărată,
micșorați-l pe un telefon.

Ultimele două se opresc la un plafon stabilit de administrator — 60 de peșteri și 512 MB, dacă nu
a fost schimbat — iar pagina spune care este. Dacă plafonul este coborât ulterior, un număr mai
mare pe care îl aveați păstrat este adus la el.

**Folosește valorile implicite ale instalării** din acest card golește toate cele trei câmpuri.

Numerele sunt în vigoare data viitoare când deschideți vizualizarea 3D. O vizualizare 3D deja
deschisă în altă fereastră le păstrează pe cele vechi până când este reîncărcată.

**Pereții peșterii selectate** are un singur număr: **Întreabă peste (MB)**. Selectarea unei
peșteri în vizualizarea 3D îi descarcă pereții imediat, dacă nu sunt mai mari decât atât; altfel
vizualizarea spune cât de mari sunt și așteaptă până alegeți să-i descărcați. Gol urmează valoarea
implicită a instalării (50 MB, dacă nu a fost schimbată); **0** întreabă înaintea fiecărei
descărcări. Butonul lui propriu **Folosește valorile implicite ale instalării** îl golește.

### Poligonațiile de pe hartă

Două numere limitează poligonațiile pe care le desenează harta plană. Sunt aceleași două pe care
le arată lista de straturi a hărții cât timp stratul **Poligonații peșteri** este afișat —
vedeți [Spațiul de lucru al hărții](map-workspace.md#detaliul-poligonațiilor) — iar un număr
stabilit în oricare dintre cele două locuri este cel în vigoare în amândouă.

| | |
|---|---|
| **Detaliu de la nivelul** | Nivelul de zoom de la care apare detaliul topografic complet, inclusiv radialele; sub el vedeți conturul galeriilor. De la 1 la 22 |
| **Limită de linii** | Câte linii poate conține o vedere. Peste ea, poligonațiile rămase sunt reținute, iar harta spune câte |

**Un câmp gol urmează valoarea implicită a instalării**, afișată cu gri în locul lui — 18 și
25.000, dacă administratorul nu le-a schimbat. Aduceți detaliul mai devreme și măriți limita pe
un calculator rapid; împingeți-le pe amândouă în sens invers pe un telefon.

Limita de linii se oprește la un plafon stabilit de administrator — 100.000, dacă nu a fost
schimbat — iar pagina spune care este. Dacă plafonul este coborât ulterior, un număr mai mare pe
care îl aveați păstrat este adus la el.

**Folosește valorile implicite ale instalării** din acest card golește aceste două câmpuri și
lasă neatinse cele trei ale pereților; butonul din cardul pereților face invers. Fiecare card
dă înapoi doar numerele lui, astfel că renunțarea la o încercare într-unul nu aruncă ce ați
reglat pentru celălalt.

Acestea două limitează doar harta plană: vizualizarea 3D desenează poligonațiile după numerele
instalării.

---

## Ce vă urmează contul și ce rămâne în acest browser

Cea mai mare parte din ce aranjați este păstrată **cu contul**, așa că o regăsiți când vă
autentificați pe alt calculator:

- aspectul — tema, densitatea, reducerea animațiilor, harta de ansamblu — și limba;
- formatele de coordonate și cu cine sunt partajate filtrele salvate noi;
- felul în care este aranjat spațiul de lucru: aranjările panourilor, aranjările cu nume,
  panourile laterale, tipurile de elemente fixate.

Câteva lucruri rămân **doar în acest browser**, pentru că un telefon și o stație de lucru cer
răspunsuri diferite: unde se deschide aplicația, ascunderea controalelor de pe hartă și toate
numerele de la [Avansat](#avansat). O aranjare cu nume pentru care preferințele salvate ale
contului nu mai aveau loc este păstrată tot în acest browser, iar meniul aranjărilor o marchează
**Doar în acest browser** — ștergeți o aranjare pe care n-o mai folosiți și ea ajunge în cont
odată cu acea schimbare.

O schimbare făcută chiar înainte de a închide pagina, sau într-o
[fereastră desprinsă](saved-views-and-windows.md#panouri-desprinse), este scrisă și ea în cont.
**Alt membru care se autentifică în același browser pornește de la valorile implicite**, nu de la
aranjarea dumneavoastră.

---

## Deconectarea

**Deconectare**, din meniul contului, vă încheie autentificarea pe server, nu doar în pagină:
deschiderea din nou a aplicației vă cere adresa de e-mail și parola. Nimic din ce este al
dumneavoastră nu îi este dat celui care se autentifică după aceea în acel browser.

Pentru a vă deconecta **peste tot** — un telefon pierdut, un calculator comun la care nu mai
ajungeți — schimbați parola; vedeți [Pierderea unui dispozitiv](#pierderea-unui-dispozitiv).

---

## Limba

Comutatorul de limbă este în bara de sus, iar aceeași alegere este și la
[Accesibilitate](#accesibilitate). Aplicația vine cu engleză și română.

**Limba urmează contul**, deci mesajele pe care vi le trimite sistemul sosesc în ea, nu doar
ecranele. Dacă schimbarea nu poate fi salvată pe cont, vi se spune — pentru că mesajele ar putea
sosi în continuare în limba anterioară.

Și datele urmează: calendarele și selectoarele de dată arată numele de luni și zile ale limbii în
care citiți și **încep săptămâna în ziua în care o începe acea limbă**.

---

Înrudite: [Notificări](notifications.md) · [Coordonate și convertorul](coordinates.md) ·
[Persoane și cluburi](people-and-clubs.md) · [Sincronizare](mobile-sync.md)
