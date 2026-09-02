# Partajare, coduri QR și pagini publice

🇬🇧 [English](../../features/sharing-and-public-pages.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux:
[Partajarea muncii dumneavoastră](../workflows/share-your-work.md)

---

Fiecare suprafață la care poate ajunge un vizitator **fără cont**. Sunt șase, și aceasta este
lista completă.

| Suprafață | Ce primește un vizitator |
|---|---|
| **Un link de partajare** | O înregistrare (opțional cu ce conține) |
| **O vedere partajată** | O configurație de hartă salvată |
| **Un album partajat** | Un album de fotografii, ca randări |
| **Galeria publică** | Fotografiile îngrijite ale instalării |
| **Un cod QR tipărit** | Confirmarea că acel cod este înregistrat. Nimic altceva |
| **Pagina de dezabonare** | Deschisă dintr-un client de poștă |

O instalare care nu publică nimic îi arată unui vizitator **pagina de autentificare** și nimic
altceva.

---

## Linkuri de partajare

Pe un element, o peșteră sau o vedere salvată: **Partajare → Creează link**.

| Alegere | |
|---|---|
| **Acces** | *Public* sau *Necesită autentificare* |
| **Include înregistrările conținute** | Dacă vine și subarborele |

**Jetonul este afișat o singură dată.** *„Copiați-l acum — acest link se afișează o singură
dată."* Dacă îl pierdeți, revocați-l și generați altul.

Linkurile existente își listează data **creării** și **starea** (*Activ* / *Revocat*).
**Revocarea** omoară unul imediat, iar etichetele deja distribuite încetează să funcționeze.

O înregistrare partajată se randează ca o **vedere doar-citire**, cu înregistrările incluse
listate. Un link care cere autentificare îi spune vizitatorului anonim să **se autentifice**, în
loc să nu-i arate nimic.

> **O partajare nu dezvăluie niciodată o locație protejată.** Partajarea nu este o cale de
> ocolire a [protecției locațiilor](../admin/location-protection.md) și a fost proiectată ca să
> nu poată deveni una.

---

## Vederi partajate

Vedeți [Vederi salvate](saved-views-and-windows.md#vederi-salvate). Același jeton afișat o
singură dată, aceeași revocare, aceleași protecții.

---

## Galeria publică și albumele partajate

**Galeria publică** (`/gallery/public`) este vitrina îngrijită a instalării. O fotografie apare
acolo pentru că cineva a bifat **Arată în galeria publică** pe ea — o decizie *separată* de cine
poate citi fotografia în interiorul aplicației.

**Un album partajat** este un singur album dat printr-un link propriu, tot afișat o singură dată.

Amândouă arată **doar randări** — niciodată fișierul original, niciodată metadatele lui de aparat.

Dacă nu s-a publicat nimic: *„Nu s-a publicat încă nimic."*

---

## Coduri QR tipărite

Pentru cluburile care prind etichete pe pereții peșterilor.

### Publicarea

Din pagina unei peșteri: **Coduri tipărite**.

| | |
|---|---|
| **Codurile se rezolvă pentru oricine** | Publicat |
| **Codurile nu se rezolvă pentru nimeni** | Nepublicat sau retras |

**Publică** / **Oprește publicarea**, cu date înregistrate pentru fiecare. Retragerea înseamnă
*„etichetele deja montate nu vor răspunde nimic."*

O peșteră care nu poartă un cod propriu o spune — **locurile dinăuntrul ei poartă codurile lor**,
iar fiecare se rezolvă prin decizia peșterii. Deschideți un loc ca să-i vedeți pătratul.

### Ce arată de fapt o scanare

> *„Codul pe care l-ați scanat este înregistrat la {instalare}, și asta este tot ce spune această
> pagină: nicio peșteră, niciun loc și nicio poziție. **Un cont nu schimbă nimic aici — răspunsul
> este același pentru toată lumea.**"*

Dacă codul nu este înregistrat sau peștera nu este publicată: *„Acest cod nu este înregistrat
aici, sau peștera de care aparține nu este publicată."*

Acesta este întregul design. Confirmă că o etichetă este autentică și cunoscută, fără să fie un
serviciu de căutare pentru locațiile peșterilor.

### Coduri care nu pot fi tipărite

Unele coduri conțin caractere pe care o adresă trebuie să le codifice, iar scanerele de telefon
citesc adresa exact cum este scrisă, fără s-o decodifice. Aplicația detectează asta și **spune că
acel cod nu poate fi tipărit ca etichetă**, în loc să vă lase să prindeți unul stricat pe o stâncă.

### Adresa

**Copiază adresa** vă dă URL-ul pe care ar trebui să-l codifice eticheta. Este o rută de cale, nu
un fragment — deliberat, pentru că scanerele aruncă orice se află după un `#`.

---

## Ce nu este niciodată public

- **Un raport de tură.** Nu există adresă publică pentru unul; se descarcă de cineva autentificat
  care poate citi tura.
- **O locație protejată exactă**, prin oricare dintre cele de mai sus.
- **Celelalte capete ale unei legături** pe care nu aveți voie să le vedeți.
- **Numele unei peșteri într-un e-mail de notificare.**

---

Flux: [Partajarea muncii dumneavoastră](../workflows/share-your-work.md) ·
Înrudite: [Protecția locațiilor](../admin/location-protection.md) ·
[Permisiuni](../admin/permissions.md)
