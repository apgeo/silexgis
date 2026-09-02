# Mesagerie și trimitere

🇬🇧 [English](../../admin/messaging.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite:
[Notificări](../features/notifications.md)

---

**Administrare → Mesagerie** configurează unde ajung e-mailurile și mesajele text, ce cere
autentificarea și un comutator de protecție a locațiilor. **Administrare → Trimiterea mesajelor**
arată dacă ceva din toate acestea chiar funcționează.

> Acestea pot fi stabilite și din mediul de instalare, iar pagina o spune. Stabilirea lor aici
> este calea care nu are nevoie de repornire.

---

## E-mail

| Câmp | |
|---|---|
| **Activat** | |
| **Server** · **Port** | |
| **Securitatea conexiunii** | Automată · TLS la conectare (465) · Niciuna (text simplu) |
| **Utilizator** · **Parolă** | Pagina spune dacă este stocată o parolă; lăsați gol ca s-o păstrați |
| **Adresă expeditor** · **Nume expeditor** · **Adresă de răspuns** | |
| **Timp de așteptare** | Secunde |
| **Acceptă un certificat nevalid** | *Doar pentru un releu intern cu certificat autosemnat.* Elimină protecția de pe traseu |

Starea este declarată limpede: **„E-mailul este configurat"**, sau:

> *„Niciun server de poștă configurat. Mesajele sunt scrise în jurnalul serverului în loc să fie
> trimise."*

**Nu configurați nimic și aplicația funcționează în continuare.** Linkurile de autentificare, cele
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

Stabiliți plafonul având asta în minte și spuneți-i cui scrie anunțuri.

## Politica de autentificare

Ce cere autentificarea — inclusiv ce metode de autentificare în doi pași oferă această instalare.
Metodele neactivate aici sunt marcate *„Neactivată pe această instalare"* pe paginile de securitate
ale membrilor.

## Protecția locațiilor

Un singur comutator, descris integral la
[Protecția locațiilor](location-protection.md#setarea-de-asociere-a-atașamentelor):

**„Arată atașamentele peșterilor protejate tuturor celor care le pot citi."**

Documentele atașate unei peșteri protejate sunt **întotdeauna servite** — nu sunt niciodată ascunse
sau scoase. Ce controlează acest comutator este dacă se divulgă *asocierea* dintre document și acea
peșteră anume.

Pornirea lui **nu dezvăluie niciodată o poziție**: urmărirea linkului tot dă vederea protejată.

---

## Texte mesaje

**Configurare → Texte mesaje.** Formularea fiecărui mesaj, editabilă per limbă. Vedeți
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

Cât timp se păstrează notificările este un număr pe care un administrator îl poate schimba, peste
ce a configurat instalarea.

---

## Fereastra de liniște

O instalare poate stabili o fereastră în care **nimic nu pleacă de pe server** — citită în **fusul
orar al fiecărui destinatar**, deci înseamnă aceeași oră a nopții indiferent de anotimp.

Ce așteaptă stă în listele din aplicație tot timpul, pentru că o listă nu trezește pe nimeni.
Avertismentele pe care un membru nu le poate opri ignoră fereastra — la fel și alarma de echipă
întârziată.

---

## Anunțuri

Cine este împuternicit poate scrie un rând unui grup de speologie. Vedeți
[Persoane și cluburi](../features/people-and-clubs.md#anunțarea-unui-club).

> **A avea voie să editați lista unui club nu este același lucru cu a avea voie să scrieți tuturor
> celor de pe ea.** Cele două drepturi se acordă separat.

---

## Listă de verificare pentru probleme

| Simptom | Priviți la |
|---|---|
| Nimeni nu primește nimic | Este e-mailul *Activat*? Spune pagina *„scrise în jurnalul serverului"*? |
| Unii primesc, alții nu | [Grila lor de notificări](../features/notifications.md) — un membru poate să fi oprit acel fel |
| Mesajele se adună și nu pleacă niciodată | Cifra **cel mai vechi în așteptare** de pe pagina de trimitere și ce a spus capătul celălalt |
| Mesajele text se opresc la mijlocul zilei | **Plafonul zilnic** — și amintiți-vă că numără segmente |
| Cineva spune că un mesaj a numit o peșteră | Nu se poate. Mesajele nu numesc niciodată peșteri. Verificați dacă nu se referă la *pagina* la care a dus linkul |

---

Înrudite: [Notificări](../features/notifications.md) ·
[Persoane și cluburi](../features/people-and-clubs.md)
