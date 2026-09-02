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

### Contopirea duplicatelor

Listele acumulează duplicate — *Ion Popescu*, *I. Popescu*, *Popescu Ion*. **Contopește** pliază o
fișă în alta: turele și grupurile lor trec dincolo, iar duplicatul este eliminat.

Nu puteți pur și simplu să ștergeți pe cineva numit pe ture. Aplicația refuză și vă spune să
contopiți în schimb fișa duplicat — ceea ce este răspunsul corect, pentru că ștergerea ar
rescrie tacit evidența cine a fost în subteran.

---

## Grupuri de speologie

**Persoane → Grupuri de speologie.** Un club, un grup sau o organizație.

Grupurile contează din trei motive:

1. **Vizibilitate.** Vizibilitatea *grup de speologie* înseamnă „membrii grupului de care este
   legată înregistrarea".
2. **Domeniu de permisiune.** O regulă poate fi limitată la *conținutul unui grup de speologie*.
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
