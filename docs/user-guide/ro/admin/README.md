# Administrare

🇬🇧 [English](../../admin/README.md) · 🇷🇴 **Română**

[← Înapoi la ghid](../README.md)

---

Pagini pentru cine deține drepturi de administrare asupra instalării. Sunt tot documentație de
*utilizator* — cum se operează aplicația, nu cum se instalează.

Pentru instalare, actualizare, copii de siguranță și TLS, vedeți
[INSTALL.md](../../../INSTALL.md) și [DEPLOY-SERVER.md](../../../DEPLOY-SERVER.md).

| Pagina | Acoperă |
|---|---|
| [Permisiunile explicate](permissions.md) | Grupuri de permisiuni, reguli, domenii, acordări pe obiect, „de ce vede persoana asta lucrul acela?" |
| [Protecția locațiilor](location-protection.md) | Mecanismul de protecție de la un capăt la altul și fiecare cale pe care o acoperă |
| [Vocabularele care aparțin clubului](vocabularies.md) | Tipuri de documente, scopuri de tură, roluri, modele de raport, relații, reguli de detectare, texte de mesaje, seturi de elemente |
| [Mesagerie și trimitere](messaging.md) | E-mail, SMS, texte de mesaje, politica de autentificare, urmărirea plecării mesajelor |
| [Generarea reliefului](terrain-builds.md) | Transformarea datelor de elevație în terenul pe care îl desenează scena 3D |
| [Operațiuni de întreținere](maintenance.md) | Recuperarea textului, a paginilor de document și a pozițiilor fotografiilor pentru fișiere sosite înainte ca acestea să funcționeze |

---

## „Administrator" nu este un grad

Nu există un singur rol de administrator. **Fiecare pagină de administrare urmează propriul
domeniu**, iar drepturile unei persoane se întâmplă să includă unele dintre ele și altele nu.

Asta înseamnă că bara dumneavoastră poate arăta *Istoric modificări*, dar nu *Mesagerie*, sau
*Relief*, dar nu *Grupuri de permisiuni*. Așa este corect.

Singura excepție reală sunt **Administratorii compleți**: apartenența însăși este acordarea,
decisă înainte de consultarea vreunei reguli. Acel grup nu deține reguli, iar aplicația refuză
orice modificare care ar lăsa niciun administrator complet activ capabil să se autentifice.

## Drepturile de care are nevoie fiecare pagină de administrare

| Pagina | Are nevoie de |
|---|---|
| Istoric modificări | Citire pe domeniul **audit** |
| Grupuri de permisiuni | Drepturi pe domeniul **permissionGroups** |
| Mesagerie, Trimiterea mesajelor | Drepturi pe **settings** |
| Texte mesaje | Drepturi pe **messageTemplates** |
| Relief | Citire pe **terrain** (pornirea unei generări este un drept separat) |
| Seturi de elemente | Drepturi pe **featureSets** |
| Tipuri de documente, scopuri de tură, roluri în tură, modele de raport | **Scriere** pe vocabulare — pentru că autorarea schemei unui tip decide ce poate spune fiecare înregistrare de acel tip |
| Relații între elemente | Administratori compleți |
| Reguli de detectare | Oricine poate crea elemente. Doar *promovarea* unui set la ce moștenește un grup sau instalarea este act de administrator |
| [Operațiuni de întreținere](maintenance.md) | **Rulare** pe domeniul **jobs** (încă fără pagină în interfață — se declanșează prin API) |

## Lucruri de pregătit devreme

1. **Grupurile de permisiuni** — înainte să existe date de stricat.
2. **Grupurile de speologie** și lista de speologi — majoritatea celorlalte lucruri le referă.
3. **Tipurile de documente** și **scopurile de tură** — pentru că decid ce li se cere fiecărui
   document și fiecărui raport de acel fel, iar schimbarea lor ulterioară este mai ușoară decât
   declasarea a o mie de documente.
4. **Mesageria** — sau acceptați că linkurile și codurile ajung în jurnalul serverului în loc să
   fie trimise.
5. **Regula clubului despre când o locație este protejată** — o decizie de politică, nu o setare.
