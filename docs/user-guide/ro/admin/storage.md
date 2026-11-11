# Spațiu de stocare

🇬🇧 [English](../../admin/storage.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Nori de puncte](../features/point-clouds.md)

---

Norii de puncte și înregistrările brute ale scanerelor pot umple un disc într-o după-amiază. De
aceea o instalare **limitează** ce se poate stoca, își **avertizează** administratorii deplini pe
măsură ce depozitul se umple și are o pagină care spune **ce îl umple și ce ar putea pleca**.

**Administrare → Spațiu de stocare**, pentru **Administratorii deplini**.

---

## Limitele

Trei limite se stabilesc la configurarea instalării (vezi ghidul de instalare):

| Limită | Ce mărginește |
|---|---|
| **Tot depozitul** | Tot ce păstrează instalarea |
| **Nori de puncte** | Scanările și înregistrările, ca total separat (opțional — fără el, scanările împart limita întregului depozit) |
| **Pe cont** | Cât pot ocupa încărcările unei persoane |

Contează și mărimile deja promise: o încărcare mare care a fost deschisă, dar ale cărei fișiere
încă sosesc, își ține spațiul, așa că doi nori porniți unul lângă altul nu pot revendica amândoi
același loc.

**Ce nu încape este refuzat înainte să înceapă** — o încărcare când este deschisă și la sosirea
fiecărui fișier, o procesare înaintea primului ei pas care scrie ceva. Refuzul numește limita, cât
era nevoie și cât lipsește. Toată lumea vede spațiul rămas înainte să înceapă: dialogul de
încărcare a unui nor de puncte, estimarea și cererea de a procesa din nou o scanare arată tot
depozitul, norii de puncte și contul propriu al persoanei.

Discurile însele refuză doar ce chiar nu poate fi scris; un spațiu liber minim este un nivel de
avertizare, nu un refuz.

## Avertizări

O dată pe oră, instalarea compară ce este folosit cu fiecare limită și cu spațiul liber de pe
discul depozitului și de pe cel al serviciului de procesare. Când este depășit un nivel — un nivel
de **atenționare** și unul de **avertizare**, amândouă procente stabilite de instalare —
administratorii deplini sunt anunțați o singură dată, la categoria de stocare a
[notificărilor](../features/notifications.md) lor (*Spațiul de stocare se umple*).
Coborârea înapoi sub un nivel este înregistrată fără mesaj, așa că o urcare ulterioară este
anunțată din nou.

## Pagina

O dată pe zi, instalarea măsoară depozitul și păstrează rezultatul. Pagina arată ultima măsurare
— sau, la prima vizită, oferă **Măsoară acum** — cu:

- totalurile depozitului, ale norilor de puncte și ale discurilor, față de limitele lor;
- cât de repede a crescut depozitul în ultimele 7 și 30 de zile și **data la care ar fi plin** în
  acest ritm;
- ce îl umple, **după tipul fișierului, după peșteră și după cont**;
- **cele mai mari 50 de fișiere**;
- grupurile de fișiere care **s-ar putea șterge**.

## Ce se poate șterge

Fiecare grup are propriul buton **Șterge**, o confirmare care spune ce va pleca și cât spațiu
eliberează, iar după aceea o măsurare nouă:

| Grup | Ce conține |
|---|---|
| **Copii înlocuite ale norilor de puncte** | Copii rărite și dale pe care o scanare nu le mai folosește, pentru că a fost împărțită din nou în dale |
| **Lucrul rulărilor eșuate** | Ce a lăsat în urmă o procesare care a eșuat |
| **Produsele versiunilor vechi ale topografiilor** | Produse făcute din versiuni ale topografiilor pe care o încărcare mai nouă le-a înlocuit |
| **Încărcări expirate** | Încărcări care au fost deschise și nu au fost terminate niciodată |

**Originalele încărcate nu fac parte din niciun grup și nu sunt oferite niciodată** — o
înregistrare de scaner, un nor, un fișier topografic rămân așa cum au fost trimise, iar serverul
refuză să șteargă unul prin această pagină chiar dacă i se cere direct. Fiecare ștergere este
înregistrată în istoricul modificărilor.

---

Înrudite: [Nori de puncte](../features/point-clouds.md) · [Operațiuni de întreținere](maintenance.md)
