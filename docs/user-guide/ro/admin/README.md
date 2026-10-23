# Administrare

🇬🇧 [English](../../admin/README.md) · 🇷🇴 **Română**

[← Înapoi la ghid](../README.md)

---

Pagini pentru cine deține drepturi de administrare asupra instalării. Sunt tot documentație de
*utilizator* — cum se operează aplicația, nu cum se instalează.

Pentru instalare, actualizare, copii de siguranță și TLS, vezi
[INSTALL.md](../../../INSTALL.md) și [DEPLOY-SERVER.md](../../../DEPLOY-SERVER.md).

| Pagina | Acoperă |
|---|---|
| [Conturi](accounts.md) | Cine se poate autentifica: invitarea cuiva, blocarea unui cont, resetarea unui al doilea factor pierdut, ștergerea unui cont |
| [Permisiunile explicate](permissions.md) | Grupuri de permisiuni, reguli, domenii, acordări pe obiect, „de ce vede persoana asta lucrul acela?" |
| [Protecția locațiilor](location-protection.md) | Mecanismul de protecție de la un capăt la altul și fiecare cale pe care o acoperă |
| [Vocabularele care aparțin clubului](vocabularies.md) | Tipuri de documente, tipuri de obiecte, scopuri de tură, roluri în tură, roluri în tabără, modele de raport, relații, reguli de detectare, texte de mesaje, seturi de obiecte |
| [Mesagerie și trimitere](messaging.md) | E-mail, SMS, texte de mesaje, politica de autentificare, urmărirea plecării mesajelor |
| [Generarea reliefului](terrain-builds.md) | Transformarea datelor de elevație în terenul pe care îl desenează scena 3D |
| [Operațiuni de întreținere](maintenance.md) | Recuperarea textului, a paginilor de document și a pozițiilor fotografiilor pentru fișiere sosite înainte ca acestea să funcționeze; unde se înregistrează erorile văzute în browserele membrilor |
| [Ture publicate](../features/live-tracking.md#tot-ce-este-publicat-pentru-administratorii-deplini) | Toate linkurile de urmărire date de instalare și ce deschide fiecare; înlocuirea unui link, retragerea publicării unei ture sau a tot ce este publicat și avertismentul „peșteră protejată în apropiere?" |

---

## „Administrator" nu este un grad

Nu există un singur rol de administrator. **Fiecare pagină de administrare urmează propriul
domeniu**, iar drepturile unei persoane se întâmplă să includă unele dintre ele și altele nu.

Asta înseamnă că bara ta poate arăta *Istoric*, dar nu *Mesagerie*, sau
*Relief*, dar nu *Grupuri de permisiuni*. Așa este corect.

Singura excepție reală sunt **Administratorii deplini**: apartenența însăși este acordarea,
decisă înainte de consultarea vreunei reguli. Acel grup nu deține reguli, iar aplicația refuză
orice modificare care ar lăsa niciun administrator deplin activ capabil să se autentifice.

## Drepturile de care are nevoie fiecare pagină de administrare

| Pagina | Are nevoie de |
|---|---|
| Istoric | Citire pe domeniul **audit** |
| [Conturi](accounts.md#cine-poate-deschide-pagina) | Citire pe **users** pentru a vedea registrul. Invitarea cere **Creare**; trimiterea din nou a unei invitații, blocarea, deblocarea și resetarea celui de-al doilea factor cer **Scriere**; ștergerea cere **Ștergere** |
| Grupuri de permisiuni | Drepturi pe domeniul **permissionGroups** |
| Mesagerie, Trimiterea mesajelor | Drepturi pe **settings** |
| Texte mesaje | Drepturi pe **messageTemplates** |
| Relief | Citire pe **terrain** (pornirea unei generări este un drept separat) |
| Seturi de obiecte | Drepturi pe **featureSets** |
| Tipuri de documente, tipuri de obiecte, scopuri de tură, roluri în tură, roluri în tabără, modele de raport | **Scriere** pe vocabulare — pentru că autorarea schemei unui tip decide ce poate spune fiecare înregistrare de acel tip |
| Relații între obiecte | Administratori deplini |
| [Ture publicate](../features/live-tracking.md#tot-ce-este-publicat-pentru-administratorii-deplini) | Administratori deplini (pagina îi numește *administratori deplini*) — arată linkurile tuturor turelor, indiferent de regulile de acces ale fiecărei ture, și le poate retrage pe toate |
| Reguli de detectare | Oricine poate crea obiecte. Doar *promovarea* unui set la ce moștenește un grup sau instalarea este act de administrator |
| [Operațiuni de întreținere](maintenance.md) | **Rulare** pe domeniul **jobs** (încă fără pagină în interfață — se declanșează prin API) |

## Conturi

**Administrare → Conturi** este registrul tuturor celor care se pot autentifica: invitarea cuiva,
trimiterea din nou a unei invitații, blocarea și deblocarea, resetarea unui al doilea factor
pierdut și ștergerea unui cont — a cărui persoană rămâne în lista de speologi și pe turele la
care a participat. Are o pagină proprie: **[Conturi](accounts.md)**.

---

## Lucruri de pregătit devreme

1. **Grupurile de permisiuni** — înainte să existe date de stricat.
2. **Grupurile de speologie** și lista de speologi — majoritatea celorlalte lucruri le referă.
3. **Tipurile de documente** și **scopurile de tură** — pentru că decid ce li se cere fiecărui
   document și fiecărui raport de acel fel, iar schimbarea lor ulterioară este mai ușoară decât
   declasarea a o mie de documente.
4. **Mesageria** — sau acceptă că linkurile și codurile ajung în jurnalul serverului în loc să
   fie trimise.
5. **Regula clubului despre când o locație este protejată** — o decizie de politică, nu o setare.
