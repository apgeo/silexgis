# Nori de puncte (scanări LiDAR)

🇬🇧 [English](../../features/point-clouds.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Topografii, drumuiri și modele 3D](surveys-and-models.md) ·
[Vizualizarea 3D](3d-view.md) · [Spațiu de stocare](../admin/storage.md)

---

Un scaner laser purtat printr-o peșteră înregistrează milioane de puncte ale pereților ei.
SilexGIS păstrează o astfel de **scanare** printre topografiile peșterii, o desenează de sine
stătător și în scena 3D și o plasează pe hartă. Primește o scanare în două forme:

- **un nor de puncte gata făcut** — un singur fișier `.laz` sau `.las`, produs de programul care a
  procesat înregistrarea scanerului;
- **o înregistrare brută de la un scaner MANDEYE** — dosarul scris de scaner, pe care serverul îl
  transformă singur în nor de puncte cu **HDMapping**, programul open-source de cartare pentru
  care sunt făcute înregistrările MANDEYE.

Totul începe pe **pagina unei peșteri**, în cardul **Modele topo 3D**.

---

## Încărcarea unui nor de puncte

**Încarcă nor de puncte** deschide un dialog cu două variante: *Un fișier nor de puncte* sau
*Dosarul unei înregistrări de scaner*.

Pentru **un fișier nor de puncte**, trage aici un fișier `.laz` sau `.las` și dă scanării un titlu
(este propus numele fișierului). Dialogul spune care este cel mai mare fișier acceptat de server
și refuză unul mai mare, sau un fișier de alt fel, înainte să trimită ceva. Deasupra butonului, un
scurt rezumat al [spațiului rămas în depozit](#spațiul-rămas-în-depozit) spune dacă fișierul
încape.

Fișierul este păstrat așa cum este. Antetul i se citește imediat; restul rulează pe server, iar
pagina de stare a încărcării arată fiecare etapă pe măsură ce trece prin ea: *Citirea antetului →
Estimare și rărire → Împărțire în dale*.

### Cât de mult din nor se afișează

Un nor de câteva sute de milioane de puncte este mai mult decât poate desena un browser. Înainte
de împărțirea în dale, serverul **estimează** ce ar păstra rărirea și arată un tabel, **Cât de
mult se afișează**: întregul nor, apoi un punct la atâția centimetri, fiecare rând cu punctele
lui, dalele lui și timpul pe care l-ar lua împărțirea în dale. Cel mai fin detaliu care se
încadrează în pragurile instalării este marcat *Sugerat*.

- Un nor sub praguri este împărțit în dale imediat.
- Un nor peste ele **se oprește după estimare** și întreabă: alege un rând, apoi **Împarte în
  dale cu această alegere**. Întregul nor poate fi ales în continuare; recomandarea spune la ce
  să te aștepți.
- O scanare deja împărțită în dale poate fi împărțită din nou cu altă alegere — **Împarte din nou
  în dale cu această alegere** — fără să o încarci din nou.

Rărirea schimbă doar ce se desenează. Fișierul original rămâne așa cum a fost încărcat.

### Cardul scanării

Pagina de stare și cardul **Topografii** al peșterii arată un card **Nor de puncte**: formatul,
câte puncte are, ce poartă fiecare punct (culoare, intensitate sau doar poziții), întinderea în
metri, dacă fișierul numește un sistem de coordonate, programul cu care a fost produs și cât de
mari sunt dalele lui. **Vezi scanarea** deschide [scanarea de sine stătătoare](#vizualizarea-unei-scanări).

## Înregistrări brute de la un scaner MANDEYE

Alege **Dosarul unei înregistrări de scaner** și trage aici dosarul scris de scaner (sau dă clic
ca să-l alegi). Dialogul enumeră ce va trimite și cât de mare este:

- bucățile `lidarNNNN.laz`, fiecare cu jurnalul ei `imuNNNN.csv` alături — **amândouă sunt
  obligatorii**, în perechi;
- opțional, fișierele `.sn` ale senzorilor, un jurnal de satelit (`.gnss` sau `.nmea`) și
  `calibration.json`.

**Pozele camerelor și alte dosare sunt lăsate deoparte** și nu se trimit. Dialogul spune ce nu
este în regulă înainte să se trimită ceva: o bucată fără jurnal IMU, două fișiere cu același nume,
un fișier gol, prea multe fișiere sau mai mult decât limita instalării (implicit 20 GB și 5.000 de
fișiere pe înregistrare). Spune și de cât spațiu are nevoie procesarea pe discul serviciului de
procesare cât rulează — cam de douăzeci de ori cât înregistrarea — iar titlul este luat din
numele dosarului.

Alege un **Profil de odometrie** — setările cu care rulează HDMapping (vezi
[profilurile de procesare](#profiluri-de-procesare)). Profilul ales ultima dată este preselectat,
iar dialogul spune dacă diferă de valorile implicite HDMapping și la câte setări.

### Ce face serverul cu ea

După ce fișierele au sosit, serverul le citește și completează un card **Înregistrare**: bucățile
lidar, senzorii, dacă există o calibrare și un jurnal de satelit, durata, numărul de puncte și
mărimea. Apoi procesarea continuă în etape:

*Calcul odometrie → Exportul norului de puncte → Verificarea exportului*, iar de acolo exact ca
pentru un nor încărcat: estimare, rărire dacă e nevoie, împărțire în dale.

- **Norul exportat** devine fișierul scanării, iar **traiectoria** urmată de scaner este păstrată
  lângă el.
- **Fiecare fișier al înregistrării este păstrat** așa cum a fost încărcat, ca să poată fi
  procesat din nou mai târziu.
- O procesare poate fi **pornită din nou de la un pas ulterior** — de exemplu odometria, cu alt
  profil — și refolosește ce au produs pașii de dinainte.
- Un serviciu de procesare oprit în mijlocul unui pas raportează pasul ca **întrerupt**, în loc
  să-l pornească din nou în tăcere.

Procesarea unei înregistrări brute are nevoie de serviciul HDMapping al instalării, care este
opțional. Acolo unde nu rulează, încărcarea este păstrată și se încheie cu o notă care spune că
procesarea nu este disponibilă aici.

## Profiluri de procesare

**Setări → Scanări · Profiluri de procesare** păstrează seturi de setări cu nume, după care
rulează un pas de procesare: odometria HDMapping, apoi estimarea, rărirea și împărțirea în dale
ale unui nor.

- **Trei niveluri.** *Ale mele* (văzute doar de tine), *Grupuri speologice* (ale unui grup de
  speologie) sau *Toată instalarea*. Crearea și modificarea profilurilor de grup și de instalare cer dreptul
  **scan-profiles** (în interfață, domeniul *Scanări · Profiluri de procesare*); unul personal nu
  cere nimic în afară de un cont.
- **Profilurile incluse** vin cu instalarea — valorile implicite HDMapping, o previzualizare
  rapidă și un profil pentru galerii înguste de peșteră, ale cărui valori urmează încă să fie
  reglate pe înregistrări din peșteri. Nu pot fi modificate; **duplică** unul ca să-l schimbi.
- **Editorul este construit din lista de setări a pasului însuși**, grupate cum le grupează
  HDMapping, cu fiecare valoare implicită afișată și valorile schimbate de tine evidențiate. Pentru
  HDMapping, lista vine din versiunea instalată efectiv, așa că un HDMapping mai nou își aduce
  setările noi cu el.
- **Importă** un fișier de parametri HDMapping (`.toml`) sau un profil salvat ca JSON. Înainte să
  se păstreze ceva, un raport spune, cheie cu cheie, ce a fost preluat, ce era necunoscut și a fost
  lăsat deoparte, ce lipsea și a fost completat cu valorile implicite și ce a fost refuzat pentru
  că era de tip greșit sau în afara limitelor; numește și o diferență de versiune HDMapping.
  **Exportul** scrie profilul înapoi într-un fișier — ca `.toml` propriu HDMapping pentru
  odometrie, ca JSON pentru orice pas.

Profilul folosit la fiecare rulare este scris în evidența acelei rulări, așa că o scanare poate
spune oricând ce setări au produs-o.

## Vizualizarea unei scanări

**Vezi scanarea** o desenează **de sine stătător**, în metrii ei proprii, nu pe hartă: trage ca
s-o rotești, derulează ca să te apropii, **Toată scanarea** ca s-o încadrezi din nou întreagă.
**Colorare după** oferă culoarea înregistrată, intensitatea sau înălțimea, iar punctele sunt
umbrite astfel încât forma unei galerii se citește și fără culoare. **Măsoară** dă distanța dintre
două puncte pe care dai clic.

Cât de fin este desenată o scanare este o setare a **acestui browser**: **Setări → Avansat → Nori
de puncte → Detaliul punctelor** (redus, normal sau ridicat). Un detaliu mai fin descarcă mai mult
și ține mai mult în memoria grafică; un telefon sau o tabletă pornesc de la redus.

## Plasarea unei scanări pe hartă

O scanare este utilă și singură, dar în
[vizualizarea 3D](3d-view.md#norii-de-puncte-ai-peșterilor-din-vedere) apare doar după ce este
**plasată**. Rândul scanării pe pagina peșterii spune cum este plasată, iar **Elimină plasarea** o
scoate din nou de pe hartă — rămâne vizibilă de sine stătător. Plasarea sau corectarea unei
scanări nu o împarte niciodată din nou în dale.

Există patru feluri, de la cel mai puțin la cel mai mult de lucru:

1. **Din coordonatele fișierului.** Când antetul unui nor numește un sistem de coordonate,
   scanarea este plasată automat după ce este împărțită în dale — *Plasată din coordonatele
   fișierului*.
2. **Din pozițiile prin satelit ale înregistrării.** O înregistrare brută cu un jurnal `.gnss` și
   fără altă plasare primește o **primă plasare** din pozițiile ei și din traiectorie: unde era și
   încotro era îndreptată. Un jurnal `.nmea` singur nu ajunge, pentru că nu poate fi potrivit cu
   ceasul propriu al scanerului. *Plasat după pozițiile prin satelit ale înregistrării* — un punct
   de pornire pe care punctele de control îl rafinează apoi.
3. **După intrare și azimut.** **Plasează pe hartă** pune punctul zero propriu al scanării într-o
   poziție și la o altitudine — este propusă intrarea principală a peșterii — și o rotește astfel
   încât nordul ei propriu să fie îndreptat pe un azimut pe care îl dai, în grade față de nordul
   geografic. *Plasată manual*.
4. **După puncte de control.** **Plasează după puncte de control** împerechează puncte ale
   scanării cu stații ale **modelului de drumuire curent** al peșterii. Pentru fiecare pereche
   apasă **Alege**, dă clic pe punctul de pe scanare și numește stația pe care se află; adaugă cel
   puțin două perechi. **Potrivește și plasează** găsește poziția și azimutul care se potrivesc
   cel mai bine perechilor și poate **potrivi și o mică înclinare** (de la trei perechi). Apoi
   arată, pentru fiecare pereche, cât de departe de stația ei ajunge punctul scanării — pe
   orizontală, pe verticală și în total — și RMS-ul tuturor. O pereche mai depărtată decât pragul
   instalării (jumătate de metru, dacă nu spune altfel) este semnalată: de obicei a fost
   împerecheată cu stația greșită. *Plasat după puncte de control · RMS …*.

## Cine ce vede

O scanare este o imagine a interiorului unei peșteri, iar sistemul de referință al scanerului este
al lui propriu: **dalele sunt desenate în metrii proprii ai scanării și nu poartă nicio
poziție**. Așadar:

- Cardul scanării, cifrele ei, scanarea de sine stătătoare și descărcarea norului procesat urmează
  **regulile obișnuite pentru fișierele topografice ale peșterii**.
- **Unde se află scanarea** — plasarea ei, scanarea în scena 3D, traiectoria — cere dreptul de a
  vedea **locația exactă** a peșterii, ca orice altă cale care ar dezvălui poziția unei peșteri
  protejate.
- Un **original care poartă el însuși coordonate reale** — un nor al cărui antet numește un sistem
  de coordonate sau o înregistrare cu jurnal de satelit — cere dreptul de locație exactă și pentru
  descărcare. Cardul spune de ce nu există link de descărcare atunci când este cazul.

Vezi [Protecția locațiilor](../admin/location-protection.md) pentru mecanism.

## Spațiul rămas în depozit

Norii de puncte și înregistrările brute sunt mari, iar o instalare limitează cât se poate stoca:
un total, un total separat pentru scanări dacă îl stabilește și o cotă pe cont. Dialogul de
încărcare, estimarea și cererea de a procesa din nou o scanare arată toate **spațiul rămas** — tot
depozitul, norii de puncte, contul tău — și ce ar adăuga această încărcare sau rulare. Ce nu
încape este refuzat înainte să se trimită sau să pornească ceva, cu cât lipsește și sub ce limită.
Administratorii sunt avertizați pe măsură ce depozitul se umple; vezi
[Spațiu de stocare](../admin/storage.md).

## Încă nu

- Procesarea unei înregistrări **pe alt calculator** (de exemplu în programul desktop propriu
  HDMapping) și aducerea înapoi a rezultatului.
- O **drumuire** și **imagini de plan și profil** desenate din traiectorie.
- **Pereți** făcuți dintr-o scanare, pentru stratul de pereți al scenei 3D.
- Unirea mai multor înregistrări într-o singură scanare.

---

Înrudite: [Topografii, drumuiri și modele 3D](surveys-and-models.md) · [Vizualizarea 3D](3d-view.md) ·
[Contul și setările](account-and-settings.md)
