# Potențial speologic: modele, dovezi și hărți de favorabilitate

🇬🇧 [English](../../features/cave-potential.md) · 🇷🇴 **Română**

[← Referință](README.md) · Înrudite: [Analize carstice](karst-analyses.md) ·
[Geologie](geology.md) · [Relief](terrain.md) ·
[Protecția locațiilor](../admin/location-protection.md)

---

Un **model de potențial speologic** este o judecată pusă în scris: *ce dovezi spun că o bucată de
teren ar putea adăposti o peșteră și cât contează fiecare fel de dovadă.* Rulat pe o zonă, el
devine o **hartă de favorabilitate** — fiecare celulă a zonei colorată de la *mai puțin favorabil*
la *mai favorabil*.

Pagina spune despre ea însăși:

> Un model cântărește dovezi — forma reliefului, roca, ce a fost înregistrat — într-o hartă a
> locurilor unde peșterile sunt mai mult sau mai puțin probabile. Hărțile sunt o cântărire a acestor
> dovezi, nu o constatare, și sunt doar pentru uz intern.

Totul se află la **Statistici → Potențial speologic**: o listă de **Modele** și o listă de **Hărți
calculate**. Hărțile se desenează apoi din panoul de straturi al [hărții](map-workspace.md).

| Vrei să | Îți trebuie |
|---|---|
| Scrii, copiezi sau partajezi un model | Un cont care poate păstra vederi și filtre salvate |
| Rulezi un model sau citești orice hartă | Dreptul de a citi **relieful** |
| Citești o anumită hartă | Regulile de la [Cine ce vede](#cine-ce-vede) |

---

## Straturile de dovezi

Un model este alcătuit din **straturi de dovezi**: suprafețe pe care aplicația le calculează
singură, câte o valoare pentru fiecare celulă. Lista este fixată de aplicație. Un model cântărește
fiecare strat cel mult o dată.

### Din forma terenului

Acestea citesc altitudinile instalării și [imaginile terenului](terrain.md#imagini-ale-terenului-pe-hartă)
calculate din ele. Nu conțin poziția niciunui lucru înregistrat de cineva.

| Strat | Ce conține o celulă | Citit din |
|---|---|---|
| **Altitudine** | Înălțimea terenului, în metri | Relieful pe care îl desenează instalarea |
| **Pantă** | Înclinarea terenului, în grade | Imaginea **Pantă**, calculată în grade |
| **Orientare** | Încotro este îndreptat versantul, ca azimut 0–360° | Imaginea **Orientare** |
| **Poziție topografică** | Cu cât stă celula deasupra sau dedesubtul terenului din jur, în metri | Imaginea **Poziție topografică** |
| **Asperitate** | Cât diferă înălțimile între o celulă și vecinele ei, în metri | Imaginea **Asperitate** |
| **Rugozitate** | Cea mai mare diferență de înălțime din jurul celulei, în metri | Imaginea **Rugozitate** |
| **Formă de relief** | Una din zece forme ale terenului: plan, vârf, creastă, umăr, pinten, versant, scobitură, bază de versant, vale, depresiune | Imaginea **Forme de relief** |

**Un model nu calculează aceste imagini.** Un administrator le cere pe
[pagina Relief](../admin/terrain-builds.md#imagini-derivate-ale-terenului); o rulare o citește
apoi pe cea mai nouă de fiecare fel care este gata și nu este depășită. Altitudinea nu cere nicio
imagine, doar o generare de relief. Dacă o imagine pe care o cântărește modelul nu a fost
calculată, rularea eșuează și spune care (vezi [Când o rulare eșuează](#când-o-rulare-eșuează)).

Din faptul că se citește imaginea care există decurg două lucruri:

- Stratul **Formă de relief** înseamnă ce l-a făcut să însemne *distanța de căutare* a imaginii:
  același versant este versant la 100 m și umăr la 1 km. Un model care dă scoruri formelor de
  relief a fost scris cu o anumită scară în minte; uită-te la imagine pe hartă înainte să te
  încrezi în rulare.
- **Asperitatea** are două definiții publicate, care dau numere diferite. Un tabel de clase scris
  pentru una este greșit pentru cealaltă.

### Din ce a fost înregistrat

Acestea sunt făcute din obiectele registrului. O suprafață făcută din ele arată unde se află, și
de aceea o hartă cântărită din oricare dintre ele ține de [protecția locațiilor](#hărți).

| Strat | Ce conține o celulă | Făcut din |
|---|---|---|
| **Distanța până la falii** | Metri până la cea mai apropiată falie | Fiecare **Linie de fractură** și fiecare [măsurătoare structurală](geology.md#măsurători-structurale) al cărei plan măsurat este **Plan de falie** |
| **Tipul rocii** | Clasa litologică a terenului | [Unitatea geologică](geology.md#unități-geologice) în care cade centrul celulei |
| **Distanța până la doline** | Metri până la cea mai apropiată dolină | Fiecare **Dolină**, desenată ca punct sau ca contur. O celulă din interiorul unui contur este la 0 m |
| **Densitatea intrărilor de peșteră** | Intrări pe km², netezit | Fiecare intrare de peșteră |

Cum se calculează fiecare, acolo unde contează pentru ce scrii:

- **Distanțele se opresc la 5.000 m.** O rulare caută falii și doline până la 5 km dincolo de
  marginea zonei, așa că una aflată imediat în afară contează pentru celulele de pe acea margine.
  Nimic mai îndepărtat nu este citit, deci nicio celulă nu pretinde că este mai departe de atât: o
  celulă fără nicio falie la mai puțin de 5 km conține 5.000 m, la fel ca fiecare celulă a unei zone
  în care nu a fost înregistrată nicio falie.
- **Tipul rocii este gol acolo unde nu e nimic cartat.** O celulă care nu stă sub nicio unitate
  geologică, sau doar sub una a cărei litologie nu este în [listă](geology.md#lista-litologiilor),
  nu are tip de rocă. Unde unitățile se suprapun, câștigă conturul mai mic, astfel încât o unitate
  cartată în detaliu se vede prin cea regională din jurul ei.
- **Densitatea este netezită larg, cu intenție.** Fiecare intrare este întinsă peste o vecinătate
  în formă de clopot lată de două celule și niciodată mai îngustă de 500 m. Desenată mai strâns ar
  fi un punct pe fiecare intrare cunoscută, iar un model care o cântărește ar spune doar că
  *peșterile sunt acolo unde sunt peșterile*.
- **Intră doar ce poți localiza.** Vezi [Rularea unui model](#rularea-unui-model).

### Ce nu poate spune niciun strat de dovezi

- **O suprafață goală nu este o dovadă că nu există nimic.** Nicio dolină la mai puțin de 5 km
  poate însemna că nu sunt, sau că nu le-a înregistrat nimeni. Aplicația nu le poate deosebi și nu
  încearcă.
- **Densitatea intrărilor îi urmează pe speologi la fel de mult ca pe peșteri.** Este cea mai
  mare acolo unde s-a căutat. Cântărită greu, desenează o hartă a efortului de până acum.
- **Straturile nu sunt independente.** Panta, asperitatea și rugozitatea se repetă în mare parte
  una pe alta; un model care le cântărește pe toate trei numără terenul abrupt de trei ori.

---

## Scrierea unui model

**Model nou** deschide editorul. Un model are un **nume**, o **descriere** opțională, cu cine este
**partajat**, **straturile de dovezi** — fiecare cu felul în care este evaluat — și felul în care
straturile sunt cântărite unul față de altul.

Un model numește feluri de dovezi și numere. Nu numește niciodată un loc: nicio zonă, niciun
obiect, nicio coordonată. Unde este rulat se alege atunci când este rulat.

### Evaluarea unui strat

Valorile brute ale unui strat — metri, grade, o clasă de rocă — sunt transformate mai întâi în
**scoruri între 0 și 1**: 0 este terenul cel mai puțin favorabil, 1 cel mai favorabil. Sunt trei
feluri, iar fiecare strat le oferă pe cele care au sens pentru el.

| Evaluat prin | Pentru | Cum funcționează |
|---|---|---|
| **Un tabel de clase** | Orice strat măsurat | Între 2 și 20 de rânduri. Fiecare rând spune *Până la* o valoare și dă un scor; ultimul rând este *Tot ce este peste*. O valoare aflată exact pe o limită ține de clasa care începe acolo |
| **O rampă între două valori** | Orice strat măsurat, în afară de **Orientare** | *Valoarea cu scorul 0* și *valoarea cu scorul 1*. Scorul crește liniar între ele și rămâne neschimbat dincolo de fiecare capăt |
| **Un scor pentru fiecare clasă** | **Formă de relief** și **Tipul rocii** | Un scor pentru fiecare clasă. O clasă lăsată goală are scorul 0 |

**O rampă poate merge în oricare sens.** Pune întâi valoarea mai mare când valorile mici sunt cele
favorabile. Exemplul editorului:

> de la 500 m în jos până la 0 m dă scorul 1 locului aflat pe o falie

**Orientarea primește un tabel și niciodată o rampă.** Un azimut de 359° și unul de 1° sunt vecine;
o rampă de la 0 la 360 ar da nordului și cel mai mic, și cel mai mare scor. Limitele claselor ei
sunt între 0 și 360.

**O clasă căreia nu îi dai scor primește 0**, nu este lăsată deoparte. A lăsa *Gresie* goală
într-un strat de tip de rocă spune că gresia este nefavorabilă; nu spune *fără părere*.

### Cântărirea straturilor

La **Cum sunt cântărite straturile**, una din două metode.

**Ponderi date direct.** Fiecare strat primește un număr mai mare decât zero. Contează doar
proporțiile: 2, 1, 1 și 50, 25, 25 sunt același model. Lista arată partea fiecărui strat ca
procent.

**Comparații pe perechi.** În loc să numești ponderi, compari straturile două câte două, iar
ponderile rezultă din comparații. Acesta este *procesul ierarhiei analitice* (AHP), descris mai
jos.

### Comparațiile pe perechi și raportul de consistență

Să numești cinci ponderi care împreună să aibă sens este greu. Să spui care din două lucruri
contează mai mult, și cam de câte ori, este mai ușor. La comparațiile pe perechi, editorul pune
această întrebare o dată pentru fiecare pereche de straturi — *Pantă față de Tipul rocii* — pe o
scară fixă:

| Alegi | Se citește de obicei ca |
|---|---|
| **La fel de important** | Cele două contează la fel |
| **de 3 ori mai important** | Moderat mai mult |
| **de 5 ori mai important** | Puternic mai mult |
| **de 7 ori mai important** | Foarte puternic mai mult |
| **de 9 ori mai important** | Extrem de mult — capătul scării |
| de 2, 4, 6, 8 ori | Între două dintre cele de mai sus |
| **de N ori mai puțin important** | Aceleași judecăți, în sens invers |

La fiecare pereche răspunzi o singură dată. Dacă panta este de 3 ori mai importantă decât tipul
rocii, tipul rocii este luat ca o treime din importanța pantei; cele două nu pot fi introduse
niciodată contrazicându-se.

Din toate răspunsurile, aplicația calculează singurul set de ponderi care li se potrivește cel
mai bine și îl arată la **Ponderi**, la fel ca la ponderile date direct. Trei straturi cer trei
comparații, cinci cer zece, opt cer douăzeci și opt.

**Raportul de consistență** este arătat lângă ponderi. Măsoară un singur lucru: *cât de mult se
contrazic răspunsurile tale între ele*.

Să presupunem că, în răspunsurile tale, panta este de 3 ori mai importantă decât tipul rocii, iar
tipul rocii de 3 ori mai important decât distanța până la falii. Dus până la capăt, panta ar trebui să fie de
aproximativ 9 ori mai importantă decât distanța până la falii. Dacă la acea pereche răspunzi cu
„de 2 ori”, cele trei răspunsuri nu pot fi adevărate toate deodată. Puțină contradicție este
normală — nimeni nu judecă în multipli exacți — iar raportul spune câtă este, față de ce ar da
niște răspunsuri completate la întâmplare:

| Raport | Ce spune editorul |
|---|---|
| **0** | Răspunsurile se potrivesc perfect. Așa este întotdeauna cu unul sau două straturi, unde nu are ce să se contrazică |
| **cel mult 0,1** | *„Comparațiile se potrivesc între ele suficient de bine (cel mult 0,1).”* |
| **peste 0,1** | *„Peste 0,1 comparațiile se contrazic mai mult decât se obișnuiește; ia în calcul revizuirea lor. Modelul poate fi totuși salvat și rulat.”* |

**Peste 0,1 este un avertisment, niciodată un refuz.** Modelul se salvează și se rulează. O regulă
care l-ar refuza i-ar învăța pe oameni doar să ajusteze numere până dispare avertismentul.

Ce **nu** spune raportul:

- **Nu spune că judecățile sunt corecte.** Un set de răspunsuri poate fi perfect consistent cu el
  însuși și greșit în privința peșterilor. Consistența ține de răspunsuri, nu de teren.
- **Nu spune ce răspuns să schimbi.** Caută perechea care nu decurge din celelalte — de obicei cea
  la care ai răspuns ultima, sau cea dintre cele două straturi pe care le compari cel mai greu.
- **Nu este o notă de calitate a hărții.**

### Partajare, copiere, modificare

| **Partajat cu** | Cine poate citi modelul, îl poate rula și copia |
|---|---|
| **Doar eu** | Tu |
| **Membrii** unui grup de speologie | Membrii acelui grup |
| **Toți cei autentificați** | Orice cont |

- **Doar autorul poate modifica sau șterge un model** (și un administrator deplin). Cineva cu care
  este partajat îl poate rula și îl poate **copia**. Copia este a lui și privată — *„Copiat. Copia
  este a ta și este privată.”* — iar modificarea ei lasă originalul așa cum l-a scris autorul.
- **Partajarea unui model partajează un fel de a cântări terenul și nimic despre unde se află
  ceva.** Modelul nu conține niciun loc.
- **Ștergerea unui model îi păstrează hărțile**: *„Ștergi acest model? Hărțile deja calculate din
  el rămân.”*
- Un model care cântărește orice strat [din ce a fost înregistrat](#din-ce-a-fost-înregistrat) este
  marcat **Folosește poziții înregistrate** în listă, iar editorul și formularul de rulare spun
  fiecare ce înseamnă asta pentru hărțile lui.

---

## Rularea unui model

**Rulează**, pe rândul unui model, cere două lucruri.

| Câmp | |
|---|---|
| **Zonă** | Scrie numele unei zone. Merge orice obiect care este un contur — o zonă carstică, un masiv, o zonă de lucru — cu condiția să îl poți citi și să îi poți vedea conturul exact |
| **Mărimea celulei (m)** | Între 5 și 1.000 m; 50 dacă nu o schimbi. Harta are câte o valoare pentru fiecare pătrat de această mărime |

**O zonă mare cere o celulă mai mare.** O rulare are cel mult un milion de celule, numărate pe
dreptunghiul din jurul zonei. Dacă i se cer mai multe, este refuzată cu limita și nimic nu este
pus în așteptare. O celulă mai fină decât altitudinile instalării nu adaugă detaliu: repetă
aceleași valori de relief peste mai multe celule.

Rularea este apoi **În așteptare** și este calculată de lucrătorul de relief al instalării — cel
care face generările de relief și imaginile terenului, câte o lucrare pe rând, deci poate aștepta
în urma lor. Rândul de la **Hărți calculate** trece prin **În așteptare**, **Se calculează** și
**Gata** (sau **Eșuat**) fără ca pagina să fie reîncărcată.

Ce face o rulare și ce păstrează:

- **Citește în numele tău.** Obiectele intră în dovezi doar dacă *tu* le poți citi și le poți vedea
  poziția exactă, cu drepturile tale așa cum sunt când pornește rularea — nu când ai cerut-o. Un
  obiect pe care nu îl poți localiza este lăsat deoparte înainte de orice calcul; nu este desenat
  undeva aproximativ. Doi membri care rulează același model pe aceeași zonă pot primi, așadar,
  hărți diferite, și amândouă sunt corecte pentru ce poate vedea fiecare.
- **Păstrează modelul așa cum a fost rulat.** Harta reține straturile, evaluarea și ponderile
  modelului din acel moment. Modificarea sau ștergerea modelului după aceea nu schimbă harta, iar o
  hartă nu ajunge niciodată să însemne pe tăcute altceva decât spune modelul ei.
- **Reține din ce a fost făcută**: contul pentru care a rulat, fiecare obiect înregistrat care a
  intrat în dovezi, construcția de elevație și imaginile calculate ale terenului (cu setările lor)
  pe care le-a citit, și data la care a fost calculată. Pagina arată contul și data; lista
  obiectelor nu este arătată niciodată și este cea care hotărăște [cine poate citi harta](#hărți).
  Când construcția de elevație din care a fost făcută o hartă nu mai este cea activă, harta o
  spune, iar rularea modelului din nou citește terenul curent.
- **Este o imagine datată, nu un strat viu.** Intrări noi, o unitate geologică redesenată sau o
  imagine a terenului recalculată nu schimbă o hartă deja făcută. Rulează modelul din nou; cele
  două hărți stau apoi una lângă alta, deosebite prin dată.

### Citirea rândului unei hărți terminate

| Coloană | |
|---|---|
| **Model** | Numele modelului când a fost rulat, cu **Folosește poziții înregistrate** acolo unde este cazul |
| **Zonă** | Zona, cu link spre pagina ei |
| **Mărimea celulei** | Cea cerută |
| **Stare** | Și, odată gata, un rând precum *„8412 din 9105 celule evaluate · media 0.47”* |
| **Data** | Când a fost calculată harta |
| **Cerut de** | *Tu*, sau membrul care a rulat-o |

**Celule evaluate** spune cât din zonă acoperă harta. Rămâne sub întreg oriunde un strat nu a avut
valoare — vezi [Celule goale](#celule-goale). O hartă cu mult mai puține celule evaluate decât are
zona este în mare parte goală, iar media este atunci media unei părți mici din ea.

**Arată pe hartă** deschide harta peste zonă, cu harta respectivă activată. Coșul șterge harta; o
poate șterge doar cine a cerut-o, sau un administrator deplin.

### Când o rulare eșuează

| Rândul spune | Ce s-a întâmplat |
|---|---|
| *O parte din dovezile modelului nu este disponibilă aici* — urmat de straturi și motive | Vezi tabelul următor |
| *Nicio parte a zonei nu a avut o valoare din fiecare strat.* | Fiecărei celule i-a lipsit cel puțin un strat: de obicei un strat de tip de rocă peste teren fără unități geologice, sau o zonă din afara reliefului |
| *Contul care a cerut-o nu mai poate citi ce îi trebuie rulării.* | Între cerere și rulare, contul a pierdut dreptul asupra reliefului sau vederea asupra zonei |
| *Zona nu mai poate fi acoperită la această mărime a celulei.* | Conturul zonei a fost schimbat, sau eliminat, după ce rularea a fost cerută |
| *Modelul, așa cum a fost rulat, nu mai poate fi rulat.* | Deschide modelul, salvează-l din nou și rulează-l din nou |
| *Harta nu a putut fi calculată.* | Orice altceva, inclusiv o rulare la care lucrătorul a renunțat. Cere-o din nou |

Motivele date pentru dovezile care nu sunt disponibile:

| Motiv | Înseamnă | Cine poate rezolva |
|---|---|---|
| `no_terrain` | Instalarea nu desenează nicio generare de relief | Un administrator, pe [pagina Relief](../admin/terrain-builds.md) |
| `no_layer` | Acea imagine a terenului nu a fost calculată pentru relieful în uz, sau este depășită | Un administrator, la [Imagini derivate](../admin/terrain-builds.md#imagini-derivate-ale-terenului) |
| `slope_unit` | Panta există doar ca procent; un model o evaluează în grade | Un administrator: cere o imagine Pantă în grade |
| `too_many_features` | Mai mult de 50.000 de obiecte de un fel se află în raza zonei | Rulează pe o zonă mai mică |

O rulare eșuată nu lasă nicio hartă. Nu este reîncercată de la sine.

---

## Harta

În **panoul de straturi** al hărții, la **Potențial speologic**, fiecare hartă pe care o poți citi
este listată cu numele modelului ei, cu zona, mărimea celulei și data. Bifează una ca să o
desenezi.

> Hărți care arată unde sunt mai favorabile dovezile cântărite de un model, fiecare pentru o zonă,
> la data afișată.

- Este desenată **sub** peșteri, intrări și contururi, și parțial transparentă, astfel încât ele
  se citesc pe fundalul ei și nu sunt ascunse de ea.
- Legenda merge de la galben pal, **Mai puțin favorabil**, la albastru închis, **Mai favorabil**.
  Este o singură familie de culori, de la deschis la închis, intenționat nu de la verde la roșu:
  aceasta nu este o hartă cu *sigur* și *pericol*.
- Același model rulat de două ori pe o zonă dă două rânduri. **Doar data le deosebește.**
- Ce hărți sunt activate nu se păstrează într-o [vedere salvată](saved-views-and-windows.md#vederi-salvate),
  iar linkul unei vederi partajate nu desenează niciodată una.

### Celule goale

O celulă rămâne liberă, cu harta de bază văzându-se prin ea, în două cazuri:

- **În afara zonei.** Marginea hărții este marginea zonei, orice dovezi s-ar afla dincolo de ea.
- **Acolo unde oricare strat al modelului nu a avut valoare** — dincolo de marginea altitudinilor,
  sau sub nicio unitate geologică cartată, pentru un model care cântărește tipul rocii.

O celulă căreia îi lipsește un strat este **lăsată goală, nu evaluată din celelalte**. Dacă ar fi
evaluată din rest, ponderile lor ar fi rescalate pe tăcute, iar două celule vecine ar fi colorate
de două modele diferite fără ca ceva de pe hartă să o spună. **Gol nu înseamnă *nefavorabil*.** Înseamnă
că modelul nu a avut un răspuns acolo.

### Ce afirmă harta

Valoarea fiecărei celule este formată din scorurile modelului pentru acea celulă, combinate cu
ponderile modelului: un număr de la 0 la 1. Atât. Sub fiecare hartă, legenda spune:

> O cântărire a dovezilor, așa cum o descrie modelul, nu o constatare: nimic de aici nu a fost
> verificat pe teren. Doar pentru uz intern.

### Ce nu afirmă harta niciodată

- **Nu este o probabilitate.** 0,8 nu înseamnă opt șanse din zece să fie o peșteră. Înseamnă că
  autorul modelului a dat acelui teren un scor mare.
- **Nu a fost verificată față de peșterile cunoscute.** Nimic de pe această pagină nu măsoară cât
  de bine găsește un model peșterile deja cunoscute, și nicio hartă nu vine cu o cifră de
  acuratețe.
- **Nu descoperă nimic.** Un model poate întoarce doar judecata autorului său, aplicată uniform
  peste zonă. Folosul lui este că aplică acea judecată consecvent pe teren pe care nu a călcat
  nimeni și că o face destul de explicită ca să poată fi discutată.
- **Două hărți din două modele nu se pot compara valoare cu valoare.** 0,6 sub un model și 0,6
  sub altul nu sunt aceeași afirmație.
- **Nu este mai fină decât cea mai grosieră dovadă a ei.** O celulă de 10 m peste altitudini mai
  grosiere și o hartă geologică regională rămâne un răspuns grosier despre un contur regional.
- **Nu este un motiv să sari peste teren.** Terenul cu scor mare fără nicio peșteră și terenul cu
  scor mic cu una sunt amândouă obișnuite.

---

## Cine ce vede

### Modele

Exact cei aleși de autor la **Partajat cu**: doar autorul, membrii unui grup de speologie sau toți
cei autentificați. Un model nu conține niciun loc, deci partajarea lui nu dezvăluie nimic despre
unde se află ceva.

Rularea unui model nu lărgește acest public. Un cititor care poate deschide o hartă, dar nu și
modelul ei, vede harta și culorile ei, fără numele modelului, straturile sau ponderile; autorul
rulării și administratorii compleți văd mereu rețeta din care a fost făcută harta.

### Hărți

**Nimeni nu alege cine vede o hartă — nici autorul ei, nici un administrator.** O hartă nu are
*partajat cu*. Dacă o poți citi se întreabă din nou de fiecare dată când te uiți, pornind de la
ce poți vedea astăzi. Poți citi o hartă când sunt adevărate toate acestea:

| | Regulă |
|---|---|
| 1 | Ai dreptul de a citi **relieful** |
| 2 | Poți citi **zona** pe care a fost rulată și îi poți vedea conturul exact — harta desenează acel contur |
| 3 | Rularea este **terminată**. O rulare în așteptare, în curs de calcul sau eșuată este arătată doar celui care a cerut-o și administratorilor deplini |
| 4 | Pentru o hartă marcată **Folosește poziții înregistrate**: poți, astăzi, să citești și să vezi poziția exactă a **fiecărui** obiect înregistrat din care a fost calculată rularea |

O hartă cântărită doar din forma terenului nu a fost calculată din niciun obiect înregistrat, deci
regula 4 nu i se aplică: o vede oricine poate citi relieful și poate localiza zona.

De ce este regula 4 atât de strictă: o hartă cântărită din poziții înregistrate arată unde se
află ele. Terenul cel mai apropiat de o dolină are cel mai mare scor; terenul cel mai dens în
intrări iese în evidență. Cineva care trece de regula 4 ar fi putut face aceeași hartă din ce
poate deja localiza, deci citirea ei nu îi spune nimic nou. Cineva care nu trece ar afla o poziție
protejată dintr-o culoare.

Ce decurge de aici:

- **Contează fiecare obiect, nu doar cele protejate.** Protecția poate fi activată după ce o hartă
  a fost făcută; întrebarea se pune despre toată lista, așa cum stau lucrurile acum.
- **Membrului care a rulat-o i se pune aceeași întrebare ca tuturor.** Un membru care între timp a
  pierdut vederea asupra unei peșteri protejate pierde și hărțile făcute din ea — inclusiv pe ale
  lui.
- **O hartă făcută dintr-un obiect care a fost șters între timp** poate fi citită doar de un
  administrator deplin, până când este ștearsă și modelul este rulat din nou.
- **O hartă pe care nu o poți citi nu este arătată ca blocată. Nu este arătată deloc**, iar
  cererea ei după adresă răspunde exact ca pentru o hartă care nu există. Nimic nu îți spune că a
  fost făcută o hartă peste teren pe care nu îl poți vedea.
- **Doi membri pot vedea liste diferite** pe aceeași pagină, și niciuna dintre liste nu spune câte
  hărți are celălalt.
- **Dacă harta unui coleg nu apare la tine, rulează tu modelul.** Rularea ta citește doar ce poți
  localiza, deci harta pe care o primești este una la care ai dreptul — făcută din mai puțin.

Sub o hartă făcută din poziții înregistrate, legenda adaugă:

> Realizată din poziții înregistrate, dintre care unele pot fi protejate; o vezi pentru că le poți
> localiza exact pe toate. Nu o transmite mai departe.

**Ultima propoziție este partea pe care aplicația nu o poate impune.** O captură de ecran a unei
asemenea hărți, trimisă cuiva care nu ar fi putut-o deschide, îi dă pozițiile din care a fost
făcută.

### Ce nu se face niciodată cu o hartă

- **Nu este exportată**, nici ca fișier, nici în vreo altă formă.
- **Nu se află pe linkul unei vederi partajate**, pe o pagină publică sau într-o tură publicată.
- **Nu este arătată nimănui fără cont.**
- **Nu este dusă într-o vedere salvată.**

---

Înrudite: [Analize carstice](karst-analyses.md) · [Geologie](geology.md) · [Relief](terrain.md) ·
[Imaginile terenului, pentru administratori](../admin/terrain-builds.md#imagini-derivate-ale-terenului) ·
[Protecția locațiilor](../admin/location-protection.md) ·
[Spațiul de lucru al hărții](map-workspace.md)
