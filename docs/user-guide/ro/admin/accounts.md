# Conturi

🇬🇧 [English](../../admin/accounts.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite:
[Permisiunile explicate](permissions.md) ·
[Persoane, speologi și cluburi](../features/people-and-clubs.md) ·
[Contul și setările](../features/account-and-settings.md#securitate)

---

**Administrare → Conturi** este registrul tuturor celor care se pot autentifica în instalare. Aici
lași pe cineva să intre, îl ții afară, îl ajuți să intre din nou după ce și-a pierdut al doilea
factor sau îi ștergi contul.

> *Toți cei care se pot autentifica în această instalare. Invită pe cineva pentru a-i deschide un
> cont, blochează un cont pentru a-l ține afară până îl deblochezi, sau șterge unul — persoana
> rămâne în registru și pe turele la care a participat.*

Un cont nu este același lucru cu o persoană din [lista de speologi](../features/people-and-clubs.md#lista-de-speologi):
lista spune *cine face parte din club*, contul spune *cine se poate autentifica și poate acționa*.
Fiecare cont are o înregistrare în listă, dar o înregistrare din listă nu are neapărat un cont.

## Cine poate deschide pagina

Pagina apare în bară, sub **Administrare**, pentru oricine are **Citire** pe domeniul **users**.
Fiecare buton de pe ea urmează propriul drept, iar un buton pentru o acțiune pe care nu o ai pur
și simplu nu este afișat.

| Pentru | Ai nevoie de |
|---|---|
| A vedea registrul și a deschide un rând | **Citire** pe **users** |
| **Invită pe cineva** | **Creare** pe **users** |
| **Trimite din nou invitația**, **Blochează** · **Deblochează**, **Resetează al doilea factor** | **Scriere** pe **users** |
| **Șterge** | **Ștergere** pe **users** |
| A urma numele unui grup de permisiuni către pagina lui | Citire pe **permissionGroups** — fără ea, numele sunt afișate, dar nu sunt linkuri |

Membrii grupului **Administratori deplini** le au pe toate. Cine deschide adresa direct fără
drept vede *„Ai nevoie de drepturi de administrator pentru această pagină."*

## Registrul

Caută după nume sau adresă de e-mail — se caută prenumele, numele de familie, numele afișat,
numele de autentificare și adresa, iar diacriticele nu contează. Lista este împărțită în pagini.

| Coloană | Arată |
|---|---|
| **Nume** | Numele contului; rândul tău este marcat **tu** |
| **Adresă de e-mail** | Adresa cu care se autentifică contul |
| **Stare** | Una sau mai multe dintre etichetele de mai jos |
| **Deschis** | Ziua în care a fost deschis contul |
| **Acțiuni** | Butoanele pe care drepturile tale le permit pe acel rând |

| Eticheta de stare | Înseamnă |
|---|---|
| **Se poate autentifica** | Nu este blocat |
| **Blocat** | Ținut afară — de un administrator sau, pentru câteva minute, după prea multe parole sau coduri greșite |
| **Fără parolă încă** | Invitat, dar titularul nu și-a ales încă parola |
| **Administrator deplin** | Un membru al grupului Administratori deplini care se poate autentifica. Un administrator deplin *blocat* nu primește eticheta, pentru că nu ar putea acționa — dar rămâne unul și redevine unul în clipa în care este deblocat |
| **Al doilea factor activ** | Cel puțin un al doilea factor este activat |

**Deschide un rând** pentru a vedea grupurile de permisiuni din care face parte contul — marcat
**membru** când este numit direct, sau *prin …*, cu numele grupurilor de speologie prin care
ajunge acolo. Un cont care nu face parte din nimic altceva arată *„Doar grupul din care face
parte orice cont."* Apartenența se schimbă pe pagina [grupurilor de permisiuni](permissions.md#grupuri-de-permisiuni),
nu aici.

## Invitarea cuiva

**Invită pe cineva** cere un **Nume** și o **Adresă de e-mail**, iar **Deschide contul** îl
deschide:

> *Pentru această adresă se deschide un cont. Titularul primește un link pentru a-și alege
> singur parola; tu nu o afli.*

Contul se face exact ca la o înregistrare: aceeași regulă că o adresă poate avea un singur cont
(*„Există deja un cont cu această adresă."*), o înregistrare în lista de speologi și grupurile
de permisiuni implicite ale instalării. Invitarea funcționează **fie că înregistrarea liberă este
pornită, fie că nu** — într-o instalare cu înregistrarea închisă, este singura cale de intrare.

**Linkul** se poate folosi o singură dată și ține **24 de ore**. Orice schimbare a datelor de
autentificare ale contului îl încheie mai devreme.

| Ce s-a întâmplat | Ce vezi |
|---|---|
| Invitația a fost trimisă prin e-mail | *„O invitație a fost trimisă la …"* |
| Nu este configurat un server de e-mail sau trimiterea a eșuat | Linkul însuși, într-o fereastră — vezi mai jos |

### Când invitația nu poate fi trimisă prin e-mail

Linkul îți este arătat **o singură dată**, într-o fereastră cu titlul **Predă acest link** pe care
un clic rătăcit în afara ei nu o închide:

> *Copiază-l acum: este afișat doar de data aceasta. Funcționează o singură dată și timp de 24
> ore; după aceea, trimite din nou invitația.*

Copiază-l, închide fereastra cu **L-am copiat** și dă-i-l persoanei chiar tu, pe un canal în care
ai încredere. Serverul nu păstrează linkul, deci unul pierdut nu mai poate fi arătat — trimite în
schimb o invitație nouă.

### Trimiterea din nou a invitației

**Trimite din nou invitația** apare pe rândurile marcate **Fără parolă încă** — pentru un cont în
care nu a intrat încă nimeni, pentru că primul link a expirat, a ajuns în dosarul de spam sau nu
a fost predat niciodată. Trimite un link nou (sau îl arată, ca mai sus).

Este refuzată pentru un cont în care se poate deja intra, cu parolă sau printr-un furnizor de
autentificare asociat: *„Acest cont se poate deja autentifica. Titularul își resetează singur
parola."* Un link care stabilește parola unui astfel de cont ar fi o cale de intrare în contul
altcuiva, nu o invitație.

## Blocarea și deblocarea

**Blochează** acționează imediat, fără confirmare: *„Contul … este blocat."* Un cont blocat nu se
poate autentifica până nu îl deblochează cineva. **Deblochează** îl lasă din nou să intre
(*„… se poate autentifica din nou."*) și șterge numărătoarea parolelor greșite, ca următoarea
parolă tastată greșit să nu-l blocheze din nou pe loc.

Blocarea nu elimină nimic: contul, înregistrarea din listă și tot ce a adăugat rămân cum erau.

## Resetarea unui al doilea factor pierdut

Pentru un membru care și-a pierdut **și** aplicația de autentificare, **și** codurile de
recuperare, și deci nu mai poate intra singur. **Resetează al doilea factor** apare doar pe
conturile cu **Al doilea factor activ** și întreabă întâi:

> *Toți factorii secundari configurați sunt dezactivați — aplicația de autentificare, codurile
> prin e-mail sau SMS — iar codurile de recuperare nu mai funcționează. Titularul este deconectat
> de pe toate dispozitivele și este anunțat prin e-mail. Apoi se poate autentifica doar cu parola
> și poate configura din nou un al doilea factor din setările de securitate.*

Nu există nicio cale de a *activa* un al doilea factor pentru altcineva — doar de a-l dezactiva.
Resetarea este înregistrată în [istoricul de audit](../features/history-and-audit.md), cu
metodele care au fost dezactivate și nimic din ce conțineau.

## Ștergerea unui cont

**Șterge** întreabă întâi, numind contul:

> *Nu se va mai putea autentifica. Înregistrarea din registru și turele la care a participat
> rămân.*

| Ce elimină o ștergere | Ce lasă |
|---|---|
| Contul: persoana nu se mai poate autentifica | Înregistrarea ei din [lista de speologi](../features/people-and-clubs.md#lista-de-speologi), acum fără cont atașat |
| | Turele la care a participat, care continuă să o numească |
| | Tot ce a adăugat, care rămâne la club |

Lista de speologi supraviețuiește intenționat conturilor atașate ei: ștergerea persoanei ar lăsa
fără stăpân evidența a ce a făcut. Scoaterea unei persoane din listă este un act separat — vezi
[Când cineva cere să fie eliminat](../features/people-and-clubs.md#când-cineva-cere-să-fie-eliminat).

Membrii nu își pot șterge singuri contul din setări; li se spune să ceară unui administrator.

## Ce refuză pagina

Butoanele pentru acestea sunt ascunse acolo unde pagina își poate da seama dinainte; unde nu
poate, refuzul o spune în cuvinte.

| Refuzat | De ce, cum o spune pagina |
|---|---|
| Blocarea, ștergerea sau resetarea celui de-al doilea factor pentru **propriul** cont | *„Nu îți poți bloca propriul cont."* · *„Nu îți poți șterge propriul cont."* · *„Propriul al doilea factor îl gestionezi din setările de securitate."* — vezi [Securitate](../features/account-and-settings.md#securitate) |
| Blocarea sau ștergerea **ultimului administrator deplin** care se poate autentifica | *„Acesta este ultimul administrator deplin care se poate autentifica. Fă mai întâi pe altcineva administrator deplin, altfel instalarea nu ar mai avea cine s-o administreze."* |
| Blocarea, deblocarea, ștergerea sau resetarea celui de-al doilea factor pentru **contul unui administrator deplin**, dacă nu ești și tu unul | *„Doar un administrator deplin poate bloca, debloca, șterge sau reseta al doilea factor al contului unui administrator deplin."* — altfel un drept delegat asupra conturilor ar fi o cale de a da jos cel mai înalt loc |
| Resetarea celui de-al doilea factor pentru un cont care nu are niciunul activ | *„Acest cont nu are activ niciun al doilea factor, deci nu este nimic de resetat."* |
| Trimiterea din nou a invitației pentru un cont în care se poate deja intra | *„Acest cont se poate deja autentifica. Titularul își resetează singur parola."* |

## Ce nu face această pagină

- **Nu arată și nu stabilește niciodată o parolă.** Titularul și-o alege prin link; tu nu o afli.
  După ce titularul și-a ales-o, resetarea ei rămâne treaba lui.
- **Nu schimbă permisiuni.** Apartenența la grupurile de permisiuni se editează pe pagina
  [grupurilor de permisiuni](permissions.md#grupuri-de-permisiuni).
- **Nu editează profilul unei persoane.** Numele, adresele și ce pot vedea alții despre ea îi
  aparțin persoanei, în [propriile setări](../features/account-and-settings.md).

---

Înrudite: [Permisiunile explicate](permissions.md) ·
[Persoane, speologi și cluburi](../features/people-and-clubs.md) ·
[Contul și setările](../features/account-and-settings.md) ·
[Mesagerie și trimitere](messaging.md) ·
[← Administrare](README.md)
