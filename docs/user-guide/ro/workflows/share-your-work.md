# Flux: partajarea muncii dumneavoastră

🇬🇧 [English](../../workflows/share-your-work.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință:
[Partajare, coduri QR și pagini publice](../features/sharing-and-public-pages.md) ·
[Permisiuni](../admin/permissions.md)

---

Cineva din afară trebuie să vadă ceva. Un proprietar de teren, un redactor de revistă, alt club,
un oficiu, publicul. Există cinci mecanisme diferite și nu sunt interschimbabile — alegerea
greșită fie nu îi ajunge, fie divulgă mai mult decât ați vrut.

---

## Hotărâți întâi: cine este persoana aceasta?

| Este | Folosiți |
|---|---|
| Un membru al acestei instalări | **Acordați-i acces** — o regulă de permisiune sau vizibilitatea înregistrării |
| Cineva din club, fără cont | **Nimic nu funcționează.** Trebuie să primească un cont, sau să fie contactat altfel |
| Un străin care are nevoie de o singură înregistrare | **Un link de partajare** |
| Un străin care trebuie să vadă o hartă configurată de dumneavoastră | **O vedere partajată** |
| Oricine, pentru poze | **Galeria publică** sau un **album partajat** |
| Cineva aflat la peșteră cu un telefon | **Un cod QR tipărit** — care nu îi spune aproape nimic |

---

## 1. Acordarea accesului unui membru

Două pârghii.

**Schimbați vizibilitatea înregistrării** — privată, grup de speologie, autentificați, publică.
Grosier, dar adesea corect: a face o peșteră *autentificată* înseamnă că oricine are cont o poate
citi.

**Scrieți o regulă de permisiune.** Pe obiectul însuși (fila *Permisiuni*), sau într-un
[grup de permisiuni](../admin/permissions.md) din care face parte. O regulă numește un subiect
(un utilizator sau un grup de speologie), un efect (permite sau refuză), acțiunile și un domeniu.

Domenii de reținut:

- **Doar acest obiect**
- **Acest obiect și tot ce conține** — subarborele
- **Un set de elemente** — un set numit de elemente către care pot arăta reguli
- **Un dulap și tot ce este clasat sub el**
- **Conținutul unui grup de speologie**
- **Obiectele proprii**, **Tot**

> Un **refuz** bate orice permisiune de la același nivel, iar o regulă scrisă direct pe un obiect
> bate orice se moștenește. Fereastra o spune înainte să salvați.

Dacă nu sunteți sigur de ce cineva vede sau nu vede ceva: fiecare obiect are **„Accesul
dumneavoastră aici și motivul lui"**, care numește regula care a decis.

## 2. Un link de partajare — o înregistrare, revocabil

Pe un element, o peșteră, o vedere salvată: **Partajare → Creează link**.

Alegeți:

- **Public** sau **Necesită autentificare**,
- **Include înregistrările conținute** — dacă vine și subarborele.

Linkul este **afișat o singură dată**. Copiați-l atunci; nu poate fi recuperat după aceea. Dacă
îl pierdeți, revocați-l și generați altul.

Linkurile existente sunt listate cu data creării și cu starea *Activ* sau *Revocat*.
**Revocarea** omoară un link imediat.

> **O partajare nu dezvăluie niciodată o locație protejată.** Cine urmează linkul vede
> înregistrarea sub aceleași reguli de protecție ca un vizitator anonim. Partajarea nu este o
> cale de ocolire a protecției locațiilor și a fost proiectată ca să nu poată deveni una.

## 3. O vedere de hartă partajată

Salvați harta curentă — extinderea, straturile, filtrele ei — ca **vedere salvată** (*Vederi
salvate → Salvează vederea curentă*). Apoi **Copiază linkul de partajare**.

Cine deschide linkul primește harta așa cum ați configurat-o, cu protecțiile intacte. Util pentru
„iată zona despre care vorbim" fără să dați cont nimănui.

Puteți și **Exporta imaginea hărții**, pentru o poză de lipit într-un document.

## 4. Fotografii: galeria publică și albumele partajate

Două lucruri separate.

**Galeria publică** (`/gallery/public`) este vitrina îngrijită a instalării. O fotografie apare
acolo pentru că cineva a bifat **Arată în galeria publică** pe ea. Aceasta este o decizie
*separată* de cine poate citi fotografia în interiorul aplicației.

**Un album partajat** este un singur album dat printr-un link propriu, tot afișat o singură dată,
tot revocabil.

Amândouă arată **doar randări** — niciodată fișierul original, niciodată metadatele aparatului.

Dați fotografiilor un **credit și o licență** înainte să le publicați. Lista de licențe merge de
la CC0, prin familia CC BY, până la *Toate drepturile rezervate*, plus *Nimeni nu a spus*.

## 5. Un cod QR tipărit la peșteră

Dacă clubul prinde etichete pe pereții peșterilor, adresa de pe etichetă se rezolvă — dar abia.

Din pagina unei peșteri: **Coduri tipărite**. Fie **Publicați** (codurile de la această peșteră
se rezolvă pentru oricine), fie **Opriți publicarea** (etichetele deja montate nu răspund nimic).

Ce primește efectiv un străin care scanează: o pagină care spune că **codul este înregistrat la
această instalare, și nimic altceva.** Fără nume, fără poziție, fără fotografie. A avea cont nu
schimbă nimic — răspunsul este același pentru toată lumea.

Acesta este exact rostul. Confirmă că eticheta este autentică și că peștera este cunoscută, fără
să fie un serviciu de căutare pentru locațiile peșterilor.

---

## Ce nu face niciodată partajarea

- Nu dă niciodată o poziție exactă pe care
  [protecția locațiilor](../admin/location-protection.md) o reține.
- Nu dezvăluie niciodată celelalte capete ale unei legături cuiva care nu are voie să le vadă.
- Nu publică niciodată un raport de tură — nu există adresă publică pentru unul. Un raport se
  descarcă de cineva autentificat care poate citi tura.
- Nu trimite niciodată numele sau poziția unei peșteri într-un e-mail de notificare.

---

## Scrisul către propriul club

Nu chiar partajare, dar înrudit: cine este împuternicit poate **Anunța** un grup de speologie —
un rând care ajunge la fiecare membru cu cont, fiecăruia în felul pe care l-a ales (poștă sau în
aplicație), nu ca o listă de mail de pe care nimeni nu poate ieși.

Înainte de trimitere vi se spune **la câți oameni ajunge**, iar confirmarea este un pas separat.
Un mesaj către două sute de oameni nu este niciodată un singur clic neatent. Vedeți
[Persoane și cluburi](../features/people-and-clubs.md#anunțarea-unui-club).

---

Referință: [Partajare, coduri QR și pagini publice](../features/sharing-and-public-pages.md)
