# Abonarea la calendar

🇬🇧 [English](../../features/calendar-feed.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Evenimente și calendar](events-and-calendar.md) ·
[Contul și setările](account-and-settings.md)

---

Turele, taberele și evenimentele dumneavoastră pot apărea în calendarul pe care îl folosiți deja —
pe telefon, pe calculator, în orice aplicație care citește un abonament la calendar. Aplicația vă
dă o **adresă de flux**; aplicația de calendar o interoghează, arată ce găsește și continuă să o
interogheze, astfel că o dată care se mută, o tură la care sunteți adăugat sau un eveniment anulat
vă urmează acolo.

## Înainte să funcționeze

Un administrator trebuie să pornească fluxurile: **Administrare → Mesagerie → Protecția locațiilor →
„Permite membrilor să se aboneze la calendarul lor din exterior."** Până atunci secțiunea descrisă
mai jos nu se afișează deloc. Pe o instalare nouă fluxurile sunt oprite, pentru că a le oferi este o
decizie despre cât de departe pot călători datele clubului.

## Crearea unei adrese

**Setări → Cont → Flux de calendar.**

1. Dați adresei un nume — dispozitivul pe care va ajunge, de pildă *Telefon* sau *Laptopul de la
   serviciu*. Câte o adresă pentru fiecare dispozitiv, ca să puteți revoca una fără să le atingeți
   pe celelalte.
2. **Creează o adresă de flux.**
3. **Copiați-o acum.** Se afișează o singură dată. Aplicația păstrează doar o amprentă a ei și nu o
   poate afișa din nou — dacă o pierdeți, revocați-o și creați alta.

> **Adresa este un secret.** Oricine o are vă poate citi calendarul fără autentificare. Tratați-o
> ca pe o parolă: nu o publicați, nu o trimiteți pe e-mail și, dacă ajunge la altcineva,
> **revocați-o** aici și creați una nouă.

## Abonarea

Lipiți adresa acolo unde aplicația dumneavoastră de calendar adaugă un abonament — se numește,
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

- **tură** la care participați — numit în echipă sau invitat să veniți,
- **tabără** a cărei listă consemnează o ședere a dumneavoastră,
- **eveniment** despre care ați fost întrebat și pe care nu l-ați refuzat.

Fiecare intrare are **titlul**, **zilele** (și orele, pentru un eveniment cu oră) și o **legătură
înapoi** către pagina din aplicație. O intrare anulată este marcată ca anulată, nu ștearsă pe tăcute,
ca să vedeți că a fost anulată.

Atât. **Niciun loc, nicio coordonată, niciun participant, nicio descriere** nu intră într-un flux
— nu pentru că ar fi filtrate, ci pentru că fluxul nu este construit să le poarte. O aplicație de
calendar de pe telefon, sau serviciul de calendar din spatele ei, nu poate afla niciodată din flux
unde se află o peșteră, orice ar adăuga cineva mai târziu la tură.

Fluxul arată **la ce participați**, nu tot ce ați putea citi: pagina de calendar din aplicație
arată fiecare tură și fiecare eveniment pe care le puteți vedea; fluxul le arată doar pe cele care
vă privesc.

## Ce respectă fluxul

Fluxul este citit **ca dumneavoastră**, la fiecare interogare. Nu ține minte niciodată ce aveați
voie să vedeți când ați creat adresa:

- o tură pe care nu o mai puteți citi dispare din flux,
- un cont blocat nu primește niciun răspuns,
- o adresă pe care o revocați încetează să răspundă imediat,
- iar dacă un administrator oprește fluxurile, toate adresele se opresc deodată.

## Revocarea unei adrese

**Setări → Cont → Flux de calendar** vă listează adresele după nume și dată — niciodată adresa în
sine. **Revocați**-o pe cea pierdută sau nefolosită. O aplicație de calendar care o are încă pur și
simplu nu se va mai actualiza; ștergeți abonamentul și acolo, ca să nu mai întrebe.

---

Înrudite: [Evenimente și calendar](events-and-calendar.md) ·
[Contul și setările](account-and-settings.md) ·
[Protecția locațiilor](../admin/location-protection.md)
