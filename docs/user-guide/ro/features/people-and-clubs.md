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
| **Grupuri de speologie** | De ce cluburi aparțin |
| **Note** | *„Doar cei care țin lista le pot citi."* |

Marcați **Fără cont** acolo unde nu există autentificare în spatele fișei.

**Cluburile din care face parte cineva se pot schimba din fișa persoanei.** *Editare* arată
grupurile de speologie și vă lasă să adăugați sau să scoateți oricare dintre ele; persoana intră
ca membru obișnuit, iar cine conduce un club se stabilește în continuare pe pagina clubului. De
îndată ce alegerea diferă de ce este salvat, formularul spune ce înseamnă: *„Apartenența la un
grup este cea care dă contului unei persoane drepturile grupului: membrii citesc și modifică ce
împarte grupul și văd poziția exactă a peșterilor pe care acesta le protejează, și sunt anunțați
când sunt adăugați. Scoaterea cuiva îi retrage imediat aceste drepturi. Nimic din ce s-a
înregistrat despre persoană — ture, ore, rapoarte — nu se schimbă."* Fiecare club este o scriere
separată, care cere dreptul de a modifica *acel* club; unde îl aveți pentru unele și nu pentru
altele, persoana este salvată, cluburile pe care le puteți modifica sunt schimbate, iar
formularul le numește pe cele rămase neschimbate.

Cineva ajunge în listă pe una din patru căi: îl adaugă aici cine ține lista; un cont își
primește fișa proprie când este creat; unei foi importate cu ture vechi i se poate spune să
creeze persoanele pe care le numește; iar **un nume scris pe o tură sau pe
[lista unei tabere](camps.md#cine-a-fost), sub care nu este înregistrat nimeni, îl adaugă**.
Un nume scris sub care cineva *este* înregistrat înseamnă acea persoană — fișa mai veche, dacă
două îl poartă — așa că același nume scris de două ori este un singur speolog, nu doi. Un nume
scris în două feluri înseamnă doi, și de aici vin cele mai multe duplicate.

### Contopirea duplicatelor

Listele acumulează duplicate — *Ion Popescu*, *I. Popescu*, *Popescu Ion*. **Contopește** pliază o
fișă în alta: turele și grupurile lor trec dincolo, iar duplicatul este eliminat.

Nu puteți pur și simplu să ștergeți pe cineva numit pe ture: ștergerea ar rescrie tacit
evidența cine a fost în subteran. Aplicația refuză — iar pentru un duplicat, răspunsul este
unificarea.

### Când cineva cere să fie eliminat

O persoană care nu este un duplicat și care cere ca ce ține aplicația despre ea să fie eliminat
nu poate fi unificată cu altcineva. De aceea refuzul spune ce o ține. Apăsați coșul de pe rândul
ei și, dacă încă apare în ture, se deschide un dialog — ***nume* nu poate fi eliminat(ă) încă** —
care enumeră **fiecare tură pe care o puteți deschide**, ca legătură, cu ce ține persoana acolo:

- **Pe listă** — tura o numește. Deschideți tura și scoateți persoana de pe lista ei.
- **Rapoarte de urmărire: 3** — [jurnalul de urmărire](live-tracking.md) al turei are rapoarte
  despre ea, numărându-le și pe cele deja scoase din jurnal și păstrate. Acolo unde puteți edita
  tura și urmărirea ei a fost pornită, alături apare **Șterge rapoartele ei din această tură: 3**.

**Șterge rapoartele ei din această tură** distruge toate rapoartele pe care tura le are despre
persoană — cele din jurnal și cele de la *Rapoarte scoase* — și întreabă mai întâi, spunând exact
asta. Acțiunea nu poate fi anulată și nu rămâne nimic de pus înapoi. Reluarea turei se schimbă,
la fel și o pagină publicată a ei. **Ce rămâne:** numele persoanei pe lista turei, până când
editați tura, și **Istoricul** turei, care consemnează că rapoartele au fost șterse și arată în
continuare ce spunea fiecare.

**Ce arată apoi o pagină publicată.** O pagină publicată a turei — cea urmărită acum sau
reluarea unei ture trecute a peșterii — enumeră lista turei și desenează fiecare persoană după
rapoartele ei. După ștergere, persoana rămâne în lista echipei de acolo, sub numele sau eticheta
pe care pagina o folosea deja, fără nimic raportat despre ea: niciun loc, niciun *A intrat*,
niciun *A ieșit* și niciun punct în vreun moment al reluării. O pagină pe care cineva o are
deschisă arată asta la următoarea reîmprospătare, la fel și una încadrată pe alt site. Ca să
dispară și numele de pe pagină, editați tura și scoateți persoana de pe lista ei — ceea ce o
urmărire în curs refuză pentru cineva cu rapoarte și permite după ce rapoartele au dispărut.
Nimic din toate acestea nu ajunge la ce a salvat deja cineva: o captură de ecran sau un film
făcut din reluare.

Când persoana este ținută și de ceva ce nu vi se arată — o tură pe care nu o puteți deschide, o
tură ștearsă care își așteaptă termenul de păstrare, mai multe ture decât cuprinde lista —
dialogul spune doar atât: *Persoana este ținută și de ceva ce nu apare aici… Întrebați un
administrator.* Nu spune niciodată câte, nici care.

Când nimic din listă nu o mai ține, în dialog apare **Eliminați persoana**; nimic nu se șterge
până nu apăsați. **Unificați un duplicat în loc** rămâne acolo ca cealaltă cale. O persoană de pe
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
3. **Anunțuri.** Vedeți mai jos.

### Membri și roluri

Membrii dețin un rol în grup: **Membru · Administrator · Proprietar**. Administrați lista din
pagina grupului.

> **A avea voie să editați lista unui club nu este același lucru cu a avea voie să scrieți
> tuturor celor de pe ea.** Cele două drepturi se acordă separat. Fondatorul unui club poate
> scrie propriului club din start.

### Anunțarea unui club

**Anunță** trimite un rând unui grup de speologie. Fiecare membru cu cont îl primește **în lista
proprie, după alegerea proprie: poștă sau în aplicație** — nu ca o listă de mail de pe care nimeni
nu poate ieși.

Înainte de trimitere:

- vi se spune **la câți oameni ajunge** (membrii fără cont nu sunt numărați, iar dumneavoastră nu
  primiți niciodată propriul anunț),
- **confirmarea este un pas separat**: *„Acesta pleacă acum către oamenii numărați mai sus. Nu
  poate fi luat înapoi."*

Un mesaj către două sute de oameni nu este niciodată un singur clic neatent. La un club mare
notificările se scriu în fundal, și o spune.

Dacă nimeni de pe listă nu are cont, vă spune că nu are cui să spună.

> **Mesajele text costă bani.** O instalare poate permite ca anunțurile să circule pe un canal
> care taxează. Este oprit dacă nu este pornit anume, o persoană nu le poate trimite în șir, iar
> cheltuiala unei zile are un plafon stabilit de un administrator. Acel plafon numără
> **segmentele** pe care le facturează un operator, nu mesajele — o singură diacritică face
> aceeași formulare să coste două dintre ele, deci un plafon înseamnă **pe jumătate mai puține
> mesaje către membrii care citesc în română decât către cei care citesc în engleză**. Vedeți
> [Mesagerie](../admin/messaging.md).

---

## Conturi

Conturile sunt create de cine administrează instalarea, dacă nu a fost pornită înregistrarea
deschisă.

**Conturile nu se șterg din pagina de setări.** *„Tot ce ați adăugat rămâne la club, așa că
întrebați un administrator."* — pentru că ștergerea unui cont ar lăsa orfană evidența a ce a făcut
acea persoană.

Vă puteți **exporta propriile date**: o copie a profilului, adreselor, setărilor și o listă a ce
ați adăugat. Se pregătește în fundal și se descarcă atunci când este gata.

Vedeți [Contul și setările](account-and-settings.md).

---

## Cine ce poate vedea despre o persoană

Câmpurile de profil poartă **audiența proprie**, individual — pictograma mică de lângă fiecare
câmp: *Doar eu · Grupurile mele de speologie · Membri autentificați*. Asta include adresele și
punctele de pe hartă atașate lor.

Nu există niciun mod de a cere aplicației turele, orele sau locurile unei persoane numind-o.
Statisticile despre o persoană sunt calculate **peste ce puteți citi**, iar ecranul o spune.
Vedeți [Măsurători și statistici](measurements-and-statistics.md#statistici-din-ture).

---

Înrudite: [Permisiuni](../admin/permissions.md) ·
[Contul și setările](account-and-settings.md) · [Notificări](notifications.md)
