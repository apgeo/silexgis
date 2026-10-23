# Hărți raster georeferențiate

🇬🇧 [English](../../features/georeferenced-maps.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Spațiul de lucru al hărții](map-workspace.md) ·
[Geodate](geodata.md)

---

Foi geologice, hărți topografice vechi, hărți turistice, topografii scanate — orice este o
imagine cu o poziție — pot fi suprapuse peste hartă.

**Registru → Geodate → Hărți raster.**

---

## Încărcarea

Trage înăuntru **GeoTIFF-uri georeferențiate**. Sunt convertite în fundal în Cloud-Optimized
GeoTIFF: *În așteptare → Se procesează… → Gata* (sau *Eșuat*).

Fiecare hartă raster poartă:

| Câmp | |
|---|---|
| **Fel** | Geologică · Topografică · Turistică · Hartă de peșteră · Altele |
| **Opacitate implicită** | 0–1, opacitatea de la care pornește stratul |
| **Atribuire** | Cui aparține foaia |

O hartă raster poate fi **legată de o peșteră**, așa ajungând o topografie scanată lângă peștera
pe care o desenează.

Un plan sau un profil scanat al unei peșteri este desenat în cadrul propriu al topografiei, nu pe
un caroiaj de hartă — un profil nici nu poate fi așezat pe teren — așa că de obicei este mai bine arătat **lângă
topografia 3D** decât pe hartă: acolo nu are nevoie de georeferențiere, ci doar de câteva stații
arătate pe el, iar o echipă urmărită este desenată și pe el. Vezi
[Hărți scanate lângă model](surveys-and-models.md#hărți-scanate-lângă-model).

## Folosirea lor pe hartă

Rasterele gata apar în arborele de straturi la **Hărți georeferențiate** / **Hărți raster**.
Fiecare are propriul control de opacitate.

Unealta de **glisare** — *Compară rasterul cu harta de dedesubt* — pune un separator pe hartă ca
să poți trage rasterul înainte și înapoi peste harta de bază. Aceasta este unealta pentru „se
suprapune corect foaia geologică din anii '50?".

## În vizualizarea 3D

Rasterele pot fi întinse pe glob, cu o rezervă pe care scena o declară singură: **pe glob fiecare
foaie este aplatizată într-o singură imagine**, așa că se înmoaie la apropiere mare. Harta plană
citește fișierul însuși.

Dacă citești detalii fine de pe o foaie, folosește harta 2D.

---

Înrudite: [Spațiul de lucru al hărții](map-workspace.md) · [Vizualizarea 3D](3d-view.md)
