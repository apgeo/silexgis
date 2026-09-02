# Relief

🇬🇧 [English](../../features/terrain.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Vizualizarea 3D](3d-view.md) ·
Administrare: [Generarea reliefului](../admin/terrain-builds.md)

---

Relieful este denivelarea reală de sub [vizualizarea 3D](3d-view.md). Fără el globul este o sferă
netedă și peșterile stau sub o suprafață fără trăsături; cu el, stau sub versanți reali.

**Este complet opțional.** O instalare care nu-l vrea deloc nu este afectată, nu descarcă nimic
și nu contactează niciun server de elevație.

---

## Ce înseamnă pentru dumneavoastră ca cititor

Dacă instalarea are relief, veți observa că:

- scena 3D are dealuri,
- vederea decupată are sens, pentru că există ceva de decupat,
- adâncimile la care a fost măsurată o topografie se citesc față de o suprafață reală.

Dacă nu are, vizualizarea 3D funcționează în continuare. O spune limpede, în loc să eșueze.

## Ce costă să-l ai

Relieful este generat de un administrator, un dreptunghi pe rând. Acoperirea gratuită de 30 de
metri este obținută de aplicație, sau se furnizează rastere proprii. Fiecare generare este
transformată într-o piramidă de dale servită ca fișiere statice de aceeași instalare.

Două lucruri de știut ca ne-administrator:

1. **Generarea dalelor are nevoie de un serviciu suplimentar** pe care o instalare obișnuită nu îl
   rulează. Dacă a dumneavoastră nu îl rulează, generările se opresc la acel pas, iar pagina
   Relief explică exact asta, împreună cu singurul rând de adăugat în fișierul de mediu al
   instalării.
2. **Adâncimea este un compromis.** Fiecare nivel aproximativ împătrește și dalele scrise, și
   timpul necesar. Nivelul 13 este cam ce conține efectiv acoperirea de 30 de metri; a cere mai
   mult de la acele date nu adaugă decât spațiu pe disc.

## Atribuire

Fiecare generare poartă un **credit** — cui aparțin datele — și acesta este scris în suprafața
publicată. O generare nu trebuie să poarte niciodată un credit care nu este al ei. Dacă furnizați
rastere, trebuie să spuneți de unde vin.

## Înălțimi

O generare declară dacă numerele ei sunt **peste nivelul mării** (ortometrice) sau **peste
elipsoid**, iar pentru al doilea caz, înălțimea geoidului local. Greșeala ridică sau coboară
întreaga suprafață cu zeci de metri — ceea ce face apoi ca fiecare peșteră să pară greșit
poziționată pe verticală.

---

Pentru fluxul complet de generare, vedeți [Generarea reliefului](../admin/terrain-builds.md).
