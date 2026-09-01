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

Fiecare peșteră, intrare, element, tură, poligonație, model topografic, atașament, etichetă,
participant, tabără, invitație și eveniment poartă o secțiune **Istoric**.

Arată, per modificare: **când**, **cine** (sau *Sistem*), **acțiunea** (Creat · Actualizat ·
Șters) și câmpurile care s-au schimbat, cu valorile de dinainte și de după.

### Restaurarea

Două acțiuni:

- **Restaurează această valoare** — pune un câmp la loc,
- **Restaurează toate valorile de dinainte de această modificare** — readuce toată înregistrarea
  la starea anterioară.

Aceasta este ce face sigură întreținerea acelorași înregistrări de către mai mulți oameni. O
editare în masă greșită este o neplăcere, nu un dezastru.

### Valori protejate în istoric

O valoare care face parte dintr-o locație protejată se citește **„Valoare ascunsă (locație
protejată)"**, în loc să fie expusă.

Contează mai mult decât pare: un istoric de modificări este exact ușa laterală prin care scapă o
coordonată reținută, și este închisă.

### Câmpuri cu nume lizibile

Istoricul numește câmpurile în cuvinte, nu în coloane: Nume, Descriere, Locație, Altitudine,
Calitatea poziției, Cea mai apropiată adresă, Număr cadastral, Note de localizare, Peșteră legată,
Vizibilitate, Legendă și așa mai departe.

---

## Istoricul de modificări

**Administrare → Istoric modificări.** O vedere peste toată instalarea, pentru cine deține dreptul
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

**Propriul istoric de lectură** — documentele din care ați luat o copie, cele mai noi întâi. Nu
are nevoie de nicio permisiune în afară de a fi dumneavoastră.

**Cine a citit acest document** — pentru **proprietarul** documentului sau pentru cineva cu
dreptul asupra istoricului de modificări.

> **A avea voie să citiți un document nu face ca cititorii lui să vă aparțină.** Un membru de club
> ar putea altfel afla care alți membri s-au uitat la o anumită topografie, ceea ce este un fapt
> despre *ei* și pe care nu au fost niciodată de acord să-l publice.

Proprietarul documentului este inclus pentru că răspunde de ce a pus înăuntru și poate întreba în
mod rezonabil cine a luat o copie.

**Nimeni nu poate obține istoricul de lectură al altcuiva numindu-l.** Vederea per document este
singurul mod în care lectura unei persoane este vizibilă alteia, și este limitată la un document
pe care aceasta îl administrează deja.

Un document pe care nu aveți voie să-l citiți răspunde *„nu a fost găsit"*, nu *„interzis"* —
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
