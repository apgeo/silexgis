# Flux: aducerea unei scanări laser a unei peșteri

🇬🇧 [English](../../workflows/bring-in-a-laser-scan.md) · 🇷🇴 **Română**

[← Fluxuri](README.md) · Referință: [Nori de puncte (scanări LiDAR)](../features/point-clouds.md) ·
[Vizualizarea 3D](../features/3d-view.md)

---

O echipă s-a întors dintr-o peșteră cu un scaner laser purtat în mână. Are fie dosarul de
înregistrare al scanerului, fie un nor pe care cineva l-a procesat deja pe un laptop. La sfârșitul
acestui flux scanarea stă printre topografiile peșterii, poate fi privită și măsurată de sine
stătător și apare în vizualizarea 3D acolo unde este peștera de fapt, aliniată cu stațiile topografiei.

**Înainte să începi**

- Peștera există în registru, iar topografia ei a fost încărcată ca **model de drumuire**
  (`.lox` sau `.3d`) și marcată drept curentă. Stațiile modelului de drumuire sunt cele după care
  se aliniază scanarea la pasul 5. Vezi
  [Topografii, drumuiri și modele 3D](../features/surveys-and-models.md).
- Știi, sau ai notat, **cel puțin două stații pe lângă care a trecut scanerul** — o stație
  topografică marcată în peșteră, un punct fix vopsit pe perete. Punctele depărtate între ele, și
  nu toate pe aceeași linie dreaptă, dau cea mai bună potrivire.
- Pentru o înregistrare brută, instalarea rulează procesorul opțional HDMapping — sau bifezi
  **Rulează odometria pe alt calculator** la pasul 1 și rulezi tu HDMapping; vezi
  [Odometria pe alt calculator](../features/point-clouds.md#odometria-pe-alt-calculator).

---

## Pasul 1 — Încarcă

Pe pagina peșterii, în cardul **Modele topo 3D**, apasă **Încarcă nor de puncte**.

- **Un nor** (`.laz` sau `.las`): alege *Un fișier nor de puncte*, trage fișierul, dă-i scanării
  un titlu.
- **O înregistrare brută MANDEYE**: alege *Dosarul unei înregistrări de scaner* și trage tot
  dosarul scris de scaner. Dialogul listează fișierele pe care le va trimite — bucățile `lidar` cu
  jurnalele lor `imu`, plus jurnalul de satelit și calibrarea, dacă există — și lasă deoparte
  pozele camerei. Alege un **Profil de odometrie**; cel folosit data trecută este deja ales. Dacă
  nu ai un motiv să-l schimbi, păstrează valorile implicite ale HDMapping.

Dialogul arată spațiul rămas în depozit înainte să trimiți. O înregistrare poate avea mulți
gigaocteți, iar legătura dintre tine și server poate fi lentă: pornește încărcarea acolo unde
conexiunea e bună și lasă pagina deschisă până se trimit fișierele.

## Pasul 2 — Lasă serverul să lucreze

Pagina de stare a încărcării arată fiecare etapă. Pentru o înregistrare, odometria vine prima și
este partea lungă — cam cât durează înregistrarea însăși, uneori mai mult. Apoi norul este
exportat, verificat, estimat și împărțit în dale.

Nu trebuie să stai cu ochii pe ea: închide pagina și revino mai târziu — cardul **Topografii** al
peșterii arată scanarea și stadiul rulării ei.

## Pasul 3 — Alege cât de mult se afișează

Un nor mare **se oprește după estimare** și te întreabă. Tabelul **Cât de mult se afișează**
listează norul întreg și variante mai rare — un punct la atâția centimetri — fiecare cu punctele,
dalele și timpul de împărțire. Rândul *Sugerat* este cel mai fin care rămâne în pragurile
instalării.

- Pentru a privi galeriile și pereții, rândul sugerat este de obicei cel potrivit.
- Pentru a măsura detalii fine, alege un rând mai fin sau norul întreg și așteaptă-te la o
  scanare mai grea în browser.

Apasă **Împarte în dale cu această alegere**. Poți reveni oricând ca să împarți din nou cu altă
alegere, fără să încarci nimic.

## Pasul 4 — Privește-o de sine stătător

**Vezi scanarea** o deschide în metrii ei, încă nu pe hartă. Rotește-o, încadreaz-o cu **Toată
scanarea**, colorează-o după înălțime ca să citești forma galeriilor și folosește **Măsoară** ca să
verifici o distanță pe care o cunoști — o lățime de galerie măsurată cu ruleta, lungimea unei
scări — înainte să te încrezi în rest.

Tot aici găsești punctele pentru pasul următor: caută locurile pe unde a trecut scanerul la
stațiile pe care le-ai notat.

## Pasul 5 — Plaseaz-o pe hartă

Uită-te mai întâi la rândul scanării de pe pagina peșterii. S-ar putea să fie deja plasată:

- *Plasată din coordonatele fișierului* — antetul norului numea un sistem de coordonate.
- *Plasată după pozițiile prin satelit ale înregistrării* — înregistrarea avea un jurnal `.gnss`.
  Este o primă plasare aproximativă, luată acolo unde scanerul a avut ultima poziție prin satelit
  — de obicei la intrare; rafineaz-o cu puncte de control.

Altfel, sau ca s-o faci exactă, apasă **Plasează după puncte de control**:

1. **Alege**, apoi dă clic pe un punct din scanare unde se află o stație și scrie numele stației așa
   cum îl dă modelul de drumuire.
2. **Adaugă o pereche** și repetă — cel puțin două perechi, trei sau mai multe răspândite prin
   scanare dacă poți.
3. Bifează **Potrivește și o mică înclinare** numai dacă scanerul se poate să nu fi stat la nivel
   și ai trei sau mai multe perechi care nu sunt pe aceeași linie.
4. Apasă **Potrivește și plasează**.

Dacă nu ai stații cu care să faci perechi, **Plasează pe hartă** cu intrarea peșterii și un azimut
pune totuși scanarea aproximativ la locul ei.

## Pasul 6 — Verifică potrivirea

După potrivire, fiecare pereche arată cât de departe de stația ei ajunge punctul — pe orizontală,
pe verticală și în total — și RMS-ul tuturor.

- **Câțiva centimetri până la un decimetru-două** este o potrivire bună pentru o scanare de mână
  față de o topografie cu busolă și ruletă.
- **O pereche semnalată** ajunge mai departe de stația ei decât pragul instalării (o jumătate de
  metru, implicit). De obicei punctul a fost asociat cu altă stație sau ales pe alt perete. Șterge
  perechea sau alege-o din nou și potrivește din nou.
- **Toate deplasate cam cu aceeași valoare** înseamnă de obicei că topografia și scanarea nu se
  înțeleg, nu perechile. Compară cu închiderea topografiei înainte să dai vina pe scanare.

O nouă plasare nu împarte niciodată scanarea din nou în dale, așa că încearcă de câte ori ai
nevoie.

## Pasul 7 — Vezi-o la locul ei

Deschide vizualizarea 3D deasupra peșterii și pornește **Scanări** în lista de straturi. Scanarea este
desenată sub teren, împreună cu drumuirile peșterii, pentru oricine are voie să vadă locația exactă
a peșterii. Dacă scena merge greu pe calculatorul tău, coboară **Detaliul punctelor** la
**Setări → Avansat**.

---

## Ce văd ceilalți

Cardul scanării, scanarea de sine stătătoare și descărcarea ei urmează regulile obișnuite ale
fișierelor topografice ale peșterii. Unde este scanarea — plasarea, traiectoria, scanarea în vizualizarea
3D — și un original care poartă coordonate reale cer dreptul de a vedea locația exactă a peșterii.
Vezi [Cine ce vede](../features/point-clouds.md#cine-ce-vede).

---

Vezi și: [Nori de puncte (scanări LiDAR)](../features/point-clouds.md) ·
[Înregistrarea unei peșteri noi](record-a-cave.md)
