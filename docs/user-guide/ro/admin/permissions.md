# Permisiunile explicate

🇬🇧 [English](../../admin/permissions.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite:
[Protecția locațiilor](location-protection.md) ·
[Noțiuni de bază](../core-concepts.md#2-vizibilitatea-permisiunile-și-protecția-locației-sunt-trei-lucruri-diferite)

---

SilexGIS are **grupuri de permisiuni editabile în locul unor roluri fixe**. Nu există „Editor" sau
„Moderator" încorporat în program; există seturi de reguli denumite pe care le-a scris cineva, iar
dumneavoastră faceți parte din unele dintre ele.

---

## Cele trei lucruri care decid accesul

Accesul la o înregistrare este decis de trei mecanisme independente care se compun:

### 1. Proprietatea

Dețineți ce ați creat. Proprietatea acordă acces prin ea însăși.

### 2. Vizibilitatea

O proprietate a înregistrării, stabilită de cine a creat-o:

| Valoare | Admite |
|---|---|
| **Privată** | Doar proprietarul și cine a primit acces explicit |
| **Grup de speologie** | Membrii grupului de care este legată |
| **Utilizatori autentificați** | Oricine are cont aici |
| **Publică** | Oricine, inclusiv vizitatori fără cont |

### 3. Regulile

*Permite* sau *refuză*, pe acțiune, pe domeniu, la un anumit cuprins. Regulile trăiesc în
**grupuri de permisiuni** sau direct pe un obiect.

Deasupra tuturor celor trei stau **Administratorii compleți** — apartenența decide înainte de
consultarea vreunei reguli.

---

## Anatomia unei reguli

O regulă spune: **[subiectul] este [permis / refuzat] pentru [aceste acțiuni] pe [acest domeniu]
în cuprinsul [acestui domeniu de aplicare]**.

### Subiect

Un **utilizator** sau un **grup de speologie**. (Într-un grup de permisiuni, membrii sunt
subiectul.)

### Acțiuni

| Acțiune | |
|---|---|
| **Citire** | |
| **Scriere** | |
| **Creare** | |
| **Ștergere** | |
| **Partajare** | Generarea de linkuri de partajare |
| **Rulare** | Execuție — de ex. pornirea unei sarcini |
| **Administrare permisiuni** | Scrierea de reguli pe acest lucru |
| **Locație exactă** | Vederea coordonatelor reținute — vedeți [Protecția locațiilor](location-protection.md) |

### Domenii

Elemente · Jurnale de tură · Fișiere de geodate · Hărți georeferențiate · Vederi de hartă salvate
· Depozitul de fișiere · Documente · Expediții · Straturi de hartă · Etichete · Ierarhii ·
Vocabulare · Speologi · Grupuri de speologie · Conturi de utilizator · Grupuri de permisiuni ·
Seturi de elemente · Setările instalării · Texte mesaje · Istoric modificări · Sarcini · Relief ·
Liste de verificare · Evenimente de calendar

### Cuprinsuri

Aceasta este partea care face sistemul practic în loc de plictisitor.

| Cuprins | Ajunge la |
|---|---|
| **Tot** | Întregul domeniu |
| **Obiectele proprii** | Ce a creat subiectul |
| **Conținutul unui grup de speologie** | Tot ce este legat de acel grup |
| **Un element și tot ce conține** | Un subarbore din ierarhia de elemente |
| **Un set de elemente** | Un set numit de elemente — vedeți mai jos |
| **Un dulap și tot ce este clasat sub el** | O ramură din arborele de clasare |
| **Un singur obiect** | O singură înregistrare |

Regulile au și un **nivel** — *global*, *colecție* sau *obiect* — în termenii căruia este
formulată rezolvarea conflictelor de mai jos.

---

## Cum se rezolvă conflictele

Două reguli, declarate pe fiecare ecran care vă lasă să scrieți una:

> **Un refuz bate orice permisiune de la același nivel, iar o regulă scrisă pe obiectul însuși bate
> orice se moștenește.**

Deci o permisiune la nivel de obiect bate un refuz global; un refuz global bate o permisiune
globală.

**Verificați ce costă înainte să salvați.** Un grup de permisiuni care conține un refuz nu vă lasă
să-l salvați fără să deschideți întâi previzualizarea — vedeți mai jos.

---

## Grupuri de permisiuni

**Administrare → Grupuri de permisiuni.**

Un grup are un **nume**, o **descriere**, **membri** și **reguli**. Trei file: Reguli, Membri,
Previzualizare.

> *„O regulă trăiește aici sau direct pe un obiect — niciodată într-un grup de speologie."*
> Grupurile de speologie sunt despre cine este într-un club; grupurile de permisiuni sunt despre ce
> pot face oamenii.

### Grupuri incluse și protejate

Unele grupuri vin cu produsul și sunt marcate **Incluse** sau **Protejate**. În special,
**Administratorii compleți nu dețin reguli**: apartenența însăși este acordarea, decisă înaintea
oricărei reguli.

Aplicația refuză categoric două lucruri:

- orice ar **lăsa niciun administrator complet activ capabil să se autentifice**,
- o regulă care ar **acorda drepturi pe care dumneavoastră înșivă nu le dețineți**.

### Previzualizarea

**Alegeți un utilizator sau un grup de speologie ca să vedeți ce drepturi ar deține.** Extindeți un
rând ca să vedeți ce regulă a decis fiecare acțiune.

Un set de reguli care conține un **refuz** vă cere să deschideți previzualizarea înainte de
salvare. Aceasta este singura frecare din tot sistemul de permisiuni și există pentru că un refuz
este lucrul care strică tacit accesul cuiva peste trei luni.

### Ștergerea unui grup

*„Membrii pierd doar ce le acorda."* Ștergerea unui grup de permisiuni nu șterge nimic altceva.

---

## Acordări pe obiect

Fiecare obiect are o filă **Permisiuni** (acolo unde aveți voie să administrați permisiuni pe el).

Adăugați o regulă care numește un **utilizator** sau un **grup de speologie**, un efect, acțiuni și
un cuprins de **doar acest obiect** sau **acest obiect și tot ce conține**.

### Reguli scrise de o tabără

Pe o tură care aparține unei tabere, unele reguli apar ca **De la o tabără**. Au fost scrise de
tabără și se schimbă acolo, nu aici.

Sunt arătate pe tură oricum, ca **întreaga listă a cui poate citi tura să fie într-un singur loc**.

---

## Seturi de elemente

**Configurare → Seturi de elemente.** Un set numit de elemente către care pot fi îndreptate reguli
de acces — *„felul de a spune «aceste peșteri anume»"* fără a scrie o regulă per peșteră.

- Adăugați și scoateți elemente căutându-le.
- **Schimbarea elementelor unui set mută accesul**, pentru că regulile arată către set. Fiecare
  modificare este înregistrată.
- Un set către care încă arată reguli nu poate fi șters până nu sunt scoase acele reguli.

Dulapurile fac echivalentul pentru documente — vedeți
[Documente și dulapuri](../features/documents-and-cabinets.md#dulapuri).

---

## „De ce vede persoana asta lucrul acela?"

Fiecare obiect poartă **Accesul dumneavoastră aici și motivul lui**.

Pentru fiecare acțiune spune **Permis** sau **Refuzat** și numește motivul:

| Sursă | |
|---|---|
| *Administrare completă: apartenența decide înaintea oricărei reguli.* | |
| *Acordat prin proprietatea asupra obiectului.* | |
| *Acordat prin vizibilitatea obiectului.* | |
| *Decis la nivelul [obiect / colecție / global] de „[numele regulii]".* | |
| *Decis la nivelul … de o regulă pe care nu o puteți vedea.* | Vi se spune că o regulă a decis, nu ce spune regula |
| *Decis de o regulă scrisă direct pe acest obiect.* | |
| *Nicio regulă nu acordă asta.* | |

Aceasta este unealta pentru fiecare discuție „dar de ce nu poate Maria să deschidă aia?".
Începeți de aici.

---

## Ce nu fac permisiunile

- **Nu trec peste [protecția locațiilor](location-protection.md).** Vederea unei poziții exacte
  este o acțiune proprie (*Locație exactă*), acordată separat.
- **Nu circulă printr-o invitație.** A fi invitat pe o tură nu acordă nimic.
- **Nu ajung la cineva fără cont.** Un speolog din listă care nu se poate autentifica nu poate
  primi nimic.

---

Înrudite: [Protecția locațiilor](location-protection.md) ·
[Persoane și cluburi](../features/people-and-clubs.md) ·
[Istoric și modificări](../features/history-and-audit.md)
