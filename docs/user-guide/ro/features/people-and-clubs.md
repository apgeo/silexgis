# Persoane, speologi și cluburi

🇬🇧 [English](../../features/people-and-clubs.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Permisiuni](../admin/permissions.md)

---

Există două noțiuni diferite de „persoană" în aplicație, iar confundarea lor cauzează majoritatea
problemelor pe care le au oamenii aici.

| | |
|---|---|
| **Un cont** | Cineva care se poate autentifica. Are adresă de e-mail, parolă, setări, notificări |
| **Un speolog** | O fișă în lista clubului. Poate avea sau nu un cont |

Un speolog fără cont poate fi numit pe ture, creditat la fotografii, listat ca deținător de
autorizație — dar **nu i se poate acorda acces la nimic**, nu i se poate trimite o notificare și
nu poate răspunde la o invitație. Trebuie contactat altfel.

---

## Lista de speologi

**Persoane → Speologi.**

| Câmp | |
|---|---|
| **Nume** · **E-mail** · **Telefon** | |
| **Nume scurt** | *„Cum i se spune în echipă — numele cu care este scris un jurnal de suprafață."* Apare la [turele publicate](live-tracking.md#ce-vede-cineva-care-urmărește) în locul numelui întreg și este recunoscut întreg când o [foaie de rapoarte](live-tracking.md#importul-unei-foi-de-rapoarte) numește pe cineva prin el. Gol, se folosește numele întreg |
| **Grupuri de speologie** | De ce cluburi aparțin |
| **Observații** | *„Vizibile doar celor care administrează lista."* |

Marchează **Fără cont** acolo unde nu există autentificare în spatele fișei.

**Cluburile din care face parte cineva se pot schimba din fișa persoanei.** *Editare* arată
grupurile de speologie și te lasă să adaugi sau să scoți oricare dintre ele; persoana intră
ca membru obișnuit, iar cine conduce un club se stabilește în continuare pe
[pagina clubului](#membri-și-roluri). De
îndată ce alegerea diferă de ce este salvat, formularul spune ce înseamnă: *„Apartenența la un
grup este cea care dă contului unei persoane drepturile grupului: membrii citesc și modifică ce
împarte grupul și văd poziția exactă a peșterilor pe care acesta le protejează, și sunt anunțați
când sunt adăugați. Scoaterea cuiva îi retrage imediat aceste drepturi. Nimic din ce s-a
înregistrat despre persoană — ture, ore, rapoarte — nu se schimbă."* Fiecare club este o scriere
separată, care cere dreptul de a modifica *acel* club; unde îl ai pentru unele și nu pentru
altele, persoana este salvată, cluburile pe care le poți modifica sunt schimbate, iar
formularul le numește pe cele rămase neschimbate.

Cineva ajunge în listă pe una din patru căi: îl adaugă aici cine ține lista; un cont își
primește fișa proprie când este creat; unei foi importate cu ture vechi i se poate spune să
creeze persoanele pe care le numește; iar **un nume scris pe o tură sau pe
[lista unei tabere](camps.md#cine-a-fost), sub care nu este înregistrat nimeni, îl adaugă**.
Un nume scris sub care cineva *este* înregistrat înseamnă acea persoană — fișa mai veche, dacă
două îl poartă — așa că același nume scris de două ori este un singur speolog, nu doi. Un nume
scris în două feluri înseamnă doi, și de aici vin cele mai multe duplicate.

### Contopirea duplicatelor

Listele acumulează duplicate — *Ion Popescu*, *I. Popescu*, *Popescu Ion*. **Unifică** pliază o
fișă în alta: turele și grupurile lor trec dincolo, iar duplicatul este eliminat.

Nu poți pur și simplu să ștergi pe cineva numit pe ture: ștergerea ar rescrie tacit
evidența cine a fost în subteran. Aplicația refuză — iar pentru un duplicat, răspunsul este
unificarea.

### Când cineva cere să fie eliminat

O persoană care nu este un duplicat și care cere ca ce ține aplicația despre ea să fie eliminat
nu poate fi unificată cu altcineva. De aceea refuzul spune ce o ține. Apasă coșul de pe rândul
ei și, dacă încă apare în ture, se deschide un dialog — ***nume* nu poate fi eliminat(ă) încă** —
care enumeră **fiecare tură pe care o poți deschide**, ca legătură, cu ce ține persoana acolo:

- **Pe listă** — tura o numește. Deschide tura și scoate persoana de pe lista ei.
- **Rapoarte de urmărire: 3** — [jurnalul de urmărire](live-tracking.md) al turei are rapoarte
  despre ea, numărându-le și pe cele deja scoase din jurnal și păstrate. Acolo unde poți edita
  tura și urmărirea ei a fost pornită, alături apare **Șterge rapoartele ei din această tură: 3**.

**Șterge rapoartele ei din această tură** distruge toate rapoartele pe care tura le are despre
persoană — cele din jurnal și cele de la *Rapoarte scoase* — și întreabă mai întâi, spunând exact
asta. Acțiunea nu poate fi anulată și nu rămâne nimic de pus înapoi. Reluarea turei se schimbă,
la fel și o pagină publicată a ei. **Ce rămâne:** numele persoanei pe lista turei, până când
editezi tura, și **Istoricul** turei, care consemnează că rapoartele au fost șterse și arată în
continuare ce spunea fiecare.

**Ce arată apoi o pagină publicată.** O pagină publicată a turei — cea urmărită acum sau
reluarea unei ture trecute a peșterii — enumeră lista turei și desenează fiecare persoană după
rapoartele ei. După ștergere, persoana rămâne în lista echipei de acolo, sub numele sau eticheta
pe care pagina o folosea deja, fără nimic raportat despre ea: niciun loc, niciun *A intrat*,
niciun *A ieșit* și niciun punct în vreun moment al reluării. O pagină pe care cineva o are
deschisă arată asta la următoarea reîmprospătare, la fel și una încadrată pe alt site. Ca să
dispară și numele de pe pagină, editezi tura și scoți persoana de pe lista ei — ceea ce o
urmărire în curs refuză pentru cineva cu rapoarte și permite după ce rapoartele au dispărut.
Nimic din toate acestea nu ajunge la ce a salvat deja cineva: o captură de ecran sau un film
făcut din reluare.

Când persoana este ținută și de ceva ce nu ți se arată — o tură pe care nu o poți deschide, o
tură ștearsă care își așteaptă termenul de păstrare, mai multe ture decât cuprinde lista —
dialogul spune doar atât: *Persoana este ținută și de ceva ce nu apare aici… Întreabă un
administrator.* Nu spune niciodată câte, nici care.

Când nimic din listă nu o mai ține, în dialog apare **Elimină persoana**; nimic nu se șterge
până nu apeși. **Unifică un duplicat în loc** rămâne acolo ca cealaltă cale. O persoană de pe
[lista unei tabere](camps.md#cine-a-fost) este refuzată separat, cu o propoziție care spune asta.

---

## Grupuri de speologie

**Persoane → Grupuri de speologie.** Un club, un grup sau o organizație.

Grupurile contează din trei motive:

1. **Vizibilitate.** Vizibilitatea *grup de speologie* înseamnă „membrii grupului de care este
   legată înregistrarea".
2. **Domeniu de permisiune.** O regulă poate fi limitată la *conținutul unui grup de speologie*.
   Un grup nou pornește cu o astfel de regulă pentru membrii săi — citirea, corectarea și crearea
   conținutului propriu al grupului — de aceea un membru poate
   [înregistra o tură care aparține grupului](trips.md#când-dreptul-de-a-înregistra-ture-vine-de-la-club)
   fără să dețină vreun drept mai larg.
3. **Anunțuri.** Vezi mai jos.

### Membri și roluri

Membrii dețin un rol în grup: **Membru · Administrator · Proprietar**. Administrezi lista din
pagina grupului — sau pui o persoană într-un club ori o scoți de acolo din
[fișa ei din lista de speologi](#lista-de-speologi).

> **A avea voie să editezi lista unui club nu este același lucru cu a avea voie să scrii
> tuturor celor de pe ea.** Cele două drepturi se acordă separat. Fondatorul unui club poate
> scrie propriului club din start.

### Anunțarea unui club

**Anunță** trimite un rând unui grup de speologie. Fiecare membru cu cont îl primește **în lista
proprie, după alegerea proprie: poștă sau în aplicație** — nu ca o listă de mail de pe care nimeni
nu poate ieși.

Înainte de trimitere:

- ți se spune **la câți oameni ajunge** (membrii fără cont nu sunt numărați, iar tu nu
  primești niciodată propriul anunț),
- **confirmarea este un pas separat**: *„Pleacă acum către persoanele numărate mai sus. Nu
  poate fi retras."*

Un mesaj către două sute de oameni nu este niciodată un singur clic neatent. La un club mare
notificările se scriu în fundal, și o spune.

Dacă nimeni de pe listă nu are cont, îți spune că nu are cui să spună.

> **Mesajele text costă bani.** O instalare poate permite ca anunțurile să circule pe un canal
> care taxează. Este oprit dacă nu este pornit anume, o persoană nu le poate trimite în șir, iar
> cheltuiala unei zile are un plafon stabilit de un administrator. Acel plafon numără
> **segmentele** pe care le facturează un operator, nu mesajele — o singură diacritică face
> aceeași formulare să coste două dintre ele, deci un plafon înseamnă **pe jumătate mai puține
> mesaje către membrii care citesc în română decât către cei care citesc în engleză**. Vezi
> [Mesagerie](../admin/messaging.md).

---

## Conturi

Conturile sunt deschise de un administrator — **Invită pe cineva** la
[Administrare → Conturi](../admin/accounts.md), care îi trimite persoanei un link pentru a-și
alege singură parola — dacă nu a fost pornită înregistrarea deschisă. În ambele cazuri contul
primește o înregistrare în lista de speologi.

**Conturile nu se șterg din pagina de setări.** *„Ce ai adăugat rămâne la club, așa că
adresează-te unui administrator."* Un administrator care șterge un cont îi închide persoanei accesul;
**înregistrarea ei din registru și turele la care a participat rămân**, pentru că ștergerea lor
ar lăsa orfană evidența a ce a făcut acea persoană.

Îți poți **exporta propriile date**: o copie a profilului, adreselor, setărilor, filtrelor salvate
și o listă a ce ai adăugat. Se pregătește în fundal și se descarcă atunci când este gata.

Vezi [Contul și setările](account-and-settings.md).

---

## Cine ce poate vedea despre o persoană

Câmpurile de profil poartă **audiența proprie**, individual — pictograma mică de lângă fiecare
câmp: *Doar eu · Grupurile mele · Membrii autentificați*. Asta include adresele și
punctele de pe hartă atașate lor.

Nu există niciun mod de a cere aplicației turele, orele sau locurile unei persoane numind-o.
Statisticile despre o persoană sunt calculate **peste ce poți citi**, iar ecranul o spune.
Vezi [Măsurători și statistici](measurements-and-statistics.md#statistici-din-ture).

---

Înrudite: [Permisiuni](../admin/permissions.md) ·
[Contul și setările](account-and-settings.md) · [Notificări](notifications.md)
