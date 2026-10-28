# Istoric și modificări

🇬🇧 [English](../../features/history-and-audit.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Permisiuni](../admin/permissions.md)

---

Trei mecanisme separate, pentru trei întrebări separate.

| Întrebare | Mecanism |
|---|---|
| *Ce s-a schimbat la această înregistrare și pot să pun la loc?* | **Istoricul**, pe înregistrarea însăși |
| *Ce s-a întâmplat prin instalare?* | **Istoricul de modificări** |
| *Cine a luat o copie a acestui document?* | **Istoricul de acces** |

---

## Istoricul (pe înregistrare)

Fiecare peșteră, intrare, obiect, tură, drumuire, model topografic, atașament, etichetă,
participant, tabără, invitație și eveniment poartă o secțiune **Istoric**.

Arată, per modificare: **când**, **cine** (sau *Sistem*), **acțiunea** (Creat · Modificat ·
Șters), **de unde a venit modificarea** și câmpurile care s-au schimbat, cu valorile de dinainte
și de după.

### De unde a venit o modificare

Fiecare intrare poartă o etichetă care îi numește sursa: *Modificat în browser*, *Import din
fișier*, *Încărcare de pe telefon*, *Import de arhivă de pe telefon*, *Restaurat din istoric*,
*Anulare*, *Decizie de revizuire*, *Sarcină în fundal* sau *Tură convertită dintr-o înregistrare*.
Istoricul scris înainte ca sursele să fie înregistrate nu are etichetă.

Mai multe înregistrări scrise de o singură acțiune — o încărcare de pe telefon, o restaurare — se
arată ca **o singură intrare**, cu numărul de modificări din ea și cu fiecare modificare listată
dedesubt. O intrare care aparține unui import are o legătură **Vezi acest import**, care deschide
importul în Geodate → Importuri.

### Restaurarea

Două acțiuni:

- **Restaurează această valoare** — pune un câmp la loc,
- **Restaurează toate valorile dinaintea acestei modificări** — readuce toată înregistrarea
  la starea anterioară.

Valoarea unei peșteri, a unei intrări sau a unui obiect este pusă la loc **de server**, ca o
editare obișnuită: se aplică aceleași verificări ca la orice editare făcută de mână, iar
restaurarea este înregistrată în istoric ca *Restaurat din istoric*. Dacă unele dintre valorile
cerute nu au putut fi puse la loc, intrarea spune care și de ce: valoarea îți este ascunsă pentru
că locația este protejată; poți modifica obiectul, dar nu poziția, altitudinea sau calitatea lui;
forma anterioară nu a fost păstrată, pentru că modificarea a fost înregistrată înainte ca formele
mari să fie salvate integral; sau istoricul nu poate pune la loc acest fel de valoare. Celelalte
sunt restaurate oricum.

Aceasta este ce face sigură întreținerea acelorași înregistrări de către mai mulți oameni. O
editare în masă greșită este o neplăcere, nu un dezastru.

### Valori protejate în istoric

O valoare care face parte dintr-o locație protejată se citește **„Valoare ascunsă (locație
protejată)"**, în loc să fie expusă.

Contează mai mult decât pare: un istoric de modificări este exact ușa laterală prin care scapă o
coordonată reținută, și este închisă.

### Formele mari sunt rezumate

O formă mică — punctul unei intrări, conturul unui obiect — este înregistrată integral, astfel
încât un punct mutat poate fi citit și pus la loc. Una mare se arată în istoric ca rezumat: **„Geometrie cu n puncte (prea mare pentru a fi
înregistrată integral)"**. Rezumatul nu conține nicio coordonată, deci nu poate localiza o peșteră
protejată. Pentru un obiect modificat după ce au început să se păstreze copii integrale, serverul
ține forma anterioară întreagă lângă rezumat, așa că ea poate fi pusă la loc; pentru istoricul mai
vechi și pentru liniile unei topografii, care se reconstruiesc din fișierul ei, nu poate, iar
restaurarea spune acest lucru.

### Câmpuri cu nume lizibile

Istoricul numește câmpurile în cuvinte, nu în coloane: Nume, Descriere, Locație, Altitudine,
Calitatea poziției, Cea mai apropiată adresă, Număr cadastral, Note despre locație, Peșteră asociată,
Vizibilitate, Legendă și așa mai departe.

---

## Istoricul de modificări

**Administrare → Istoric** (pagina se numește *Istoric modificări*). O vedere peste toată instalarea, pentru cine deține dreptul
asupra domeniului de audit.

| Coloană | |
|---|---|
| **Când** | |
| **Utilizator** | |
| **Acțiune** | |
| **Entitate** | Filtrabilă după tip — de ex. `Cave` |
| **Id entitate** | |

Aceasta este vederea *ce s-a întâmplat aici*, nu vederea *ce s-a întâmplat cu această
înregistrare*.

---

## Istoricul de acces — cine a citit un document

Două suprafețe, cu reguli intenționat diferite.

**Propriul istoric de lectură** — documentele din care ai luat o copie, cele mai noi întâi. Nu
are nevoie de nicio permisiune în afară de a fi tu.

**Cine a citit acest document** — pentru **proprietarul** documentului sau pentru cineva cu
dreptul asupra istoricului de modificări.

> **A avea voie să citești un document nu face ca cititorii lui să-ți aparțină.** Un membru de club
> ar putea altfel afla care alți membri s-au uitat la o anumită topografie, ceea ce este un fapt
> despre *ei* și pe care nu au fost niciodată de acord să-l publice.

Proprietarul documentului este inclus pentru că răspunde de ce a pus înăuntru și poate întreba în
mod rezonabil cine a luat o copie.

**Nimeni nu poate obține istoricul de lectură al altcuiva numindu-l.** Vederea per document este
singurul mod în care lectura unei persoane este vizibilă alteia, și este limitată la un document
pe care aceasta îl administrează deja.

Un document pe care nu ai voie să-l citești răspunde *„nu a fost găsit"*, nu *„interzis"* —
pentru că un istoric care ar răspunde altfel pentru un document care există i-ar divulga
existența.

---

## Ce se înregistrează despre acțiunile făcute în numele altora

Acolo unde cineva acționează pentru altcineva — un administrator care repune manual o notificare
abandonată, un organizator care răspunde la o invitație pentru cineva care a sunat — **actul este
înregistrat pe seama celui care l-a făcut**, nu a celui pentru care a fost făcut.

---

Înrudite: [Permisiuni](../admin/permissions.md) ·
[Protecția locațiilor](../admin/location-protection.md)
