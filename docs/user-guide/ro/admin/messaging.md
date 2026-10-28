# Mesagerie și trimitere

🇬🇧 [English](../../admin/messaging.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite:
[Notificări](../features/notifications.md)

---

**Administrare → Mesagerie** configurează unde ajung e-mailurile și mesajele text, ce cere
autentificarea și alte câteva răspunsuri valabile pentru toată instalarea. **Administrare →
Trimiterea mesajelor** arată dacă ceva din toate acestea chiar funcționează.

> Acestea pot fi stabilite și din mediul de instalare, iar pagina o spune. Stabilirea lor aici
> este calea care nu are nevoie de repornire.

Pagina are unsprezece file:

| Fila | Ce hotărăște | Mai jos |
|---|---|---|
| **E-mail** | Serverul de e-mail | [E-mail](#e-mail) |
| **SMS** | Poarta SMS | [Mesaje text](#mesaje-text) |
| **Politică de autentificare** | Adresele confirmate, ce factori secundari se oferă, cât trăiește un cod | [Politica de autentificare](#politica-de-autentificare) |
| **Protecția locației** | Două comutatoare despre cât de departe ajung locurile protejate și datele clubului | [Protecția locațiilor](#protecția-locațiilor) |
| **Import vectorial** | Dacă un import poate sări peste verificare și cum se caută duplicatele | [Import vectorial](#import-vectorial) |
| **Sincronizare cu telefonul** | Ce primește aplicația de pe telefon și cât așteaptă fișierele nefolosite | [Sincronizare cu telefonul](#sincronizare-cu-telefonul) |
| **Notificări** | Cât se păstrează notificările și cât poate costa un anunț | [Păstrare](#păstrare) · [Garda de cheltuială](#garda-de-cheltuială) |
| **Fototeci** | Oprirea și repornirea folosirii unei fototeci pe care instalarea o are deja | [Fototeci](#fototeci) |
| **Încărcări de topografii** | Dacă o peșteră făcută de o încărcare de topografie ia primul model al acelei încărcări drept formă | [Încărcări de topografii](#încărcări-de-topografii) |
| **Spațiul de lucru** | Cu ce aranjare a spațiului de lucru al hărții pornește un cont nou | [Spațiul de lucru](#spațiul-de-lucru) |
| **Hărți în documente** | Ce fundaluri de hartă pot fi copiate într-un document | [Hărți în documente](#hărți-în-documente) |
| **Hartă de bază proprie** | Harta de bază vectorială a instalării: ce se servește și descărcarea unei regiuni | [Hartă de bază proprie](#hartă-de-bază-proprie) |

---

## E-mail

| Câmp | |
|---|---|
| **Activat** | |
| **Server** · **Port** | |
| **Securitatea conexiunii** | Automată · TLS la conectare (465) · Niciuna (text simplu) |
| **Utilizator** · **Parolă** | Pagina spune dacă este stocată o parolă; lasă gol ca s-o păstrezi |
| **Adresă expeditor** · **Nume expeditor** · **Adresă de răspuns** | |
| **Timp de așteptare** | Secunde |
| **Acceptă un certificat invalid** | *Doar pentru un releu intern cu certificat autosemnat.* Elimină protecția de pe traseu |

Starea este declarată limpede: **„E-mailul este configurat"**, sau:

> *„Niciun server de e-mail configurat. Mesajele sunt scrise în jurnalul serverului în loc să fie
> trimise."*

**Nu configura nimic și aplicația funcționează în continuare.** Linkurile de autentificare, cele
de confirmare și codurile ajung în jurnalul serverului. Acesta este un mod legitim de a rula o
instalare mică — atâta timp cât cine are nevoie de un link poate citi jurnalul.

## Mesaje text

Orice poartă SMS care vorbește HTTP. Aceeași formă: configurată, sau *„Mesajele text sunt scrise
în jurnalul serverului în loc să fie trimise."*

### Garda de cheltuială

O instalare poate permite ca **anunțurile** să circule prin mesaje text. Este **oprită dacă nu
este pornită anume**, o persoană nu le poate trimite în șir, iar cheltuiala unei zile are un
**plafon** pe care îl stabilește un administrator și îl poate vedea pe pagina de trimitere.

> Plafonul numără **segmentele pe care le facturează un operator**, nu mesajele. **O singură
> diacritică face aceeași formulare să coste două dintre ele** — deci un plafon înseamnă cam **pe
> jumătate mai puține mesaje către membrii care citesc în română decât către cei care citesc în
> engleză**.

Stabilește plafonul având asta în minte și spune-i cui scrie anunțuri.

## Politica de autentificare

Ce cere autentificarea — inclusiv ce metode de autentificare în doi pași oferă această instalare.

| | |
|---|---|
| **Trimite un mesaj de confirmare la crearea unui cont** | |
| **Cere o adresă confirmată pentru autentificare** | Un cont neconfirmat este refuzat la autentificare și i se oferă un nou mesaj de confirmare. *„Nu are efect până când nu este configurat un server de e-mail — altfel nimeni nu ar putea confirma."* |
| **Permite aplicații de autentificare** · **Permite coduri prin e-mail** · **Permite coduri prin SMS** | Codurile prin e-mail au nevoie de un server de e-mail, cele prin SMS de o poartă SMS. *„Cea mai slabă dintre cele trei: un număr poate fi mutat pe altă cartelă convingând operatorul de telefonie."* |
| **Valabilitatea codului (minute)** · **Pauză între coduri (secunde)** | Pentru codurile prin e-mail și prin SMS |

Metodele neactivate aici sunt marcate *„Nu este activată pe această instalare."* pe paginile de securitate
ale membrilor.

Aceleași coduri sunt cerute și după o autentificare printr-un furnizor extern: un furnizor
dovedește cine este cineva la furnizor, nu că deține al doilea factor al contului.

## Protecția locațiilor

Două comutatoare, descrise amândouă integral la [Protecția locațiilor](location-protection.md):

**„Arată atașamentele peșterilor protejate tuturor celor care le pot citi."**
([detalii](location-protection.md#setarea-de-asociere-a-atașamentelor))

Documentele atașate unei peșteri protejate sunt **întotdeauna servite** — nu sunt niciodată ascunse
sau scoase. Ce controlează acest comutator este dacă se divulgă *asocierea* dintre document și acea
peșteră anume.

Pornirea lui **nu dezvăluie niciodată o poziție**: urmărirea linkului tot dă vederea protejată.

**„Permite membrilor să se aboneze la calendarul lor din exterior."**
([detalii](location-protection.md#setarea-fluxului-de-calendar)) Oprit implicit. Pornit, fiecare
membru poate crea adrese secrete pe care un calendar de pe telefon sau de pe calculator le
interoghează fără autentificare; oprirea lui din nou oprește toate adresele deja date. Vezi
[Abonarea la calendar](../features/calendar-feed.md).

## Import vectorial

Un fișier importat este [verificat](../features/geodata.md#importuri) înainte să se creeze ceva.
Aici acest pas poate fi eliminat și tot aici se stabilește cât de departe caută implicit
detectarea duplicatelor.

| | |
|---|---|
| **Permite unui utilizator de încredere să creeze obiecte fără verificare** | Dezactivat implicit. Activarea nu lărgește cine poate crea — cel care importă are în continuare nevoie de dreptul de a crea obiecte — ci doar elimină pasul intermediar |
| **Caută ceva deja existent pe o rază de** | Raza de pornire pentru o verificare nouă; cel care importă o poate schimba. Se măsoară doar față de obiectele a căror poziție exactă o poate vedea cel care verifică |
| **Numele se consideră identice peste** | Cât de asemănătoare trebuie să fie două nume, de la 0 la 1, pentru ca un obiect din apropiere să fie considerat același lucru, nu doar aproape |

## Sincronizare cu telefonul

Ce face această instalare cu aplicația SpeleoLoc de pe telefon (vezi [Telefon și sincronizare
offline](../features/mobile-sync.md)). O modificare se aplică imediat tuturor telefoanelor care
se sincronizează. Salvarea înlocuiește toate cele cinci setări împreună.

| | |
|---|---|
| **Dă telefoanelor lista de persoane** | Activat implicit. Un telefon poate descărca o listă de speologi — doar nume, niciodată o adresă sau un număr de telefon — ca utilizatorul lui să poată spune cine este și cine a fost într-o tură. Dezactivat: telefoanele nu primesc lista și nu pot spune ce speolog este unul dintre utilizatorii lor. Asocierile făcute deja pe pagina [Utilizatori de telefon](../features/people-and-clubs.md#utilizatori-de-telefon) rămân, iar ce înregistrează un telefon despre utilizatorii lui se păstrează în continuare |
| **Ale cui nume sunt în listă** | *Speologii din grupurile de speologie ale contului telefonului și speologul propriu al contului* (implicit) sau *Toți speologii din listă*. Un telefon poate spune că utilizatorul lui este doar cineva care i-a fost arătat, așa că aceasta stabilește și pe cine poate numi un telefon. *Toți speologii* arată toată lista fiecărui cont care sincronizează un telefon, indiferent de grup; alege-o doar dacă lista nu este un secret față de cei care se autentifică |
| **Fă ture din înregistrări cu probleme** | Activat implicit. O tură înregistrată pe telefon este transformată singură într-un raport de tură. Activat: asta se întâmplă și când o parte nu a putut fi potrivită, de exemplu o stație pe care nimeni nu o poate plasa sau un utilizator de telefon neasociat unui speolog; tura este semnalată pentru verificare și spune de ce. Dezactivat: o astfel de înregistrare așteaptă pe pagina peșterii până când cineva o convertește manual |
| **Păstrează fotografiile trimise înaintea unei încărcări (zile)** | Implicit 7, de la 1 la 365. Un telefon trimite octeții unei fotografii înaintea încărcării care îi folosește; dacă nicio încărcare nu îi preia în acest număr de zile, sunt șterși. O modificare se aplică fișierelor sosite după ea. Fișierele care așteaptă se numără în spațiul contului care le trimite, așa că lasă loc pentru fotografiile unui telefon pe cam atâtea zile |
| **Păstrează fișierele unei încărcări respinse (zile)** | Implicit 30, de la 1 la 365. Când o încărcare ținută pentru verificare este hotărâtă fără ca fișierele ei să fie folosite — respinsă, înlocuită de una ulterioară sau acceptată fără ele — fișierele sunt șterse după acest număr de zile. Cât timp o încărcare așteaptă un verificator, fișierele ei se păstrează oricât durează |

Aceleași cinci setări pot primi o valoare de pornire din mediul de instalare; o valoare salvată
aici o înlocuiește.

## Fototeci

Pentru o instalare care are alături o fototecă, fiecare fototecă are un singur comutator:
**Oprește folosirea acestei fototeci**. Cât timp este pornit, nimic din aplicație nu întreabă
fototeca nimic — fără strat pe hartă, fără răsfoire, fără căutare și fără fotografii. Nu schimbă
nimic din ce conține fototeca însăși sau cine se poate autentifica direct în ea.

Comutatorul nu poate *conecta* o fototecă: ce fototeci are o instalare se hotărăște la
implementare, iar pagina o spune — sau spune că nu este nimic de oprit.

## Încărcări de topografii

Fila **Încărcări de topografii** ține alegerile despre topografiile trimise de pe un dispozitiv de
teren, cum este TopoDroid (vezi [Încărcarea topografiilor din TopoDroid](../features/topodroid-uploads.md)).

**O peșteră creată de o încărcare primește primul ei model** — pornit dacă nu e oprit. Când o
încărcare însăși a creat peștera, iar peștera nu are încă alt model și nicio drumuire, modelul
Therion produs de încărcare devine modelul curent al peșterii, iar drumuirea lui linia cu care este
desenată peștera, ca o peșteră făcută de pe telefon să nu rămână fără formă până alege cineva.
Versiunile următoare ale topografiei și încărcările la o peșteră care exista deja nu iau niciodată
singure acest loc. Oprit, un model dintr-o încărcare nu devine niciodată curent de la sine; cineva
îl alege pe pagina peșterii.

Două alegeri înrudite nu sunt pe această filă: câte versiuni ale unei topografii își păstrează
rezultatele rulărilor (`SILEXGIS__SurveyUploads__KeepArtefactVersions` — 0 le păstrează pe ale
fiecărei versiuni, 1 doar pe ale celei mai noi) și dimensiunea de la care cardul *Topografii* al
peșterii și pagina de stare avertizează că versiunile mai vechi țin mult
(`SILEXGIS__SurveyUploads__OlderVersionsWarnMegabytes`, implicit 500 MB). Amândouă se stabilesc în
mediul instalării; fila le numește.

## Spațiul de lucru

Fila **Spațiul de lucru** alege **aranjarea de pornire**: cu care dintre aranjările spațiului de
lucru al hărții — *Hartă*, *Hartă + 3D*, *Verificarea topografiilor* sau *Planificarea turei* —
pornește un cont nou (vezi [Panouri și aranjări](../features/map-workspace.md#panouri-și-aranjări)).
Se aplică o singură dată, prima dată când un cont care nu a aranjat nimic deschide harta pe un
calculator; de atunci aranjarea este a membrului, iar cine și-a aranjat deja spațiul de lucru nu
este mutat. *Două ecrane* nu este oferită: deschide o a doua fereastră, pe care browserul o permite
doar când cineva apasă ceva. *Hartă (ca la livrare)* anulează alegerea.

Alegerea se păstrează în aranjarea de pornire a instalării, același document cu secțiunile
implicite ale panourilor, pe care fila aceasta le lasă cum sunt.

## Hărți în documente

Fila **Hărți în documente** hotărăște **ce fundaluri de hartă pot fi copiate într-un document**.
[Raportul descărcat](../features/trips.md) al unei ture conține o hartă a turei desenată peste un
singur fundal: cel implicit, dacă este pornit aici, altfel primul care este.

Fiecare fundal pe care îl publică această instalare apare cu **un comutator**.

| Coloană | Ce arată |
|---|---|
| **Fundal** | Sursa, grupul în care o așază catalogul și mențiunea de drepturi scrisă sub imagine |
| **În documente** | Comutatorul |
| **Livrat** | Cu ce a venit instalarea — și *Hotărât aici* acolo unde un administrator a ales altfel, cu **Folosește răspunsul livrat** pentru a reveni asupra hotărârii |

- **Pornirea unui fundal este o afirmație despre termenii lui.** Afișarea unei hărți pe ecran și
  punerea ei într-un fișier care apoi este trimis și tipărit sunt utilizări diferite, iar fiecare
  furnizor o hotărăște separat pe a doua. Pornește o sursă doar după ce i-ai citit termenii; de
  aceea este arătat grupul în care este așezată.
- O hotărâre are efect **imediat** și se **păstrează la reporniri și actualizări**. Răspunsul
  livrat se aplică în continuare fiecărui fundal despre care nu a hotărât nimeni.
- O sursă **fără mențiune de drepturi** nu poate fi pornită: nu ar fi nimic de scris sub imagine.
- Cu **nimic pornit**, raportul își primește totuși harta — desenată pe un fond simplu, lucru pe
  care imaginea îl spune sub ea. Pagina avertizează când este cazul.
- Fiecare hotărâre este consemnată în [istoricul de modificări](../features/history-and-audit.md).

Ce fundaluri există și adresele lor rămân în fișierul-catalog al instalării — vezi ghidul de
instalare.

## Hartă de bază proprie

Fila **Hartă de bază proprie** se ocupă de **harta de bază vectorială proprie** a instalării: formele
OpenStreetMap ale unei regiuni, servite numai de instalarea ta și desenate pe hartă ca fundalul
*Self-hosted basemap (light)* — clare la orice zoom, cu etichete în limba fiecărui cititor și fără
nicio cerere către serverul altcuiva.

> O hartă de bază vectorială OpenStreetMap, servită numai de această instalare: descarcă o regiune
> dintr-o versiune Protomaps a planetei, iar fundalurile desenate din ea sunt oferite pe hartă de
> atunci încolo.

| Parte | Ce arată |
|---|---|
| **Servită acum** | Versiunea din care a fost decupată, mărimea, când a fost descărcată, regiunea și ultimul nivel de zoom |
| **Ultima descărcare** | În așteptare, în curs (cu cât a înaintat și **Anulează descărcarea**), terminată, eșuată sau anulată — și de ce a eșuat, în cuvinte |
| **Descarcă o regiune** | **România**, **Apuseni (regiune carstică)** sau **Un dreptunghi ales de mine** (vest, sud, est și nord, în grade), **ultimul nivel de zoom** (cel mai adânc este 15) și **versiunea** — cea mai nouă, implicit |

- **Până la prima descărcare, fundalul nu este oferit deloc pe hartă**, iar pagina spune ce
  fundaluri așteaptă una.
- **Estimează mărimea** întâi: calculează cât ar cântări descărcarea fără s-o aducă. România până
  la zoom 15 are aproximativ 1,3 GB, dreptunghiul Apuseni aproximativ 36 MB. O regiune mai mare
  decât acceptă instalarea ta, sau una pentru care nu e loc pe disc, este refuzată înainte să se
  descarce ceva.
- **O singură descărcare odată**, în fundal: poți părăsi pagina și reveni. O descărcare nouă
  **înlocuiește** harta de bază servită doar după ce s-a terminat; până atunci — și dacă eșuează
  sau e anulată — cea de acum este servită în continuare, neschimbată.
- Plăcile nu au curbe de nivel și nici relief: păstrează fundalurile topografice pentru citirea
  carstului.
- De unde se decupează regiunea se stabilește în configurarea instalării, nu pe această pagină:
  alegi o dată și un dreptunghi, niciodată o adresă. Dacă pagina spune că găzduirea proprie este
  oprită, instalarea nu rulează niciun server de dale pentru o hartă de bază proprie — vezi ghidul
  de instalare.
- Fiecare descărcare cerută și fiecare anulată este consemnată în
  [istoricul de modificări](../features/history-and-audit.md).

---

## Texte mesaje

**Configurare → Texte mesaje.** Formularea fiecărui mesaj, editabilă per limbă. Vezi
[Vocabulare](vocabularies.md#configurare--texte-mesaje).

---

## Urmărirea plecării mesajelor

**Administrare → Trimiterea mesajelor.**

Numerele de sus:

- câte mesaje **așteaptă**,
- câte sunt **reținute pentru un rezumat**,
- câte au fost **abandonate**,
- și cel care contează: **de cât timp așteaptă cel mai vechi care nu a plecat.**

Dedesubt, cele care merită privite: despre ce era fiecare, **pentru cine era — sub numele sub care
poate fi arătat și niciodată adresa lui** — și ce a spus capătul celălalt, cu tot ce arată a adresă
scos din mesaj.

### Repunerea manuală

Un mesaj abandonat poate fi reîncercat. Reîncercarea **pune din nou fiecare întrebare înainte să
trimită**:

- cine a spus între timp că nu vrea acel fel de mesaj **nu primește unul**,
- cine a cerut un rezumat zilnic **îl primește în rezumat**,
- iar **fiecare astfel de act este înregistrat pe seama celui care l-a făcut**.

Deci o reîncercare nu este o redare. Este o trimitere nouă, sub regulile curente.

### Păstrare

Cât timp se păstrează notificările — **Păstrează notificările (zile)**, în fila **Notificări** —
este un număr pe care un administrator îl poate schimba, peste ce a configurat instalarea. O
notificare este ștearsă după acest interval, citită sau nu, iar evidența felului în care a fost
trimisă dispare odată cu ea.

---

## Fereastra de liniște

O instalare poate stabili o fereastră în care **nimic nu pleacă de pe server** — citită în **fusul
orar al fiecărui destinatar**, deci înseamnă aceeași oră a nopții indiferent de anotimp.

Ce așteaptă stă în listele din aplicație tot timpul, pentru că o listă nu trezește pe nimeni.
Avertismentele pe care un membru nu le poate opri ignoră fereastra — la fel și alarma de echipă
întârziată.

---

## Anunțuri

Cine este împuternicit poate scrie un rând unui grup de speologie. Vezi
[Persoane și cluburi](../features/people-and-clubs.md#anunțarea-unui-club).

> **A avea voie să editezi lista unui club nu este același lucru cu a avea voie să scrii tuturor
> celor de pe ea.** Cele două drepturi se acordă separat.

---

## Listă de verificare pentru probleme

| Simptom | Uită-te la |
|---|---|
| Nimeni nu primește nimic | Este e-mailul *Activat*? Spune pagina *„scrise în jurnalul serverului"*? |
| Unii primesc, alții nu | [Grila lor de notificări](../features/notifications.md) — un membru poate să fi oprit acel fel |
| Mesajele se adună și nu pleacă niciodată | Cifra **cel mai vechi în așteptare** de pe pagina de trimitere și ce a spus capătul celălalt |
| Mesajele text se opresc la mijlocul zilei | **Plafonul zilnic** — și amintește-ți că numără segmente |
| Cineva spune că un mesaj a numit o peșteră | Nu se poate. Mesajele nu numesc niciodată peșteri. Verifică dacă nu se referă la *pagina* la care a dus linkul |

---

Înrudite: [Notificări](../features/notifications.md) ·
[Persoane și cluburi](../features/people-and-clubs.md)
