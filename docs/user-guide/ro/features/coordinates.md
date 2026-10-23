# Coordonate și convertorul

🇬🇧 [English](../../features/coordinates.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Peșteri și intrări](caves-and-entrances.md) ·
[Contul și setările](account-and-settings.md)

---

Orice poziție pe care aplicația o arată sau o copiază este scrisă așa cum ai ales să o citești, și
oriunde scrii o poziție o poți scrie în oricare dintre aceste feluri. Pozițiile sunt păstrate
întotdeauna ca longitudine și latitudine WGS84; formatele sunt doar felul în care sunt scrise și
citite.

## Formatele

| Format | Exemplu (vârful Moldoveanu) | Setările lui |
|---|---|---|
| Grade zecimale | `45.59944°N 24.73617°E` | numărul de zecimale; cu N/S/E/V, sau cu semn și latitudinea întâi |
| Grade, minute zecimale | `45°35.966′N 24°44.170′E` | zecimalele minutelor |
| Grade, minute, secunde | `45°35′58.0″N 24°44′10.2″E` | zecimalele secundelor |
| UTM | `35T 323446 E 5052036 N` | zona (fiecare poziție în zona ei, sau o zonă păstrată); 1 m, 10 m sau 100 m |
| MGRS | `35T LL 23447 52037` | de la 1 m la 10 km; cu sau fără spații |
| Stereo 70 (România) | `E 479541 N 455555` | de la 1 m la 100 m; E/N sau X (nord) / Y (est), ca în topografie |
| Caroiajul elvețian LV95 și LV03 | `E 2600000 N 1200000` (Berna) | precizia |
| Plus Code | `8GQ6HPXP+QF` | lungimea codului (8 până la 13) |

**Setări › Coordonate** alege formatul în care sunt arătate pozițiile, celelalte formate în care
este listată o poziție arătată în detaliu și setările fiecărui format, cu o previzualizare pe
vârful Moldoveanu. Setările îți urmează contul în orice browser te autentifici.

- **Stereo 70** se convertește din WGS84 cu transformarea obișnuită în șapte parametri. Este bună
  la câțiva metri — destul pentru a găsi o intrare, nu pentru un reper topografic. Este oferită
  doar în România.
- **Caroiajele elvețiene** sunt bune la aproximativ un metru și sunt oferite doar în Elveția și
  Liechtenstein.
- **UTM cu zonă păstrată**: o echipă care lucrează peste marginea unei zone poate păstra o singură
  zonă, ca toate pozițiile de lângă margine să fie scrise în același caroiaj. Departe de acea zonă
  se folosește zona proprie a poziției.
- **Plus Codes** sunt întotdeauna codul complet (global); un cod scurt are nevoie de un oraș din
  apropiere ca să fie citit înapoi, iar aplicația nu are o listă de orașe.

Un format care nu are referință pentru o poziție — caroiajul elvețian în România, UTM lângă un
pol — spune **Nu este disponibil aici**.

## Alte formate

Acolo unde o poziție este arătată în detaliu — o intrare selectată pe hartă, intrările de pe
pagina unei peșteri — **Alte formate** o deschide în fiecare format ales, fiecare cu butonul lui
de copiere, iar **Deschide în convertorul de coordonate** o trimite convertorului.

## Poziții protejate

Intrarea unei peșteri protejate este arătată, cuiva care nu o poate vedea exact, la o poziție
mutată pe caroiajul de protecție. Ea este scrisă **în fiecare format** nu mai fin decât acel
caroiaj: grade zecimale la sutime, minute întregi, UTM și caroiajele naționale la kilometru, MGRS
prin pătratul de 10 km, Plus Codes la șase caractere. Copierea copiază exact ce se vede. O astfel
de poziție nu este trimisă convertorului.

## Scrierea unei poziții

Editorul de intrări și alegerea unui punct au un câmp **Scrie sau lipește o poziție**. Câmpul
citește oricare dintre formatele de mai sus și acceptă felul în care sunt scrise pozițiile în
realitate: `°` sau `º`, `'` sau `′`, `"` sau `″`, N/S/E/V înainte sau după număr, virgula ca semn
zecimal, UTM cu sau fără litera benzii, MGRS cu sau fără spații. Spune ca ce a citit textul și ce
a trebuit să presupună (două numere fără litere sunt citite ca latitudine, apoi longitudine). Un
text care poate fi citit în mai multe feluri oferă fiecare citire; formatul poate fi ales și
direct. **Folosește** completează longitudinea și latitudinea, care sunt cele salvate.

Câmpul este dezactivat și nu arată nimic cât timp poziția de lângă el este una pe care o poți
vedea doar aproximativ.

## Convertorul de coordonate

**Unelte › Convertor de coordonate** (la care se ajunge și din legăturile de mai sus) scrie o
poziție dată în orice format în toate formatele deodată, fiecare cu copiere. **Arată pe o hartă**
pune un marcaj temporar pe o hartă mică separată — nu se adaugă nimic pe harta ta și nu
se salvează nimic — iar **Deschide locul pe harta principală** centrează harta spațiului de lucru
acolo.

**Mai multe poziții** convertește deodată o listă lipită: câte o poziție pe linie, sau un tabel
(CSV) în care se alege coloana cu poziția — sau două coloane, latitudinea și longitudinea.
Fiecare linie este citită separat, iar o linie care nu poate fi citită spune de ce. **Descarcă
CSV** dă fiecare linie în fiecare format, cu latitudinea și longitudinea în grade zecimale. Se
convertesc cel mult 5000 de linii deodată.

Totul se petrece în browserul tău: nimic din ce scrii sau lipești în convertor nu este
trimis serverului.
