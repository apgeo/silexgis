# Încărcarea topografiilor din TopoDroid

🇬🇧 [English](../../features/topodroid-uploads.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Topografii, drumuiri și modele 3D](surveys-and-models.md) ·
[Contul și setările](account-and-settings.md) ·
[Protecția locațiilor](../admin/location-protection.md)

---

**TopoDroid** este aplicația Android cu care topografiază mulți speologi. Poate trimite o topografie
direct în SilexGIS, din fereastra de export, așa că fișierele ajung pe pagina peșterii fără un
laptop la mijloc. Pagina aceasta este partea ta: cheia pe care o ține telefonul, locul unde urmărești
ce se întâmplă cu o încărcare și linkul pe care îl poți da cuiva fără cont.

Pașii de pe telefon (unde stă intrarea *SilexGIS* în fereastra de export și în setările
TopoDroid) sunt descriși în documentația TopoDroid pentru această funcție; nimic de aici nu depinde
de ei în afară de adresa acestei instalări și de cheie.

---

## O cheie de acces pentru telefon

Telefonul nu ține parola ta. Ține o **cheie de acces**: un text lung, aleator, care poate face un
singur lucru — să încarce topografii în numele tău.

**Setări → Chei de acces.**

1. Scrie un **nume** pentru cheie — una pe dispozitiv e obiceiul util, pentru că un telefon pierdut
   înseamnă atunci o singură revocare și nimic altceva („TopoDroid pe telefonul meu").
2. Alege **cât timp e valabilă**. Alegerile vin de la instalare, care stabilește și cea mai lungă
   viață a unei chei.
3. Apasă **Creează cheia**.

Cheia se afișează **o singură dată**, cu un buton de copiere și un avertisment. Lipește-o imediat în
setările SilexGIS din TopoDroid. După ce închizi mesajul, nimeni — nici tu — nu mai poate citi
cheia; lista păstrează doar primele ei caractere, care servesc la deosebirea cheilor tale. Dacă s-a
pierdut, revocă-o și fă alta.

Lista arată pentru fiecare cheie numele, cum începe, pentru ce e, când a fost făcută, când a fost
folosită ultima oară, când expiră și **starea**: *Activă*, *Expirată* sau *Revocată*.

### Ce nu face niciodată o cheie

- **Nu poate citi restul contului tău**, nu schimbă nimic în afara încărcărilor de topografii și nu
  poate face sau lista alte chei. Cine găsește o cheie poate încărca topografii în numele tău și atât.
- **Nu poate deschide această pagină.** Cheile de acces se gestionează doar când ești autentificat
  cu parola; de pe un dispozitiv care ține o cheie, pagina refuză.
- **Nu se poate recupera.** Serverul păstrează o amprentă a ei, nu textul.

### Revocarea

**Revocă** lângă cheie, apoi confirmă. Telefonul care o ține este refuzat la următoarea încărcare.
O cheie revocată rămâne în listă, ca să vezi ce a fost retras.

---

## Ce poate face telefonul cu încărcările tale

**Setări → Încărcări de ridicări.**

| Setare | Ce face |
|---|---|
| **Produce întotdeauna** | Rezultatele pe care le vrei din fiecare topografie încărcată, în plus față de ce cere telefonul. Alegerile sunt rezultatele pe care instalarea le poate produce. Dacă nu bifezi nimic și telefonul nu cere nimic, instalarea produce setul ei implicit, pe care pagina îl numește |
| **Legături publice pentru încărcările mele** | Dacă fiecare încărcare a ta poate primi un link care arată rezultatele fără autentificare (vezi mai jos). Implicit, pornit |

Amândouă sunt doar ale tale și se aplică încărcărilor de acum încolo.

---

## Unde ajunge o încărcare

O topografie trimisă din TopoDroid se păstrează la o **peșteră**. Dacă telefonul numește o peșteră,
topografia merge acolo (trebuie să ai voie să scrii în ea). Dacă nu numește niciuna, serverul face o
peșteră cu titlul topografiei.

O peșteră poate ține **multe topografii**, iar niciuna nu este „topografia". De fiecare dată când
aceeași topografie este trimisă din nou, devine o **versiune** nouă a ei, cu momentul trimiterii;
versiunile mai vechi se păstrează.

Pe **pagina peșterii**, un card **Topografii** listează topografiile sosite de pe un dispozitiv:

| Coloană | |
|---|---|
| **Topografie** | Titlul ei |
| **De la** | Instrumentul care a trimis-o |
| **Versiuni** | De câte ori a fost trimisă |
| **Cea mai nouă versiune** | Când a sosit ultima |

Deschide o topografie ca să-i vezi versiunile, cele mai noi primele, fiecare cu starea ei și cu cine a
trimis-o. Deschide o versiune ca să vezi fișierele trimise (arhiva TopoDroid și eventualele fișiere
Therion, fiecare cu descărcare) și ce s-a făcut din ele. **Pagina de stare** deschide pagina
descrisă mai jos.

Cardul apare doar la peșterile care au avut o încărcare și **doar pentru cei care văd exact poziția
peșterii**: pentru ceilalți secțiunea pur și simplu lipsește, ca la o peșteră fără încărcări.
Cardul *Surse topografice* și lista de modele de mai jos sunt întregul peșterii; cardul Topografii
spune din ce încărcare vine fiecare piesă și nu le repetă.

---

## Pagina de stare a unei încărcări

Fiecare încărcare are o pagină proprie, la care ajungi din **Pagina de stare** de pe peșteră, din
notificarea primită la sfârșitul rulării sau din linkul arătat de telefon.

- **O bară de progres** cu procentul și **etapele** rulării, fiecare cu starea ei — *Arhivare*,
  *Citirea arhivei*, *Pregătirea compilării*, *Compilare*, *Salvarea rezultatelor*, *Citirea
  rezultatelor*. O etapă de care rularea nu a avut nevoie este marcată *Nu era nevoie*, iar o rulare
  oprită devreme lasă etapele următoare vizibil neatinse.
- **Ultimul lucru spus de server** și, dacă rularea a eșuat, **eroarea** cu codul ei. Codul merită
  citat când ceri ajutor.
- **Jurnalul** — afișat doar celor care pot edita peștera.
- **Fișierele trimise**, fiecare cu descărcare.
- **Ce s-a produs**, cu descărcare pentru fiecare și, unde SilexGIS are o pagină pentru el, cu o
  cale de a-l deschide.
- **Linkul public**, cu *Copiază*, *Fă un link nou* și *Retrage*.
- **Rulează din nou**, pentru cei care pot edita peștera, după ce rularea s-a încheiat.

Pagina întreabă din nou la câteva secunde cât timp rularea merge și se oprește când se termină.

**O instalare fără compilator** primește și citește totuși fișierele: rularea se termină la 100 % și
spune într-o propoziție că nu s-a compilat nimic. Este o notă la un succes, nu un avertisment. Dacă
o instalare compilează sau nu este decizia administratorului.

### Încă nu există „șterge această încărcare"

O încărcare terminată nu poate fi retrasă din această pagină: fișierele aduse sunt arhivate la
peșteră în clipa în care sosesc, iar ștergerea lor se face acolo unde sunt listate — *Surse
topografice* ale peșterii.

---

## Linkul public

Când comutatorul din **Setări → Încărcări de ridicări** e pornit și instalarea permite, o încărcare
terminată poate primi un link de forma `…/p/survey/…`. Oricine îl are îl poate deschide **fără cont**
și vede:

- titlul topografiei, **numele** peșterii și momentul trimiterii;
- aceeași bară de progres și aceleași etape;
- rezultatele care pot fi deschise sau descărcate.

### Ce nu arată niciodată linkul public

- **Arhiva TopoDroid sau fișierele sursă Therion.**
- **Jurnalul**, echipa, numele stațiilor.
- **Unde se află peștera.** Pagina numește peștera și nu spune nimic despre poziția ei.

### Ce este linkul și ce nu este

- **Este o adresă secretă.** Tratează-l ca pe o partajare fără parolă: cine îl are poate vedea
  rezultatele. **Retrage** linkul pe pagina de stare și nu mai funcționează pe loc; **Fă un link
  nou** îl înlocuiește, iar adresa veche nu mai funcționează nici ea.
- **O peșteră cu locația protejată nu primește niciun link**, iar una care devine protejată după ce
  s-a făcut linkul nu mai răspunde pe loc. Când nu există link, pagina de stare spune de ce, atunci când
  serverul dă un motiv — comutatorul tău, setarea instalării, lipsa dreptului de a partaja peștera
  sau protecția peșterii — și nu oferă butonul „Fă un link" când motivul este protecția peșterii.
  **Deschide** de lângă link arată ce vede un vizitator fără cont.
- Un link greșit, retras sau care nu mai e permis spune doar „Acest link nu funcționează". Din
  răspuns nu se poate deduce care dintre cele trei a fost.

---

## Ce poate merge prost și ce înseamnă

- **„Cheile de acces se gestionează doar când ești autentificat cu parola"** — ai deschis pagina de
  pe un dispozitiv care ține o cheie. Autentifică-te obișnuit.
- **Telefonul este refuzat** — cheia e revocată, expirată sau a fost copiată incomplet. Fă una nouă.
- **Cardul Topografii lipsește de la o peșteră despre care știi că are încărcări** — poate nu vezi
  exact poziția peșterii. Vezi [Protecția locațiilor](../admin/location-protection.md).
- **„Încărcarea nu a fost găsită sau nu ai voie să o vezi"** — adresa e greșită sau
  peștera nu îți este deschisă. Cele două nu se deosebesc intenționat.

---

[← Referință](README.md) · [Topografii, drumuiri și modele 3D](surveys-and-models.md) ·
[Contul și setările](account-and-settings.md)
