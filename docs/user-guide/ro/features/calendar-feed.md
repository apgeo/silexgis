# Abonarea la calendar

🇬🇧 [English](../../features/calendar-feed.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Evenimente și calendar](events-and-calendar.md) ·
[Contul și setările](account-and-settings.md)

---

Turele, taberele și evenimentele tale pot apărea în calendarul pe care îl folosești deja —
pe telefon, pe calculator, în orice aplicație care citește un abonament la calendar. Aplicația îți
dă o **adresă de flux**; aplicația de calendar o interoghează, arată ce găsește și continuă să o
interogheze, astfel că o dată care se mută, o tură la care ești adăugat sau un eveniment anulat
te urmează acolo.

## Înainte să funcționeze

Un administrator trebuie să pornească fluxurile: **Administrare → Mesagerie → Protecția locațiilor →
„Permite membrilor să se aboneze la calendarul lor din exterior."** Până atunci secțiunea descrisă
mai jos nu se afișează deloc. Pe o instalare nouă fluxurile sunt oprite, pentru că a le oferi este o
decizie despre cât de departe pot călători datele clubului.

## Crearea unei adrese

**Setări → Cont → Flux de calendar.**

1. Dă adresei un nume — dispozitivul pe care va ajunge, de pildă *Telefon* sau *Laptopul de la
   serviciu*. Câte o adresă pentru fiecare dispozitiv, ca să poți revoca una fără să le atingi
   pe celelalte.
2. **Creează o adresă de flux.**
3. **Copiaz-o acum.** Se afișează o singură dată. Aplicația păstrează doar o amprentă a ei și nu o
   poate afișa din nou — dacă o pierzi, revoc-o și creează alta.

> **Adresa este un secret.** Oricine o are îți poate citi calendarul fără autentificare. Trateaz-o
> ca pe o parolă: nu o publica, nu o trimite pe e-mail și, dacă ajunge la altcineva,
> **revoc-o** aici și creează una nouă.

## Abonarea

Lipește adresa acolo unde aplicația ta de calendar adaugă un abonament — se numește,
după caz, *calendar abonat*, *calendar din URL*, *calendar de internet* sau *adaugă după URL*:

| Unde | Cum |
|---|---|
| **iPhone / iPad** | Setări → Calendar → Conturi → Adaugă cont → Altele → Adaugă calendar abonat |
| **Calendar pe macOS** | Fișier → Abonament nou la calendar |
| **Google Calendar** (pe web) | Alte calendare → + → Din URL. Google interoghează rar — ore, uneori o zi |
| **Outlook** | Adaugă calendar → Abonare de pe web |
| **Thunderbird** | Calendar nou → În rețea → iCalendar (ICS) |
| **Android** | Majoritatea calendarelor de telefon arată abonamentele contului Google; unele permit adăugarea directă a unui URL |

Cât de des recitește calendarul adresa este alegerea aplicației, nu a noastră. Telefoanele
interoghează de la câteva minute la câteva ore; nu există o cale de a le împinge o schimbare mai
devreme.

## Ce poartă fluxul

Câte o intrare pentru fiecare:

- **tură** la care participi — numit în echipă sau invitat să vii,
- **tabără** a cărei listă consemnează o ședere a ta,
- **eveniment** despre care ai fost întrebat și pe care nu l-ai refuzat.

Fiecare intrare are **titlul**, **zilele** (și orele, pentru un eveniment cu oră) și o **legătură
înapoi** către pagina din aplicație. O intrare anulată este marcată ca anulată, nu ștearsă pe tăcute,
ca să vezi că a fost anulată.

Atât. **Niciun loc, nicio coordonată, niciun participant, nicio descriere** nu intră într-un flux
— nu pentru că ar fi filtrate, ci pentru că fluxul nu este construit să le poarte. O aplicație de
calendar de pe telefon, sau serviciul de calendar din spatele ei, nu poate afla niciodată din flux
unde se află o peșteră, orice ar adăuga cineva mai târziu la tură.

Fluxul arată **la ce participi**, nu tot ce ai putea citi: pagina de calendar din aplicație
arată fiecare tură și fiecare eveniment pe care le poți vedea; fluxul le arată doar pe cele care
te privesc.

## Ce respectă fluxul

Fluxul este citit **ca tine**, la fiecare interogare. Nu ține minte niciodată ce aveai
voie să vezi când ai creat adresa:

- o tură pe care nu o mai poți citi dispare din flux,
- un cont blocat nu primește niciun răspuns,
- o adresă pe care o revoci încetează să răspundă imediat,
- iar dacă un administrator oprește fluxurile, toate adresele se opresc deodată.

## Revocarea unei adrese

**Setări → Cont → Flux de calendar** îți listează adresele după nume și dată — niciodată adresa în
sine. **Revocă** adresa pierdută sau nefolosită. O aplicație de calendar care o are încă pur și
simplu nu se va mai actualiza; șterge abonamentul și acolo, ca să nu mai întrebe.

---

Înrudite: [Evenimente și calendar](events-and-calendar.md) ·
[Contul și setările](account-and-settings.md) ·
[Protecția locațiilor](../admin/location-protection.md)
