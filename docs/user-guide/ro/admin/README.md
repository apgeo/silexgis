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
| [Conturi](#conturi) | Cine se poate autentifica: invitarea cuiva, blocarea unui cont, resetarea unui al doilea factor pierdut, ștergerea unui cont |
| [Permisiunile explicate](permissions.md) | Grupuri de permisiuni, reguli, domenii, acordări pe obiect, „de ce vede persoana asta lucrul acela?" |
| [Protecția locațiilor](location-protection.md) | Mecanismul de protecție de la un capăt la altul și fiecare cale pe care o acoperă |
| [Vocabularele care aparțin clubului](vocabularies.md) | Tipuri de documente, tipuri de obiecte, scopuri de tură, roluri în tură, roluri în tabără, modele de raport, relații, reguli de detectare, texte de mesaje, seturi de elemente |
| [Mesagerie și trimitere](messaging.md) | E-mail, SMS, texte de mesaje, politica de autentificare, urmărirea plecării mesajelor |
| [Generarea reliefului](terrain-builds.md) | Transformarea datelor de elevație în terenul pe care îl desenează scena 3D |
| [Operațiuni de întreținere](maintenance.md) | Recuperarea textului, a paginilor de document și a pozițiilor fotografiilor pentru fișiere sosite înainte ca acestea să funcționeze; unde se înregistrează erorile văzute în browserele membrilor |
| [Ture publicate](../features/live-tracking.md#tot-ce-este-publicat-pentru-administratorii-deplini) | Toate linkurile de urmărire date de instalare și ce deschide fiecare; înlocuirea unui link, retragerea publicării unei ture sau a tot ce este publicat și avertismentul „peșteră protejată în apropiere?" |

---

## „Administrator" nu este un grad

Nu există un singur rol de administrator. **Fiecare pagină de administrare urmează propriul
domeniu**, iar drepturile unei persoane se întâmplă să includă unele dintre ele și altele nu.

Asta înseamnă că bara dumneavoastră poate arăta *Istoric*, dar nu *Mesagerie*, sau
*Relief*, dar nu *Grupuri de permisiuni*. Așa este corect.

Singura excepție reală sunt **Administratorii compleți**: apartenența însăși este acordarea,
decisă înainte de consultarea vreunei reguli. Acel grup nu deține reguli, iar aplicația refuză
orice modificare care ar lăsa niciun administrator complet activ capabil să se autentifice.

## Drepturile de care are nevoie fiecare pagină de administrare

| Pagina | Are nevoie de |
|---|---|
| Istoric | Citire pe domeniul **audit** |
| [Conturi](#conturi) | Citire pe **users** pentru a vedea registrul. Invitarea, blocarea, deblocarea și resetarea celui de-al doilea factor cer **Scriere**; ștergerea cere **Ștergere** |
| Grupuri de permisiuni | Drepturi pe domeniul **permissionGroups** |
| Mesagerie, Trimiterea mesajelor | Drepturi pe **settings** |
| Texte mesaje | Drepturi pe **messageTemplates** |
| Relief | Citire pe **terrain** (pornirea unei generări este un drept separat) |
| Seturi de elemente | Drepturi pe **featureSets** |
| Tipuri de documente, tipuri de obiecte, scopuri de tură, roluri în tură, roluri în tabără, modele de raport | **Scriere** pe vocabulare — pentru că autorarea schemei unui tip decide ce poate spune fiecare înregistrare de acel tip |
| Relații între elemente | Administratori compleți |
| [Ture publicate](../features/live-tracking.md#tot-ce-este-publicat-pentru-administratorii-deplini) | Administratori compleți (pagina îi numește *administratori deplini*) — arată linkurile tuturor turelor, indiferent de regulile de acces ale fiecărei ture, și le poate retrage pe toate |
| Reguli de detectare | Oricine poate crea elemente. Doar *promovarea* unui set la ce moștenește un grup sau instalarea este act de administrator |
| [Operațiuni de întreținere](maintenance.md) | **Rulare** pe domeniul **jobs** (încă fără pagină în interfață — se declanșează prin API) |

## Conturi

**Administrare → Conturi** este registrul tuturor celor care se pot autentifica. Îl căutați după
nume sau adresă de e-mail; fiecare rând spune dacă contul **Se poate autentifica**, este
**Blocat**, este **Fără parolă încă**, are **Al doilea factor activ** sau aparține unui
**Administrator complet**. Deschideți un rând pentru a vedea grupurile de permisiuni din care
face parte contul — numit direct sau ajuns acolo printr-unul dintre grupurile lui de speologie.
Apartenența se schimbă pe pagina [grupurilor de permisiuni](permissions.md#grupuri-de-permisiuni),
nu aici.

| Acțiune | Ce se întâmplă |
|---|---|
| **Invitați pe cineva** | Deschide un cont pentru un nume și o adresă de e-mail. Titularul primește un link pentru a-și alege singur parola — dumneavoastră nu o aflați. Contul primește o înregistrare în lista de speologi și grupurile de permisiuni implicite ale instalării, exact ca la o înregistrare, iar invitarea funcționează fie că înregistrarea liberă este pornită, fie că nu |
| **Trimite din nou invitația** | Pentru un cont în care nu a intrat încă nimeni. Refuzată pentru unul în care se poate deja intra — titularul își resetează singur parola |
| **Blochează** · **Deblochează** | Acționează imediat. Un cont blocat nu se poate autentifica până nu este deblocat |
| **Resetează al doilea factor** | Pentru un membru care și-a pierdut *și* aplicația de autentificare, *și* codurile de recuperare. Toți factorii secundari configurați sunt dezactivați, iar codurile de recuperare nu mai funcționează; titularul este deconectat de pe toate dispozitivele și anunțat prin e-mail, apoi se autentifică doar cu parola și își configurează din nou un al doilea factor |
| **Șterge** | Întreabă întâi, numind contul. Persoana nu se mai poate autentifica, dar **înregistrarea ei din registru și turele la care a participat rămân** |

**Când invitația nu poate fi trimisă prin e-mail** — nu există server de e-mail sau trimiterea a
eșuat — linkul vă este arătat **o singură dată**, într-o fereastră pe care un clic rătăcit nu o
închide: *„Copiați-l acum: este afișat doar de data aceasta."* Funcționează o singură dată și
timp de 24 de ore; după aceea, trimiteți din nou invitația. Predați-l chiar dumneavoastră, pe un
canal în care aveți încredere.

Ce refuză pagina, spunând-o în cuvinte:

- blocarea, ștergerea sau resetarea celui de-al doilea factor pentru **propriul** cont — al
  doilea factor propriu se administrează din [setările de securitate](../features/account-and-settings.md#securitate);
- blocarea sau ștergerea **ultimului administrator complet** care se poate autentifica;
- acțiunea asupra **contului unui administrator complet** dacă nu sunteți și dumneavoastră unul —
  altfel un drept delegat asupra conturilor ar fi o cale de a-i lua celui mai înalt loc al doilea
  factor.

---

## Lucruri de pregătit devreme

1. **Grupurile de permisiuni** — înainte să existe date de stricat.
2. **Grupurile de speologie** și lista de speologi — majoritatea celorlalte lucruri le referă.
3. **Tipurile de documente** și **scopurile de tură** — pentru că decid ce li se cere fiecărui
   document și fiecărui raport de acel fel, iar schimbarea lor ulterioară este mai ușoară decât
   declasarea a o mie de documente.
4. **Mesageria** — sau acceptați că linkurile și codurile ajung în jurnalul serverului în loc să
   fie trimise.
5. **Regula clubului despre când o locație este protejată** — o decizie de politică, nu o setare.
