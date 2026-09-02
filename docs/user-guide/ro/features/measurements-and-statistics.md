# Măsurători și statistici

🇬🇧 [English](../../features/measurements-and-statistics.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite:
[Topografii și modele](surveys-and-models.md) · [Ture](trips.md)

---

Aplicația calculează destul de multe și este neobișnuit de atentă să spună *din ce* este
calculată o cifră și când refuză cu totul să calculeze una. Refuzul este partea utilă: un gol
este o afirmație că nimic nu a spus, iar un zero ar fi o afirmație că ceva a spus.

---

## Ce măsoară o topografie

Pe pagina unei peșteri, **Statistici din topografie** — măsurate din linia topografică.

| Cifră | |
|---|---|
| **Lungime cartată** | Totalul |
| **Lungime în plan** | Proiectată la orizontală |
| **Extindere verticală** | |
| **Cea mai mare deschidere** | Cea mai mare distanță în linie dreaptă |
| **Verticalitate / Orizontalitate** | |
| **Liniaritate / Sinuozitate** | Cât de mult șerpuiesc galeriile |
| **Segmente / Trasee** | |
| **Punctul cel mai înalt / cel mai jos** | |
| **Raport lungime–adâncime** | |

Plus **sinuozitatea celor mai lungi galerii**, fiecare cu lungimea ei și cu distanța în linie
dreaptă.

### De unde vin cifrele, declarat

Fiecare set de cifre își declară baza:

- *„Măsurat din topografia compilată, folosind chiar indicatorii topografului pentru a decide ce
  vizări sunt galerie. Așa sunt definite aceste cifre."*
- *„Aproximat din poligonația stocată, pentru că această peșteră nu are o topografie compilată cu
  indicatori per vizare. Forma liniei ține locul indicatorilor topografului."*
- *„Măsurat din topografia «X»"* — pentru că o peșteră poate avea mai multe topografii încărcate,
  iar cifre luate din două topografii diferite nu sunt aceeași măsurătoare.

### Fără altitudini înseamnă fără cifre verticale

*„Această linie topografică nu poartă altitudini — un desen în plan al unei peșteri nu este o
peșteră plată."* Fiecare cifră verticală este lăsată goală, nu afișată ca zero.

### Calculat față de declarat

Secțiunea **Morfometrie** a peșterii conține cifre tastate de cineva. Statisticile le arată pe
amândouă:

- *„n calculat; nimic scris în fișă cu care să se compare."*
- *„n calculat, m în fișă, o diferență de d."*

Iar când nu se potrivesc, o notă: **niciuna nu este automat cea greșită.** O morfometrie tastată
este adesea mai veche decât topografia; o topografie poate fi și ea parțială. Aplicația
semnalează dezacordul și lasă judecata în seama dumneavoastră.

---

## Orientarea galeriilor

O **diagramă-roză** a direcțiilor galeriilor pe sectoare, cu nordul în sus, ponderată fie **după
lungime**, fie **după numărul de vizări**. Fiecare direcție este desenată de ambele părți ale
diagramei, pentru că o galerie care merge într-un sens merge și în celălalt. Raportează
**direcția medie**.

Alături, **înclinarea** — panta vizărilor topografice, cu axă de numărare liniară sau
logaritmică, și o **pantă medie față de orizontală** care ignoră dacă o vizare urcă sau coboară.

Două refuzuri de reținut:

- **Panta nu poate fi calculată** dacă linia topografică nu poartă altitudini. Desenarea ei
  oricum ar arăta peștera drept orizontală peste tot, ceea ce este o afirmație pe care n-a
  făcut-o nimeni.
- **Nicio direcție nu poate fi calculată** dacă fiecare vizare fie coboară vertical, fie urcă
  vertical, fie este marcată drept galerie deja cartată pe altă linie.

---

## Formă măsurată (morfometrie)

Pentru un element desenat ca un contur — o dolină, o depresiune carstică, o zonă de lucru:

**Suprafață · Perimetru · Circularitate · Axa lungă · Axa scurtă · Alungire · Direcția axei
lungi · Centru**

Două observații pe care le face chiar panoul:

- Cifrele sunt **măsurate în metri, în sistemul de coordonate de lucru al instalării**, nu în
  gradele în care este stocat conturul.
- **Direcția axei lungi este o aliniere, nu o direcție**: merge de la 0° la 180°, așa că 170° și
  350° sunt aceeași aliniere.

Dacă conturul se autointersectează, nu are suprafață sau formă măsurabilă, și vi se spune să-l
redesenați în loc să primiți o cifră.

---

## Cea mai mică distanță

Cât de aproape ajung două peșteri una de alta. Alegeți a doua peșteră din pagina primei.

Primiți **cea mai scurtă distanță**, componentele ei **orizontală** și **verticală**, și un
**azimut**. Linia este desenată pe hartă (în plan) și în vizualizarea 3D (trecând prin rocă, la
adâncimile la care i-au fost ridicate capetele).

> Cifrele orizontală și verticală sunt **cele două componente ale aceleiași linii** — nu cea mai
> scurtă distanță pe orizontală și cea mai scurtă pe verticală, care sunt perechi diferite de
> puncte.

Refuză în trei cazuri, fiecare numit:

- **Nu aveți voie să localizați ambele peșteri.** O distanță între două peșteri le localizează pe
  fiecare pornind de la cealaltă, așa că se dă doar unui cititor care poate vedea exact unde sunt
  amândouă.
- **Cel puțin una nu are linie topografică** de măsurat.
- **Cel puțin una a fost desenată în plan, fără adâncimi înregistrate**, deci nu există o distanță
  onestă în trei dimensiuni.

---

## Distribuții carstice

Peste peșterile de pe o pagină de listă: **Distribuții**.

| Vedere | |
|---|---|
| **Lungime** | Histogramă a lungimii cartate |
| **Lungime față de adâncime** | Nor de puncte, cu o ajustare care raportează panta și R² |
| **Rang–mărime** | Cu ajustare de tip lege de putere și exponentul ei |
| **Pe tipuri** | Numărători |

Nota despre domeniu este partea importantă: *„Calculat din cele n din m peșteri de pe această
pagină care au o lungime cartată. **Peșterile fără una lipsesc, nu sunt numărate ca zero.**"*

---

## Statistici din ture

O persoană, o peșteră și un grup de speologie își primesc fiecare totalurile:

**Ture · Persoane · Locuri · Primele vizite · Ore în subteran · Cartat · Coardă · Stații
topografice · Ture cu incident · Fotografii**

Citiți cele două avertismente pe care le tipărește:

- **„Numărat peste turele pe care le puteți citi."** Cineva cu alt acces vede totaluri diferite
  pentru același subiect, **și amândoi au dreptate**.
- **„Orele acoperă cele n din m dăți când cineva a mers și s-au notat orele de intrare și
  ieșire."**

Nimic nu este stocat. Orele se calculează din orele înregistrate, peste câte zile a durat efectiv
tura. O **primă vizită** este pur și simplu cea mai veche tură care a dus pe cineva undeva — așa
că tastarea unei ture mai vechi din arhivă *corectează* cifrele, în loc să lase în urmă un
indicator învechit.

Oricare dintre cele trei poate fi **salvată ca foaie de calcul**, care poartă exact ce a purtat
ecranul și spune ale cui sunt totalurile — pentru că un fișier se redirecționează și se citește
luni mai târziu.

> **Nu puteți cere turele unei persoane numind-o.** Aceasta este o întrebare despre o persoană,
> asamblată din înregistrări pe care cel care întreabă s-ar putea să nu aibă niciodată voie să le
> citească. Turele proprii sunt la *Jurnale de tură → Turele mele*, calculate din cine este
> autentificat.

---

Înrudite: [Topografii și modele](surveys-and-models.md) · [Ture](trips.md) ·
[Peșteri și intrări](caves-and-entrances.md)
