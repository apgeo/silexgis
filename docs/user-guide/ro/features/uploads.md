# Încărcări

🇬🇧 [English](../../features/uploads.md) · 🇷🇴 **Română**

[← Referință](README.md) · Flux:
[Clasarea arhivei clubului](../workflows/file-the-archive.md)

---

**Bibliotecă → Încărcări** este pentru introducerea fișierelor *în masă* și pentru evidența a ce
a sosit împreună.

Pentru câteva fișiere, trage-le direct pe înregistrarea de care aparțin sau pe un dulap. Vii
aici când sunt sute.

---

## Înainte de a începe

Pagina îți spune, din capul locului:

- **Cel mai mare fișier acceptat** — 512 MB implicit; este o setare.
- **Spațiu rămas pentru tine** — cota ta, dacă instalarea stabilește una.
- **Tipurile acceptate**, sau *„Se acceptă orice tip de fișier"*, și orice **nu este acceptat**.
- Dacă încarci într-un dulap: **ce trebuie să conțină documentele depuse acolo**.

## Încărcarea

**Trage fișiere aici sau apasă pentru a le alege** — sau **Alege un folder**.

Opțiuni:

| Opțiune | |
|---|---|
| **Numele acestui lot** | Opțional. Tot ce se încarcă împreună poate fi regăsit după acest nume |
| **Etichetează tot cu** | Opțional. Eticheta se creează dacă nu există încă |
| **Desfă arhivele ZIP pe server, păstrând folderele** | Dosarele devin dulapuri |

### Duplicate

Dacă un fișier cu exact acest conținut este deja stocat, ești întrebat: **Salvează oricum**
sau **Sari peste** — formulat diferit după cum poți sau nu vedea documentul existent.

### Rezumatul

*„n din m salvate · k sărite · j eșuate"*, cu un motiv pe rând:

| Motiv | |
|---|---|
| Deja salvat | Există conținut identic |
| Mai mare decât acceptă instalarea | |
| Acest tip de fișier nu este acceptat | |
| Nu mai este spațiu suficient | Cotă |
| Fișierul este gol | |
| Această cale de folder nu poate fi depusă | |
| Nu ai voie să depui în acel dulap | |
| Fișierul nu a putut fi citit | |
| Încărcarea a eșuat | |

**Reîncearcă n eșuate** le pune înapoi dintr-o singură apăsare.

---

## Importul dintr-un dosar de pe server

Dacă instalarea este configurată pentru asta: **Importă dintr-un director de pe server**.

Copiază fișiere la care serverul poate ajunge deja, **fără să le trimită prin rețea** — răspunsul
potrivit pentru un disc băgat în server, nu o încărcare de 40 GB pe o conexiune casnică.
Dosarele devin dulapuri.

Pagina listează din ce dosare are voie să importe instalarea. Dacă nu este configurat niciunul, o
spune și numește setarea de care ar avea nevoie un operator.

Importul pornește, iar **raportul lui se completează pe măsură ce rulează**.

---

## Registrul loturilor

Fiecare lot de încărcare este listat:

| Coloană | |
|---|---|
| **Sursă** | Browser · Arhivă · Director pe server |
| **Stare** | Deschis · În lucru · Terminat · Eșuat |
| **Numărători** | *n salvate · m sărite · j eșuate* |
| **Încărcat** | Când |
| **Raport** | Detaliul per fișier |

Așa răspunzi peste șase luni la „ce s-a întâmplat cu cutia de scanări pe care am făcut-o în
martie".

---

## Ce urmează

Documentele încărcate ajung în [bibliotecă](documents-and-cabinets.md). **Textul lor este citit
în fundal**, iar starea aceea se poate urmări pe pagina fiecărui document. Fotografiile ajung în
[galerie](photographs.md).

Dacă ai cerut să fii anunțat, primești o notificare când **încărcările tale termină de
procesat** — vezi [Notificări](notifications.md).

> **Ai încărcat o arhivă mare înainte ca cititorul de text sau convertorul de documente să
> funcționeze?** Un administrator poate trece din nou peste ea — vezi
> [Operațiuni de întreținere](../admin/maintenance.md).

---

Înrudite: [Documente și dulapuri](documents-and-cabinets.md) ·
[Fotografii](photographs.md) · [Operațiuni de întreținere](../admin/maintenance.md)
