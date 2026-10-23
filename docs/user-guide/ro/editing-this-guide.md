# Cum se modifică acest ghid

🇬🇧 [English](../editing-this-guide.md) · 🇷🇴 **Română**

[← Înapoi la ghid](README.md) · Vezi și: [Terminologie EN/RO](TERMINOLOGIE.md)

---

Acest ghid este făcut ca să fie modificat — de oameni și de un agent care lucrează în acest
depozit. Pagina aceasta este contractul dintre cele două, ca niciunul să nu strice munca
celuilalt.

---

## Unde se află

```
docs/user-guide/
├── README.md …               versiunea engleză (canonică)
├── workflows/ features/ admin/
└── ro/                       versiunea română — aceleași nume de fișiere
    ├── README.md
    ├── TERMINOLOGIE.md       corespondența EN/RO a termenilor
    ├── workflows/
    ├── features/
    └── admin/
```

**Markdown simplu, fără pas de construcție, fără generator de site.** Se randează pe GitHub, în
orice vizualizator de Markdown, într-un editor, și poate fi lipit într-un wiki. Linkurile sunt
relative, deci arborele poate fi mutat sau publicat ca atare.

**Engleza este canonică.** Când o pagină se schimbă, se schimbă întâi în engleză, apoi în română.
O pagină românească rămasă în urmă este un defect, nu o variantă.

## Ce este fiecare director

| Director | Răspunde la |
|---|---|
| Nivelul de sus | *Ce este asta și cum încep?* |
| `workflows/` | *Cum fac treaba asta întreagă?* — narativ, pași în ordine, trimiteri pentru detalii |
| `features/` | *Ce face ecranul acesta?* — referință, completă, răsfoibilă |
| `admin/` | *Cum administrez această instalare?* — tot pentru utilizator, nu pentru instalare |

Dacă nu ești sigur unde se potrivește ceva: **dacă are o ordine, este un flux de lucru; dacă
are o suprafață, este o pagină de referință.**

---

## Convenții

**Fiecare pagină începe cu un rând de limbă și un fir de navigare** — un link către cealaltă
limbă, un link înapoi la cuprinsul secțiunii și, de obicei, către paginile înrudite.

**Fiecare pagină se încheie cu linkuri mai departe.** Nimic nu trebuie să fie o fundătură.

**Folosește linkuri relative.** De la nivelul de sus, `features/trips.md`; din `features/`, doar
`trips.md`; dintr-un director frate, `../features/trips.md`. Din `ro/`, orice trimitere în afara
lui `docs/user-guide/` are nevoie de un `../` în plus.

**Tabele pentru orice se poate enumera.** Liste de câmpuri, stări, opțiuni, înțelesuri de erori.
Se parcurg mult mai ușor și se mențin corecte mai ușor decât proza.

**Citează cuvintele aplicației** când spune ceva bine, într-un citat. O mare parte din textul de
interfață al acestei aplicații este ea însăși documentație bună, iar citarea face ca ghidul și
ecranul să fie de acord.

**Spune ce *nu* se întâmplă.** O mare parte din ce face această aplicație previzibilă este ce
refuză să facă. Secțiunile intitulate *Ce nu face niciodată X* sunt un tipar deliberat.

**Niciun detaliu de implementare**, decât dacă schimbă ce ar trebui să facă cititorul. Fără căi de
fișiere din `server/` sau `client/`, fără rute de interfață de programare, fără tabele de bază de
date, fără cod.

### Pentru română, în plus

**Folosește exact termenii pe care îi afișează interfața.** Sunt strânși în
[TERMINOLOGIE.md](TERMINOLOGIE.md), extrași din fișierul de traducere al aplicației. Un ghid care
spune „dosare" acolo unde ecranul spune „dulapuri" nu ajută pe nimeni.

**Scrie cu diacritice**, peste tot.

**Ghidul în română, ca și interfața, i se adresează cititorului cu „tu”** — niciodată cu
„dumneavoastră” sau cu pluralul de politețe. Termenii stabiliți (drumuire pentru *centerline*,
model de drumuire pentru *line plot*, obiect pentru *feature*, registru pentru registrul
peșterilor …) sunt trecuți, cu regulile lor, la începutul lui [TERMINOLOGIE.md](TERMINOLOGIE.md).

---

## Lucrul cu un agent la acest ghid

Cere ce vrei în cuvinte obișnuite. Câteva formulări care funcționează bine:

> *„Adaugă o pagină sub `features/` pentru ecranul X, urmând convențiile din
> `docs/user-guide/editing-this-guide.md`, și leag-o din cuprinsul de referință și din pagina
> principală a ghidului — în ambele limbi."*

> *„Recitește `client/src/i18n/locales/en.json` și verifică pagina de Ture față de ce spune acum
> interfața."*

> *„Fluxul pentru Y este învechit — parcurge ecranele reale și corectează-l."*

### Ce trebuie spus explicit unui agent

- **Actualizează cuprinsurile.** O pagină nouă trebuie legată cel puțin din `README.md` al
  directorului ei și, dacă este importantă, din pagina principală a ghidului. O pagină nelegată
  este o pagină invizibilă.
- **Actualizează și versiunea română**, sau spune limpede că nu a făcut-o.
- **Adaugă intrări în glosar** pentru orice termen nou, și în TERMINOLOGIE.md dacă termenul apare
  în interfață.
- **Nu inventa comportamente.** Dacă purtarea aplicației nu este clară, mișcarea onestă este un
  paragraf scurt care spune ce nu se știe, nu o presupunere sigură de sine. Valoarea acestui ghid
  stă în faptul că se poate avea încredere în el.
- **Verifică textul interfeței** (`client/src/i18n/locales/en.json` și `ro.json`), nu
  specificația, atunci când cele două ar putea diferi. Specificația descrie intenția; textul de
  interfață descrie ce citește efectiv un utilizator.

### O ordine bună a surselor pentru un agent

1. `client/src/i18n/locales/en.json` și `ro.json` — ce spune efectiv interfața
2. `client/src/App.tsx` și `client/src/components/navItems.tsx` — ce pagini există și cum sunt
   grupate
3. `README.md` din depozit — narațiunea funcțiilor, în vocea aplicației
4. `.claude/docs/` — specificația, pentru intenție și pentru ce nu explicitează interfața

---

## Ca ghidul să rămână onest

Două moduri de a eșua, de urmărit pe măsură ce crește:

**Derivă.** Un ecran se schimbă și pagina care îl descrie nu. Apărarea cea mai ieftină este să
corectezi pagina în clipa în care observi, nu s-o treci pe o listă de sarcini.

**Greșeală spusă cu siguranță de sine.** O pagină corectă în proporție de 90% și inventată în
proporție de 10% este mai rea decât una care spune *„aceasta nu este încă documentată"*, pentru
că un cititor nu poate ști care sunt cele 10%. Preferă o lacună unei presupuneri și marchează
lacunele limpede:

```markdown
> **Încă nedocumentat.** [Ce lipsește și cine ar ști.]
```

---

## Ce nu se află în acest ghid

| Subiect | Unde |
|---|---|
| Instalare, actualizare, copii de siguranță, TLS | [INSTALL.md](../../INSTALL.md) |
| Punerea pe un server | [DEPLOY-SERVER.md](../../DEPLOY-SERVER.md) |
| Contractul de sincronizare cu telefonul | [`docs/speleoloc-sync/`](../../speleoloc-sync/) |
| Orice despre cod | Nu aici. Acest ghid nu are conținut pentru programatori, prin proiectare |

---

[← Înapoi la ghid](README.md)
