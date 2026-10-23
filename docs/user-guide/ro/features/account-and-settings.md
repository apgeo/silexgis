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

> **Numele afișat** apare lângă tot ce adaugi. Fără el apari sub un nume generat.
> Stabilește-l devreme.

### Despre tine

**Descriere** (o scurtă prezentare) · **Telefon** · **Club de speologie** · **Poză de profil**

Poza de profil poate fi încărcată (sub 5 MB) sau aleasă din setul inclus — liliac, cască, lampă
frontală, coardă, carst, stalactită, busolă, sifon.

Numărul de telefon se schimbă din **Securitate**, pentru că i se trimite întâi un cod prin SMS.

**Clubul de speologie** se alege din lista grupurilor de speologie ale instalării; tastarea o
restrânge, fără să conteze literele mari sau diacriticele. Dacă clubul nu este încă în listă,
aceasta oferă **Creează «nume»** — alegerea lui adaugă clubul la grupurile de speologie și îl
selectează, fără să părăsești formularul. Un nume care diferă de un club existent doar prin litere
mari sau punctuație nu este un club nou: ți se oferă cel existent. Oferta li se face tuturor celor
care pot adăuga grupuri de speologie — oricărui cont, dacă instalarea nu a retras acest drept.

### Adrese

Unde poți fi găsit. Fiecare adresă are o **etichetă**, **țară**, **oraș**, **strada și restul**
și, opțional, **un punct pe hartă** pe care îl alegi sau îl tastezi.

### Audiențe per câmp

**Pictograma mică de lângă un câmp stabilește cine îl poate vedea**: *Doar eu · Grupurile mele ·
Membrii autentificați*. Este per câmp, nu o singură setare pentru tot profilul — deci
numele poate fi vizibil tuturor, iar adresa nu.

---

## Cont

**Nume de utilizator** — te autentifici întotdeauna cu adresa de e-mail, deci schimbarea lui
schimbă doar felul în care ești listat.

**Conturile nu se șterg de aici.** *„Ce ai adăugat rămâne la club, așa că adresează-te unui
administrator."*

**Exportă-ți datele** — o copie a profilului, adreselor, setărilor, filtrelor salvate și o listă
a ce ai adăugat.
Se pune la coadă, se pregătește în fundal, apoi se descarcă.

**Flux de calendar** — se afișează doar când un administrator a pornit fluxurile. Creează adresa
secretă la care se abonează un calendar de pe telefon sau de pe calculator, le listează pe cele pe
care le ai după nume și dată și revocă una. Vezi [Abonarea la calendar](calendar-feed.md).

---

## E-mail

Adresa ta și dacă este **confirmată**. Dacă nu, **Trimite un link de confirmare**.

**Schimbarea adresei**: dai adresa nouă, un link pleacă spre ea și **nu se schimbă nimic până nu
deschizi acel link**. Până atunci continui să te autentifici cu cea curentă. Pagina arată
schimbarea în așteptare și te lasă să **retrimiți** (cu o scurtă pauză) sau să **anulezi
schimbarea**.

---

## Notificări

Vezi [Notificări](notifications.md) — are pagina ei.

---

## Securitate

### Parolă

Schimbă parola cu care te autentifici: **Parola actuală**, apoi cea nouă de două ori. O parolă
are nevoie de cel puțin 10 caractere.

> *„Schimbarea parolei te deconectează peste tot, inclusiv aici. Apoi te autentifici din nou cu
> cea nouă."*

Un cont care se autentifică doar printr-un furnizor extern nu are parolă aici, iar cardul o
spune. Primești un e-mail de fiecare dată când parola este schimbată sau resetată — o singură
dată — astfel încât o schimbare pe care nu ai făcut-o să nu treacă neobservată.

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

Configurarea unei aplicații de autentificare: scanează pătratul QR, sau adaugă cheia manual, apoi
tastează codul pe care îl arată.

**Codurile de recuperare** sunt afișate **o singură dată**. Păstrează-le atunci. Pagina îți spune
câte au mai rămas.

Ai pierdut și aplicația de autentificare, și codurile de recuperare? Un administrator care
gestionează conturile îți poate **reseta al doilea factor**: toate metodele sunt dezactivate, ești
deconectat peste tot și anunțat prin e-mail, apoi te autentifici doar cu parola și configurezi
din nou un al doilea factor. Vezi [Conturi](../admin/accounts.md#resetarea-unui-al-doilea-factor-pierdut).

Prea multe coduri greșite blochează contul pentru o vreme.

### Număr de telefon

Adaugă sau schimbă numărul — ți se trimite un cod prin SMS și confirmi. Introdu-l în
formă internațională (`+40712345678`). Este folosit **doar pentru coduri de autentificare**.

Ștergerea numărului oprește odată cu el și codurile prin SMS, și o spune. **Dacă codurile prin SMS
sunt singurul factor secundar**, pagina întreabă a doua oară — *„Ștergi singurul factor
secundar?"* — pentru că autentificarea în doi pași se oprește atunci și contul rămâne protejat doar
de parolă; primești un e-mail care o spune. Dacă instalarea nu are o poartă SMS, codurile prin SMS
pur și simplu nu sunt disponibile, iar pagina spune de ce.

### Pierderea unui dispozitiv

> **Schimbarea sau resetarea parolei revocă fiecare jeton și fiecare autorizație de pe contul
> tău — fiecare client, fiecare dispozitiv, inclusiv cel pe care îl folosești.**

Acesta este răspunsul la un telefon pierdut sau furat, și este **o singură acțiune la care ajunge
orice speolog**, fără să aștepte un administrator. Ești deconectat peste tot și te
reconectezi pe dispozitivele pe care încă le ai.

Funcționează așa pentru că persoana care acționează nu-și poate lista propriile sesiuni și nu
poate numi dispozitivul care i-a fost luat — deci singurul lucru onest de revocat este toate.

Vezi [Telefon și sincronizare offline](mobile-sync.md#autentificarea-unui-dispozitiv-și-retragerea-ei).

### Conturi asociate

Dacă instalarea oferă furnizori externi, te poți autentifica cu **Google, GitHub sau alt
furnizor OIDC** pe lângă parolă. Asociază și dezasociază de aici.

Un furnizor dovedește cine ești la el și nimic mai mult: **cu autentificarea în doi pași
pornită, ți se cere în continuare codul** după autentificarea prin el.

---

## Accesibilitate

### Aspect (urmează contul pe orice dispozitiv)

| | |
|---|---|
| **Temă** | Ca sistemul · Luminoasă · Întunecată |
| **Densitate** | Confortabilă · Compactă |
| **Reduce animațiile** | Oprește animațiile și deplasarea hărții |
| **Hartă de ansamblu pe hartă** | Micul cadru care arată unde se află vederea hărții într-o zonă mai largă |
| **Limbă** | English · Română — aceeași alegere ca în comutatorul din bara de sus; vezi [Limba](#limba) |

Ambele teme folosesc culorile proprii ale bibliotecii de interfață. Când te deplasezi într-o
pagină cu tastatura, un inel marchează controlul care are focusul, în tema luminoasă ca și în
cea întunecată; prima apăsare pe Tab oferă **Sari la conținutul principal**, iar pe pagina hărții
**Sari la hartă**. Apasă **?** pentru lista scurtăturilor de tastatură.

### Doar în acest browser

*„Acestea rămân pe acest dispozitiv — un telefon și o stație de lucru cer răspunsuri diferite."*

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
| **Partajează filtrele noi cu** | Doar eu · Membrii unuia dintre grupurile tale de speologie · Toți cei autentificați |

De aici **pornește** un filtru pe care îl salvezi: fiecare fereastră de salvare îl preselectează,
iar tu poți alege altfel de fiecare dată. Este **Doar eu** până îl schimbi și îți
urmează contul pe orice dispozitiv. Partajarea le dă celorlalți doar întrebarea — fiecăruia i se
răspunde cu ce are el însuși dreptul să vadă. Dacă grupul de speologie ales aici nu mai este unul
cu care poți partaja, filtrele noi pornesc private până alegi din nou. Vezi
[Filtre salvate](search-and-filters.md#filtre-salvate).

---

## Coordonate

Cum sunt scrise pozițiile oriunde sunt afișate sau copiate. *„Aceste setări îți urmează contul."*

| | |
|---|---|
| **Coordonate afișate ca** | Formatul în care este scrisă fiecare poziție — grade zecimale, grade și minute, grade–minute–secunde, UTM, MGRS, Stereo 70, caroiajele elvețiene sau Plus Code |
| **Listate și la „Alte formate"** | Formatele în care mai poate fi deschisă o poziție afișată în detaliu — o intrare selectată, intrările unei peșteri —, fiecare cu butonul ei de copiere |
| **Setările fiecărui format** | De pildă zona UTM păstrată lângă marginea unei zone, cu o notă despre cât te poți baza pe fiecare format |

Un punct de exemplu arată pe loc fiecare format ales, pe măsură ce le schimbi. Un format care
nu are referință pentru un loc — un caroiaj național în afara țării lui — este omis acolo. Tot
ce privește formatele, pozițiile protejate și scrierea unei poziții în oricare dintre ele este la
[Coordonate și convertorul](coordinates.md).

---

## Sincronizare

Pentru aplicația de telefon. Vezi
[Telefon și sincronizare offline](mobile-sync.md).

---

## Avansat

Numere care hotărăsc cât cere aplicația de la calculatorul din fața ta. Se păstrează
**doar în acest browser** și nu urmează contul — un telefon și o stație de lucru cer răspunsuri
diferite.

### Pereții peșterilor în vizualizarea 3D

Trei numere limitează vizualizarea 3D atunci când desenează
[pereții tuturor peșterilor din vedere](3d-view.md#pereții-tuturor-peșterilor-din-vedere):

| | |
|---|---|
| **Arată de la zoom-ul** | Nivelul de zoom de la care acest mod desenează ceva; sub el scena îți cere să apropii vederea. De la 1 la 22 |
| **Peșteri cel mult** | Pentru câte peșteri se desenează pereții deodată, cele mai apropiate de mijlocul vederii întâi |
| **Megaocteți cel mult** | Cât model de pereți se descarcă și se păstrează pentru ele. Un browser are nevoie de aproximativ dublu în memoria grafică |

**Un câmp gol urmează valoarea implicită a instalării**, afișată cu gri în locul lui — 14, 12 și
64 MB, dacă administratorul nu le-a schimbat. Un număr scris de tine înlocuiește
valoarea implicită în acest browser: mărește-l pe un calculator cu o placă grafică adevărată,
micșorează-l pe un telefon.

Ultimele două se opresc la un plafon stabilit de administrator — 60 de peșteri și 512 MB, dacă nu
a fost schimbat — iar pagina spune care este. Dacă plafonul este coborât ulterior, un număr mai
mare pe care îl aveai păstrat este adus la el.

**Folosește valorile implicite ale instalării** din acest card golește toate cele trei câmpuri.

Numerele sunt în vigoare data viitoare când deschizi vizualizarea 3D. O vizualizare 3D deja
deschisă în altă fereastră le păstrează pe cele vechi până când este reîncărcată.

**Pereții peșterii selectate** are un singur număr: **Întreabă peste (MB)**. Selectarea unei
peșteri în vizualizarea 3D îi descarcă pereții imediat, dacă nu sunt mai mari decât atât; altfel
vizualizarea spune cât de mari sunt și așteaptă până alegi să-i descarci. Gol urmează valoarea
implicită a instalării (50 MB, dacă nu a fost schimbată); **0** întreabă înaintea fiecărei
descărcări. Butonul lui propriu **Folosește valorile implicite ale instalării** îl golește.

### Drumuirile de pe hartă

Două numere limitează drumuirile pe care le desenează harta plană. Sunt aceleași două pe care
le arată lista de straturi a hărții cât timp stratul **Drumuiri peșteri** este afișat —
vezi [Spațiul de lucru al hărții](map-workspace.md#detaliul-drumuirilor) — iar un număr
stabilit în oricare dintre cele două locuri este cel în vigoare în amândouă.

| | |
|---|---|
| **Detaliu de la nivelul** | Nivelul de zoom de la care apare detaliul topografic complet, inclusiv radialele; sub el vezi conturul galeriilor. De la 1 la 22 |
| **Limită de linii** | Câte linii poate conține o vedere. Peste ea, drumuirile rămase sunt reținute, iar harta spune câte |

**Un câmp gol urmează valoarea implicită a instalării**, afișată cu gri în locul lui — 18 și
25.000, dacă administratorul nu le-a schimbat. Adu detaliul mai devreme și mărește limita pe
un calculator rapid; împinge-le pe amândouă în sens invers pe un telefon.

Limita de linii se oprește la un plafon stabilit de administrator — 100.000, dacă nu a fost
schimbat — iar pagina spune care este. Dacă plafonul este coborât ulterior, un număr mai mare pe
care îl aveai păstrat este adus la el.

**Folosește valorile implicite ale instalării** din acest card golește aceste două câmpuri și
lasă neatinse cele trei ale pereților; butonul din cardul pereților face invers. Fiecare card
dă înapoi doar numerele lui, astfel că renunțarea la o încercare într-unul nu aruncă ce ai
reglat pentru celălalt.

Acestea două limitează doar harta plană: vizualizarea 3D desenează drumuirile după numerele
instalării.

---

## Ce îți urmează contul și ce rămâne în acest browser

Cea mai mare parte din ce aranjezi este păstrată **cu contul**, așa că o regăsești când te
autentifici pe alt calculator:

- aspectul — tema, densitatea, reducerea animațiilor, harta de ansamblu — și limba;
- formatele de coordonate și cu cine sunt partajate filtrele salvate noi;
- felul în care este aranjat spațiul de lucru: aranjările panourilor, aranjările cu nume,
  panourile laterale, tipurile de obiecte fixate.

Câteva lucruri rămân **doar în acest browser**, pentru că un telefon și o stație de lucru cer
răspunsuri diferite: unde se deschide aplicația, ascunderea controalelor de pe hartă și toate
numerele de la [Avansat](#avansat). O aranjare cu nume pentru care preferințele salvate ale
contului nu mai aveau loc este păstrată tot în acest browser, iar meniul aranjărilor o marchează
**Doar în acest browser** — șterge o aranjare pe care n-o mai folosești și ea ajunge în cont
odată cu acea schimbare.

O schimbare făcută chiar înainte de a închide pagina, sau într-o
[fereastră desprinsă](saved-views-and-windows.md#panouri-desprinse), este scrisă și ea în cont.
**Alt membru care se autentifică în același browser pornește de la valorile implicite**, nu de la
aranjarea ta.

---

## Deconectarea

**Deconectare**, din meniul contului, îți încheie autentificarea pe server, nu doar în pagină:
deschiderea din nou a aplicației îți cere adresa de e-mail și parola. Nimic din ce este al
tău nu îi este dat celui care se autentifică după aceea în acel browser.

Pentru a te deconecta **peste tot** — un telefon pierdut, un calculator comun la care nu mai
ajungi — schimbă parola; vezi [Pierderea unui dispozitiv](#pierderea-unui-dispozitiv).

---

## Limba

Comutatorul de limbă este în bara de sus, iar aceeași alegere este și la
[Accesibilitate](#accesibilitate). Aplicația vine cu engleză și română.

**Limba urmează contul**, deci mesajele pe care ți le trimite sistemul sosesc în ea, nu doar
ecranele. Dacă schimbarea nu poate fi salvată pe cont, ți se spune — pentru că mesajele ar putea
sosi în continuare în limba anterioară.

Și datele urmează: calendarele și selectoarele de dată arată numele de luni și zile ale limbii în
care citești și **încep săptămâna în ziua în care o începe acea limbă**.

---

Înrudite: [Notificări](notifications.md) · [Coordonate și convertorul](coordinates.md) ·
[Persoane și cluburi](people-and-clubs.md) · [Sincronizare](mobile-sync.md)
