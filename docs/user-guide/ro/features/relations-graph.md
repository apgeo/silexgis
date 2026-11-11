# Graful relațiilor

🇬🇧 [English](../../features/relations-graph.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Legături între înregistrări](links.md)

---

Graful relațiilor desenează **un element și tot ce este legat de el** ca puncte și linii, ca să
vezi cum se așază o peșteră, o tură, un raport sau o persoană printre celelalte înregistrări. Îl
deschizi dintr-un element și explorezi spre exterior, câte un pas.

El doar **arată** relațiile. Din graf nu se creează, nu se schimbă și nu se șterge nimic; relațiile
le faci și le modifici unde le făceai dintotdeauna, pe pagina elementului.

---

## Cum îl deschizi

| De unde | Cum |
|---|---|
| Orice pagină cu secțiune de legături (peșteră, document, tură, expediție, …) | **Arată ca graf**, în antetul secțiunii |
| Panoul de selecție de pe hartă, pentru o peșteră sau alt obiect | **Arată ca graf**, lângă celelalte butoane |
| Meniul din stânga | **Unelte → Graful relațiilor**, apoi cauți elementul de la care pornești |

Dacă îl deschizi dintr-un element, acela devine **primul element**.

## Ce arată graful

Fiecare element este un punct colorat cu un mic desen a ceea ce este: o peșteră, o intrare, o linie
topo, un reper pentru orice alt loc, un document, o tură, o persoană, un club, o vedere de hartă, un
model topo, un fișier de date geografice, un dulap, cortul unei expediții, o etichetă sau un fișier
atașat. Un element pe care nu ai voie să-l deschizi este un cerc gol, punctat, cu un lacăt, și nu are
nume.

La început: primul element și ce este legat direct de el. Ca să mergi mai departe, alege un vecin și
apasă **Extinde elementul ales** (sau dă dublu clic pe el). Tot ce era pe ecran rămâne la locul lui, iar
elementele noi apar lângă cel extins.

Fiecare fel de relație este desenat, cu culoarea lui:

> Legături între resurse · Legături între obiecte · Ierarhie · Atașamente · Participanți la ture ·
> Expediții · Dulapuri · Etichete · Apartenență la grupuri · Modele topo

Când un element are mai multe relații de un fel decât încap într-un pas, lângă el apare un punct
**+N în plus**. Apasă-l ca să încarci următorul lot. **Legenda** de sub desen explică desenele și
culorile.

După ce selectezi un element, **Detalii** din bara de comenzi deschide un panou cu titlul elementului, un
buton care îi deschide pagina și relațiile lui din graf. Dacă alegi o linie, vezi cum se raportează
cele două capete și, pentru o legătură, un buton care deschide pagina legăturii.

**Revino la primul element** te întoarce la elementul de pornire. **Scoate din graf** ascunde un
element pe care nu vrei să-l vezi; nu șterge nimic.

## Filtre

Panoul **Filtre** enumeră fiecare tip de relație și fiecare tip de obiect încărcat, cu numărul lui. Poți
porni sau opri **mai multe deodată**:

- debifează un tip de relație ca să-i ascunzi liniile și, odată cu ele, elementele rămase fără legătură
  cu primul element;
- deschide un tip ca să oprești o singură relație din el, de pildă o anumită relație dintre legături;
- debifează un tip de obiect ca să le ascunzi pe toate cele de felul acela;
- **Doar** păstrează un singur tip și le ascunde pe celelalte; **Arată tot** readuce totul.

Filtrele ascund, nu șterg: le repornești și elementele revin. Numerele sunt ale celor încărcate până
acum, nu ale întregului registru.

## Căutare

- **Caută în graf** găsește după titlu sau subtitlu elementele deja din graf. Majusculele și
  diacriticele nu contează. Potrivirile sunt evidențiate, iar **Enter** trece la următoarea.
- Pe pagina goală, **Caută un element de explorat** caută în tot registrul un element de pornire. Vezi
  doar ce ai voie să citești.

## Lista de lângă desen

Lista **Elementele din graf** arată aceleași elemente grupate pe tipuri, fiecare cu relațiile lui.
Este calea de a folosi graful cu tastatura sau cu un cititor de ecran: alegerea unui rând e același
lucru cu apăsarea punctului, iar lista are propriile butoane **+N în plus**. Ține cont de filtre.

## Elemente restricționate

Dacă o legătură unește elementul tău cu ceva ce **nu ai voie să vezi**, graful desenează un punct gri
**Restricționat**. Nu are titlu și **nu poate fi extins**; graful îți spune doar că legătura mai are un
membru. Două legături care duc la elemente ascunse diferite dau două puncte, iar ce se află în spatele
lor nu se dezvăluie.

## De ce nu apare ceva

- **Nu ai voie să-l citești.** Elementele din afara accesului tău nu sunt desenate niciodată și nici nu
  intră în numărătoarea „+N în plus”.
- **Un filtru îl ascunde**, sau ascunde singurul drum spre el. Verifică **Filtre**.
- **Este mai departe.** Se încarcă un singur pas; extinde vecinul.
- **Locația lui este protejată.** Graful nu poartă coordonate, iar relațiile care ar dezvălui poziția
  unei peșteri protejate rămân ascunse oricui nu are voie să-i vadă poziția exactă.
- **Nu este o relație.** Două înregistrări aflate doar aproape pe hartă nu sunt înrudite decât dacă o
  legătură, o ierarhie sau una dintre relațiile enumerate o spune.

## Ce nu face graful niciodată

- Nu schimbă nicio înregistrare.
- Nu arată nimic ce nu ai fi putut deschide oricum.
- Nu arată poziții sau coordonate.

---

Înrudite: [Legături între înregistrări](links.md) · [Căutare și filtre](search-and-filters.md)
