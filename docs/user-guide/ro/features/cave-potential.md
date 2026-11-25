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

Un model poate și să **învețe din intrări cunoscute**. Autorul lui nu mai numește atunci nici
scoruri, nici ponderi: fiecare rulare numără cum sunt răspândite peste dovezi intrările de peșteră
deja înregistrate și verifică harta la care ajunge față de intrări care nu i-au fost arătate. Acest
fel de model este descris la
[Modele care învață din intrări cunoscute](#modele-care-învață-din-intrări-cunoscute); restul
paginii i se aplică și lui, în afară de ce spune altfel acea secțiune.

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

La **Cum sunt cântărite straturile**, una din patru metode. Primele două sunt cântărite prin
judecată și sunt descrise aici. **Raport de frecvență** și **Ponderile dovezilor** nu iau nicio
pondere de la tine — vezi
[Modele care învață din intrări cunoscute](#modele-care-învață-din-intrări-cunoscute).

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
- Un model care învață din intrări cunoscute este marcat în schimb **Învață din intrări
  cunoscute**, iar lista îi arată straturile fără ponderi: nu are niciuna până nu este rulat.

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
| **Stare** | Și, odată gata, un rând precum *„8412 din 9105 celule evaluate · media 0.47”*. O hartă învățată din intrări cunoscute adaugă *„învățat din 31 intrări, testat pe 13 · AUC 0.842 / 0.731”* — vezi [Cele două curbe și AUC](#cele-două-curbe-și-auc) |
| **Data** | Când a fost calculată harta |
| **Cerut de** | *Tu*, sau membrul care a rulat-o |

**Celule evaluate** spune cât din zonă acoperă harta. Rămâne sub întreg oriunde un strat nu a avut
valoare — vezi [Celule goale](#celule-goale). O hartă cu mult mai puține celule evaluate decât are
zona este în mare parte goală, iar media este atunci media unei părți mici din ea.

**Arată pe hartă** deschide harta peste zonă, cu harta respectivă activată. Coșul șterge harta; o
poate șterge doar cine a cerut-o, sau un administrator deplin. La o hartă învățată din intrări
cunoscute, **Ponderi și curbe** deschide sub rând [ce a numărat rularea](#citirea-tabelelor-de-ponderi).

### Când o rulare eșuează

| Rândul spune | Ce s-a întâmplat |
|---|---|
| *O parte din dovezile modelului nu este disponibilă aici* — urmat de straturi și motive | Vezi tabelul următor |
| *Nicio parte a zonei nu a avut o valoare din fiecare strat.* | Fiecărei celule i-a lipsit cel puțin un strat: de obicei un strat de tip de rocă peste teren fără unități geologice, sau o zonă din afara reliefului |
| *Contul care a cerut-o nu mai poate citi ce îi trebuie rulării.* | Între cerere și rulare, contul a pierdut dreptul asupra reliefului sau vederea asupra zonei |
| *Zona nu mai poate fi acoperită la această mărime a celulei.* | Conturul zonei a fost schimbat, sau eliminat, după ce rularea a fost cerută |
| *Modelul, așa cum a fost rulat, nu mai poate fi rulat.* | Deschide modelul, salvează-l din nou și rulează-l din nou |
| *Prea puține intrări cunoscute în zonă, dintre cele pe care contul le poate localiza exact, pentru a păstra o parte și a învăța din rest.* | Un model care învață din intrări cunoscute a găsit mai puțin de zece celule cu o intrare pe care o poți localiza. Vezi [De ce puține intrări fac cifrele nesigure](#de-ce-puține-intrări-fac-cifrele-nesigure) |
| *Intrările cunoscute nu lasă nimic de deosebit în această zonă.* | Un model care învață din intrări cunoscute nu a avut teren fără intrare cu care să compare, sau harta lui nu a putut fi testată. Folosește o celulă mai mică sau o zonă mai mare |
| *Harta nu a putut fi calculată.* | Orice altceva, inclusiv o rulare la care lucrătorul a renunțat. Cere-o din nou |

Motivele date pentru dovezile care nu sunt disponibile:

| Motiv | Înseamnă | Cine poate rezolva |
|---|---|---|
| `no_terrain` | Instalarea nu desenează nicio generare de relief | Un administrator, pe [pagina Relief](../admin/terrain-builds.md) |
| `no_layer` | Acea imagine a terenului nu a fost calculată pentru relieful în uz, sau este depășită | Un administrator, la [Imagini derivate](../admin/terrain-builds.md#imagini-derivate-ale-terenului) |
| `slope_unit` | Panta există doar ca procent; un model o evaluează în grade | Un administrator: cere o imagine Pantă în grade |
| `too_many_features` | Mai mult de 50.000 de obiecte de un fel se află în raza zonei — sau, la un model care învață din intrări cunoscute, mai mult de 50.000 de intrări se află în dreptunghiul din jurul ei | Rulează pe o zonă mai mică |

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

La un model cântărit prin judecată, valoarea fiecărei celule este formată din scorurile modelului
pentru acea celulă, combinate cu ponderile modelului: un număr de la 0 la 1. Atât. (Ce conține o
hartă învățată din intrări cunoscute este la
[Harta unui model care învață](#harta-unui-model-care-învață).) Sub fiecare hartă, legenda spune:

> O cântărire a dovezilor, așa cum o descrie modelul, nu o constatare: nimic de aici nu a fost
> verificat pe teren. Doar pentru uz intern.

### Ce nu afirmă harta niciodată

- **Nu este o probabilitate.** 0,8 nu înseamnă opt șanse din zece să fie o peșteră. Pe o hartă
  cântărită prin judecată înseamnă că autorul modelului a dat acelui teren un scor mare; pe o hartă
  învățată din intrări cunoscute înseamnă că terenul stă sus *pe acea hartă*.
- **O hartă cântărită prin judecată nu a fost verificată față de peșterile cunoscute.** Nu vine cu
  nicio cifră despre cât de bine le găsește. O hartă învățată din intrări cunoscute vine cu două —
  iar ele spun mai puțin decât par: vezi [Ce nu înseamnă AUC](#ce-nu-înseamnă-auc).
- **Nu descoperă nimic.** Un model cântărit prin judecată poate întoarce doar judecata autorului
  său, aplicată uniform peste zonă. Folosul lui este că aplică acea judecată consecvent pe teren pe
  care nu a călcat nimeni și că o face destul de explicită ca să poată fi discutată. Un model care
  învață poate întoarce doar tiparul intrărilor deja înregistrate — care este tiparul locurilor
  unde s-a căutat la fel de mult ca al locurilor unde sunt peșteri.
- **Două hărți din două modele nu se pot compara valoare cu valoare.** 0,6 sub un model și 0,6
  sub altul nu sunt aceeași afirmație.
- **Nu este mai fină decât cea mai grosieră dovadă a ei.** O celulă de 10 m peste altitudini mai
  grosiere și o hartă geologică regională rămâne un răspuns grosier despre un contur regional.
- **Nu este un motiv să sari peste teren.** Terenul cu scor mare fără nicio peșteră și terenul cu
  scor mic cu una sunt amândouă obișnuite.

---

## Modele care învață din intrări cunoscute

Un model cântărit prin judecată spune ce crede autorul lui. Un model care **învață** întreabă în
schimb registrul: *pe această zonă, în ce feluri de teren sunt intrările de peșteră cunoscute mai
dese decât terenul însuși?* Desenează harta care decurge din răspuns, apoi verifică acea hartă față
de intrări pe care le-a păstrat deoparte.

Două metode fac asta. Amândouă sunt numărători, făcute dintr-o singură trecere: nimic nu este
potrivit pe date sau reglat, așa că aceeași zonă, aceleași intrări și aceleași setări dau aceleași cifre la
fiecare rulare.

| Metodă | Fiecare clasă a unui strat primește | Fiecare celulă primește |
|---|---|---|
| **Raport de frecvență** | Un număr: partea din intrări a clasei, raportată la partea ei din teren | Suma rapoartelor claselor în care se află, câte una pe strat — un indice |
| **Ponderile dovezilor** | Două ponderi, contrastul lor și cât de sigur este acel contrast | O probabilitate calculată din ponderi |

Raportul de frecvență este mai ușor de citit și de explicat. Ponderile dovezilor spun mai mult —
mai ales cât de departe se poate avea încredere în fiecare cifră — și sunt de preferat atunci când
ce te interesează sunt chiar tabelele. Ambele seturi de coloane sunt arătate la fiecare asemenea
hartă; metoda hotărăște doar cum este desenată harta.

### Scrierea unuia

În editor, alege **Raport de frecvență** sau **Ponderile dovezilor** la **Cum sunt cântărite
straturile**. Editorul cere atunci mai puțin:

- **Un strat măsurat este tăiat în clase, și atât.** Fiecare rând este doar *Până la* o valoare.
  Nu este niciun scor de completat, pentru că valoarea fiecărei clase este tocmai ce numără o
  rulare. Nu există nici rampă: intrările se numără pe clase, iar o rampă nu are clase. Un strat pe
  care îl evaluaseși printr-o rampă este arătat ca tabelul lui de clase, iar întoarcerea la o metodă
  cântărită prin judecată aduce rampa înapoi.
- **Forma de relief și Tipul rocii nu cer nimic**: *„Fiecare clasă a acestui strat este numărată;
  nu este nimic de completat.”*
- **Nicio pondere și nicio comparație**: *„Ponderile acestui model sunt numărate din intrările
  cunoscute atunci când este rulat și sunt arătate împreună cu fiecare hartă.”*
- **Densitatea intrărilor de peșteră nu este oferită**, iar un model care o cântărește deja nu
  poate fi salvat ca unul care învață. Acel strat este făcut chiar din intrările pe care le numără
  modelul, inclusiv din cele păstrate: modelului i s-ar arăta intrările pe care este apoi testat.

Apar două câmpuri:

| Câmp | |
|---|---|
| **Intrări păstrate pentru testare (%)** | Între 10 și 50; 30 dacă nu o schimbi. Această parte nu este arătată niciodată modelului |
| **Sămânța tragerii** | Un număr întreg, 0 sau mai mare; 1 dacă nu o schimbi. Hotărăște *care* intrări sunt păstrate |

> Intrările păstrate nu sunt arătate niciodată modelului; cât de bine le găsește harta lui este
> rata de predicție. Aceeași zonă, aceleași intrări, aceeași parte și aceeași sămânță păstrează
> aceleași intrări la fiecare rulare.

Sămânța există ca tragerea să poată fi repetată, nu ca să fie reglată. Orice schimbă celulele care
conțin o intrare schimbă și tragerea: altă mărime a celulei, o intrare nou înregistrată, drepturile
altui cont.

**Alege clasele cu grijă; ele sunt modelul.** Un model care învață nu are nicio părere despre
pantă, ci doar despre clasele în care ai tăiat panta. Câteva clase late pot șterge o diferență
reală; multe clase înguste lasă fiecăreia o mână de intrări și o cifră care spune puțin — vezi
[De ce puține intrări fac cifrele nesigure](#de-ce-puține-intrări-fac-cifrele-nesigure).

Ca orice model, unul care învață nu numește niciun loc și nu conține nicio intrare. Partajarea lui
partajează o listă de straturi și limite de clase, și nimic despre unde se află ceva.

### Ce numără o rulare

1. **Terenul.** Celulele din interiorul zonei pentru care *fiecare* strat al modelului are o
   valoare. O celulă căreia îi lipsește un strat este lăsată în afara numărătorii, la fel cum este
   lăsată în afara hărții, astfel încât tabelul fiecărui strat este un tabel al aceluiași teren.
2. **Intrările.** Fiecare intrare de peșteră pe care *tu* o poți citi și localiza exact și care se
   află pe acel teren. Una pe care nu o poți localiza este lăsată deoparte, niciodată mutată undeva
   aproximativ. Una din afara zonei, sau de pe o celulă pentru care vreun strat nu are valoare, nu
   este folosită deloc.
3. **Celulele ocupate.** Se numără celule, nu intrări. O celulă fie conține o intrare, fie nu, iar
   două intrări într-o celulă sunt o singură celulă ocupată.
4. **Împărțirea.** Sămânța împarte celulele ocupate în cele din care modelul **învață** și cele
   **păstrate**. Tabelele sunt numărate doar din prima parte.
5. **Tabelele, harta și cele două curbe**, descrise mai jos.

O rulare are nevoie de cel puțin **zece celule ocupate**. Acesta este pragul sub care nu se poate
păstra nimic — nu un număr de la care rezultatele încep să merite citite.

### Raportul de frecvență

Pentru o clasă a unui strat:

> **raportul de frecvență** = partea clasei din celulele ocupate ÷ partea clasei din teren

| Raport | Se citește |
|---|---|
| **1** | Clasa conține intrări proporțional cu mărimea ei. Nu spune nimic, în niciun sens |
| **Peste 1** | Conține mai mult decât partea ei; 2 înseamnă de două ori cât ar da doar mărimea |
| **Sub 1** | Conține mai puțin decât partea ei |
| **0** | Nicio intrare din care a învățat modelul nu se află în ea. Asta *nu* înseamnă „acolo nu sunt peșteri” |

Un exemplu inventat: o zonă de 10.000 de celule, dintre care 40 ocupate de intrări din care învață
modelul, și un strat de pantă tăiat în trei clase.

| Clasă | Celule | Cu intrare | Parte din teren | Parte din intrări | Raport de frecvență |
|---|---|---|---|---|---|
| sub 10 | 6.000 | 12 | 60 % | 30 % | 0,50 |
| de la 10 până la 25 | 3.000 | 20 | 30 % | 50 % | 1,67 |
| 25 și peste | 1.000 | 8 | 10 % | 20 % | 2,00 |

Clasa cea mai abruptă este o zecime din teren și conține o cincime din intrări, deci raportul ei
este 2.

Valoarea unei celule pe hartă este **suma** rapoartelor claselor în care se află, câte una pe
strat. O celulă medie pe fiecare din trei straturi ajunge la 3. Este un indice: pune terenul în
ordine, și nu este o probabilitate, un număr de peșteri sau o densitate.

### Ponderile dovezilor

Aceleași numărători, cu o întrebare mai ascuțită: *cu cât este mai probabilă o celulă din această
clasă printre celulele ocupate decât printre celelalte?*

| Coloană | Ce este | Cum se citește |
|---|---|---|
| **W+** | Logaritmul natural al raportului dintre partea celulelor ocupate care sunt în clasă și partea celulelor neocupate care sunt în clasă | Peste 0, intrările sunt mai dese în clasă decât în afara ei; sub 0, mai rare; 0, nicio diferență |
| **W−** | Același lucru pentru terenul din *afara* clasei | De obicei cu semn opus lui W+, și mai mic |
| **Contrast** | C = W+ − W− | Cât de strâns merge clasa împreună cu intrările, într-un singur număr. 0 înseamnă deloc; semnul spune în ce sens |
| **s(C)** | Abaterea standard a contrastului, din cele patru numărători din spatele lui | Este mare ori de câte ori una dintre numărători este mică |
| **C / s(C)** | Contrastul măsurat în propriile abateri standard — contrastul *studentizat* | *„Peste aproximativ 2, clasa deosebește intrările de teren cu oarecare încredere.”* Aproape de 0, contrastul poate fi foarte bine întâmplare |

Același exemplu:

| Clasă | Raport de frecvență | W+ | W− | Contrast | s(C) | C / s(C) |
|---|---|---|---|---|---|---|
| sub 10 | 0,50 | −0,695 | 0,563 | −1,258 | 0,346 | −3,64 |
| de la 10 până la 25 | 1,67 | 0,514 | −0,338 | 0,851 | 0,317 | 2,69 |
| 25 și peste | 2,00 | 0,697 | −0,118 | 0,815 | 0,397 | 2,06 |

**Citește întâi ultima coloană.** Terenul lin este limpede mai sărac în intrări (−3,64). Clasa cea
mai abruptă are cel mai mare raport și cel mai slab sprijin dintre cele trei: se sprijină pe opt
celule, iar C / s(C) al ei este abia peste 2. Un contrast mare cu un C / s(C) mic este un tipar
într-o mână de celule.

**Valoarea unei celule pe hartă este o probabilitate.** Pornește de la șansa ca o celulă aleasă
pe nevăzute să fie ocupată — partea ocupată a terenului — și este mutată în sus sau în jos de W+ al
clasei celulei pe fiecare strat. Cu un singur strat este exact partea celulelor clasei care sunt
ocupate: 8 din 1.000 pentru clasa cea mai abruptă de mai sus.

Două precauții despre această probabilitate:

- **Este șansa ca o celulă de această mărime să conțină o intrare ca acelea din care a învățat
  modelul.** Este mică peste tot, pentru că intrările sunt rare, și se schimbă odată cu mărimea
  celulei. Nu este șansa de a găsi o peșteră mergând acolo.
- **Adunarea ponderilor mai multor straturi presupune că straturile spun lucruri independente.**
  Straturile care se repetă între ele — panta, asperitatea și rugozitatea — sunt numărate de mai
  multe ori, iar probabilitățile ies prea mari. Pagina măsoară asta pentru tine:

  > Probabilitățile însumează 71.3 celule ocupate, față de cele 40 din care s-a învățat. O sumă
  > mult mai mare înseamnă că straturile se repetă între ele și probabilitățile sunt prea mari;
  > ordonarea rămâne utilizabilă.

  Straturile independente dau înapoi numărul din care s-a învățat. Când suma este mult peste el,
  citește harta ca pe o ordine a terenului și nimic mai mult, sau scoate unul dintre straturile care
  se repetă.

### Citirea tabelelor de ponderi

**Ponderi și curbe**, pe rândul unei hărți terminate, deschide ce a numărat rularea. Întâi vin
câteva rânduri:

- *„Învățat din 31 intrări în 29 celule; 13 intrări în 12 celule păstrate.”*
- *„30 % păstrate, sămânța 1 · 8412 celule de teren cântărite”*
- câte dintre intrările folosite aveau localizarea protejată, dacă a fost vreuna;
- ce înseamnă 0 și 1 pe hartă (vezi [Harta unui model care învață](#harta-unui-model-care-învață));
- la ponderile dovezilor, cât însumează probabilitățile.

Apoi cele două curbe și câte un tabel pentru fiecare strat, cu un rând pentru fiecare clasă.

- **Celule** și **Cu intrare** sunt cele două numărători din care decurge orice altă coloană.
  Uită-te la ele înaintea oricărui alt lucru de pe rând.
- **ajustat**, lângă numele unei clase, înseamnă că una dintre cele patru numărători din spatele
  ponderilor ei era zero — de obicei o clasă în care nu se află nicio intrare. Ponderea ei ar fi
  infinită, iar o clasă care nu conține nicio peșteră cunoscută nu este prin asta dovedită a nu
  conține niciuna; așa că fiecăreia dintre cele patru numărători i se adaugă o jumătate, doar
  pentru acea clasă. Ponderile de pe un rând ajustat țin locul lui *foarte puține* și nu sunt o
  măsurătoare. Raportul de frecvență nu are nevoie de un asemenea ajutor: este pur și simplu 0.
- **O liniuță (–)** înseamnă că nu a fost nimic de numărat. O clasă în care nu cade nicio celulă a
  terenului are liniuțe peste tot. O clasă în care cade *fiecare* celulă are raportul 1, W+ egal cu
  0 și liniuțe după ele: nu există teren în afara ei cu care să fie comparată.
- **Clasele unui strat măsurat sunt numite după limitele lor** — *de la 10 până la 25*. Limitele
  sunt ale modelului, așa că un cititor care poate citi harta, dar nu și modelul ei, vede în loc
  *Clasa 1*, *Clasa 2*. Formele de relief și tipurile de rocă își păstrează numele.

Sub tabele, pagina repetă regula după care se citesc:

> Un raport de frecvență peste 1 și un W+ pozitiv înseamnă că intrările sunt mai dese în clasă
> decât pe întregul teren. Se numără celule, nu intrări: două intrări într-o celulă se numără o
> dată.

### Harta unui model care învață

O probabilitate de câteva miimi și o sumă de rapoarte fără plafon nu pot fi desenate așa cum sunt
pe o scară de la 0 la 1. Harta este deci **întinsă**: celula ei cea mai puțin favorabilă este
desenată ca 0, cea mai favorabilă ca 1, iar restul între ele, în aceeași ordine. **Ponderi și
curbe** spune ce înseamnă cele două capete:

> Pe hartă, 0 înseamnă o probabilitate de 0.000412, iar 1 înseamnă 0.0187.

Sub o asemenea hartă, legenda adaugă:

> Culorile merg de la terenul cel mai puțin favorabil la cel mai favorabil al acestei hărți; ele
> ordonează terenul și nu pot fi comparate cu cele ale altei hărți.

Ce decurge de aici:

- **Terenul cel mai închis este cel mai bun al acestei hărți, nu teren bun.** Pe o hartă pe care
  nimic nu iese în evidență, ceva tot este desenat albastru închis.
- **Două asemenea hărți nu pot fi comparate culoare cu culoare** — nici două zone, nici două
  modele, nici același model cu altă sămânță.
- **Media de pe rândul hărții** este media valorilor întinse și spune puțin.
- O hartă în care fiecare celulă a ajuns la aceeași valoare nu ordonează nimic și este desenată ca
  0 peste tot.

[Celulele goale](#celule-goale) sunt ca la orice altă hartă.

### Cele două curbe și AUC

Cât de bună este harta? Pune-i celulele în ordine, de la cea mai favorabilă în jos, și parcurge
lista. La fiecare pas notează două lucruri: cât din teren a fost luat până acum (pe orizontală) și
câte dintre celulele ocupate ale unui set au fost găsite pe el (pe verticală). Aceasta este o
**curbă de rată**, iar pagina desenează două.

| Curbă | Trasată față de | Ce spune |
|---|---|---|
| **Rata de succes** | Celulele ocupate din care a învățat modelul | Cât de bine se potrivește harta cu ce i s-a arătat. Flatează întotdeauna |
| **Rata de predicție** | Celulele ocupate păstrate | Cât de bine găsește harta intrări pe care nu le-a văzut. *„Rata de predicție este cea cinstită.”* |
| **Întâmplare** (linie întreruptă) | — | Ce ar desena o hartă care nu știe nimic: o cincime din teren conține o cincime din intrări |

Cum se citește un punct: acolo unde curba ratei de predicție trece prin 20 % pe orizontală și 60 %
pe verticală, cea mai favorabilă cincime a terenului conține trei cincimi din intrările care au
fost păstrate.

**AUC** — aria de sub curbă — pune o curbă întreagă într-un singur număr între 0 și 1.

| AUC | |
|---|---|
| **În jur de 0,5** | Nu mai bine decât întâmplarea |
| **Spre 1** | Intrările stau pe terenul pe care harta îl pune cel mai sus. 1 nu poate fi atins |
| **Sub 0,5** | Mai rău decât întâmplarea: harta pune acele intrări *jos*. Cu puține intrări păstrate, asta se întâmplă din întâmplare |

În linii mari, este șansa ca o celulă ocupată aleasă la întâmplare să stea mai sus decât o celulă
de teren aleasă la întâmplare. Rândul unei hărți terminate le dă pe amândouă: *„AUC 0.842 / 0.731”*
este rata de succes, apoi rata de predicție.

- **O diferență între cele două este de așteptat; una mare este un avertisment.** O rată de succes
  mult peste rata de predicție înseamnă că modelul s-a potrivit pe intrările anume care i s-au dat
  — asta fac clasele înguste și intrările puține — și nu pe ceva care ține și pentru intrarea
  următoare.
- **Curbele sunt făcute din trepte drepte.** O hartă făcută din clase are puține valori diferite și
  multe celule din fiecare; celulele cu aceeași valoare sunt luate împreună, ca o singură treaptă.
- **Niciun punct al unei curbe nu reprezintă o intrare.** Fiecare curbă are cel mult 101 puncte,
  fiecare o sumă peste o parte din teren.

### Ce nu înseamnă AUC

- **Nu este partea din peșteri pe care o va găsi harta și nu este o acuratețe.** 0,8 nu înseamnă
  că harta are dreptate de patru ori din cinci.
- **Este testată pe intrări cunoscute, iar intrările cunoscute sunt acolo unde s-a căutat.**
  Intrările păstrate vin din același registru ca acelea din care s-a învățat: aceleași văi,
  aceleași poteci, terenul acelorași cluburi. O hartă care a învățat *pe unde merg speologii* iese
  bine la rata de predicție. O valoare AUC mare nu poate deosebi „găsește peșteri” de „găsește
  efortul de până acum”.
- **Terenul fără intrare cunoscută nu este teren fără peșteră.** Metodele numără fiecare celulă
  neocupată ca goală. Nimic de aici nu ține evidența terenului cercetat și găsit gol, deci nimic de
  aici nu poate fi testat față de el.
- **Este o singură tragere.** Altă sămânță păstrează alte intrări și dă altă valoare AUC. Copiază
  modelul, schimbă sămânța și rulează-l din nou de câteva ori: dacă cifra se mișcă cu o zecime de
  la o sămânță la alta, acea împrăștiere este cât de bine o cunoști.
- **Ține de această zonă, de această mărime a celulei și de aceste intrări.** Nu spune nimic despre
  același model în alt loc. O zonă care cuprinde mult teren vădit nefavorabil — câmpia de lângă
  carst — ridică valoarea AUC fără ca harta să fie mai bună acolo unde contează.
- **Ține de contul care a rulat-o.** Doi membri care pot localiza intrări diferite primesc tabele
  și cifre diferite din același model, și niciunele nu sunt cele greșite.
- **Ordonează; nu localizează.** O curbă bună spune că cincimea favorabilă a unei zone conține cele
  mai multe intrări. Nu spune unde anume în acea cincime.

### De ce puține intrări fac cifrele nesigure

Fiecare cifră de aici este un raport de numărători, iar numărătorile de intrări sunt mici.

- **O singură intrare mută mult o clasă.** Ia o clasă care acoperă o zecime din teren, într-o
  rulare care învață din 12 celule ocupate. Cu una dintre ele în clasă, raportul ei de frecvență
  este 0,83; cu două, 1,67; cu trei, 2,5. Încă un weekend de explorare face din *mai săracă decât
  media* *de două ori media*. **s(C)** și **C / s(C)** există tocmai ca să spună asta — motiv
  pentru care coloanele ponderilor dovezilor merită citite și la o hartă făcută cu raportul de
  frecvență.
- **O clasă fără nicio intrare nu este măsurată deloc.** Raportul ei este 0, iar ponderile ei sunt
  *ajustate*: un înlocuitor, nu o constatare.
- **Fiecare clasă are nevoie de intrările ei.** Treizeci de celule ocupate peste un tabel de zece
  clase înseamnă, în medie, trei pe clasă, iar fiecare strat în plus le împarte din nou pe aceleași
  treizeci.
- **Testul este și mai mic.** Cu zece celule ocupate și 30 % păstrate, harta este testată de trei
  celule. Curba lor are cel mult trei trepte, iar trei celule pot ajunge la o valoare AUC de 0,85
  sau de 0,35 doar din întâmplare.
- **Se numără doar intrările pe care le poți localiza.** Un cont care poate localiza mai puține
  învață din mai puține și primește tabele mai grosiere pentru aceeași zonă.
- **Mărimea celulei schimbă numărătoarea.** O celulă mai mare unește intrări vecine într-o singură
  celulă ocupată.

Pragul de zece celule ocupate este locul de unde aritmetica devine posibilă. Nu există un număr de
la care devine sigură: citește **Celule**, **Cu intrare** și **C / s(C)** pe fiecare rând și
socotește că un rând numărat dintr-o mână de celule spune puțin, oricare i-ar fi raportul.

Cu puține intrări, un asemenea model rămâne bun la două lucruri: să arate ce clase conțin limpede
intrări și care limpede nu, și să pună ce spune registrul alături de ce presupune un model cântărit
prin judecată. Nu este bun la a deosebi două petice de teren care stau amândouă sus.

---

## Cine ce vede

### Modele

Exact cei aleși de autor la **Partajat cu**: doar autorul, membrii unui grup de speologie sau toți
cei autentificați. Un model nu conține niciun loc, deci partajarea lui nu dezvăluie nimic despre
unde se află ceva.

Rularea unui model nu lărgește acest public. Un cititor care poate deschide o hartă, dar nu și
modelul ei, vede harta și culorile ei, fără numele modelului, straturile sau ponderile; autorul
rulării și administratorii compleți văd mereu rețeta din care a fost făcută harta. La o hartă
învățată din intrări cunoscute, tabelele numărate fac parte din rezultat, deci un asemenea cititor
le vede: ce straturi au fost numărate și cifrele fiecărei clase, cu clasele numerotate — dar nu
numele modelului și nici limitele la care modelul a tăiat clasele.

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

O hartă cântărită prin judecată doar din forma terenului nu a fost calculată din niciun obiect
înregistrat, deci regula 4 nu i se aplică: o vede oricine poate citi relieful și poate localiza
zona. O hartă **învățată din intrări cunoscute** ține întotdeauna de regula 4, oricare i-ar fi
straturile — vezi [Hărți învățate din intrări cunoscute](#hărți-învățate-din-intrări-cunoscute).

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

### Hărți învățate din intrări cunoscute

O hartă făcută de un model care învață este **întotdeauna** marcată **Folosește poziții
înregistrate**, chiar și atunci când fiecare strat pe care îl cântărește vine din forma terenului.
Intrările pe care le-a numărat sunt poziții, iar o hartă a locurilor unde intrările sunt dese spune
unde se află ele.

Regula 4 i se aplică deci întotdeauna, iar obiectele despre care întreabă cuprind **fiecare intrare
din care a învățat rularea și fiecare intrare pe care a păstrat-o**, pe lângă orice obiect
înregistrat intrat în straturile ei. Protejate sau nu: întrebarea este dacă le poți citi pe fiecare
și le poți vedea poziția exactă astăzi.

| | |
|---|---|
| **Ce citește o rulare** | Doar intrările pe care contul care a cerut-o le poate citi și localiza exact, cu drepturile lui așa cum sunt când pornește rularea. Una pe care nu o poate localiza este lăsată deoparte înainte de orice numărătoare |
| **Ce reține o rulare** | Contul pentru care a rulat, dacă acel cont era administrator deplin, din câte intrări a învățat și câte a păstrat, și câte dintre ele aveau localizarea protejată. Intrările însele sunt ținute de server doar ca să pună întrebarea regulii 4 și nu sunt arătate niciodată |
| **Ce este guvernat** | Harta, rândul de cifre de pe rândul ei, tabelele de ponderi și cele două curbe — împreună. Nimeni nu vede curbele unei hărți pe care nu o poate deschide |
| **Ce nu apare niciodată** | Poziția sau celula unei intrări, o cifră pentru o singură intrare sau o listă de intrări. Fiecare rând de tabel este o clasă; fiecare punct al unei curbe este o parte din teren |

**De ce tabelele și curbele sunt ținute la fel de strâns ca harta.** Sunt sume peste aceleași
intrări. Un tabel de ponderi spune pe ce feluri de teren se află intrările; pe o zonă mică, o clasă
cu o singură celulă ocupată între câteva celule de teren ajunge aproape să o arate cu degetul.
Cineva care poate localiza fiecare intrare ar fi putut număra același tabel din ce vede deja.
Cineva care nu poate ar afla ceva despre o poziție protejată dintr-un rând de cifre.

Ce decurge de aici, pe lângă ce este valabil pentru [orice hartă făcută din poziții](#hărți):

- **Pierderea vederii asupra unei singure intrări pierde tot rezultatul** — de oricare parte a
  împărțirii s-ar fi aflat, și pentru membrul care a rulat-o ca pentru oricine altcineva.
- **Un membru care poate localiza mai puține intrări primește un model învățat din mai puține.**
  Harta mai bogată a unui coleg nu îi apare, și nimic nu îi spune că există. Rularea lui îi dă o
  hartă la care are dreptul, cu tabelele ei și cu AUC-ul ei.
- **Cifrele a două conturi nu sunt două măsurători ale aceluiași lucru.** Înainte să compari hărți
  sau valori AUC cu un coleg, verificați că amândoi ați învățat din același număr de intrări.
- Când vreo intrare folosită de rulare avea localizarea protejată, pagina o spune, ca număr:

  > Intrări cu localizare protejată printre cele folosite: 4. Vezi acest rezultat pentru că le poți
  > localiza exact pe toate. Nu îl transmite mai departe.

- **O captură de ecran a unui tabel de ponderi sau a curbelor se transmite ca o captură a hărții.**
  Poartă aceleași intrări.

### Ce nu se face niciodată cu o hartă

- **Nu este exportată**, nici ca fișier, nici în vreo altă formă.
- **Nu se află pe linkul unei vederi partajate**, pe o pagină publică sau într-o tură publicată.
- **Nu este arătată nimănui fără cont.**
- **Nu este dusă într-o vedere salvată.**

---

De citit mai departe despre cele două metode care învață: G. F. Bonham-Carter, *Geographic
Information Systems for Geoscientists* (1994), capitolul 9, pentru ponderile dovezilor;
C.-J. F. Chung și A. G. Fabbri, „Validation of spatial prediction models for landslide hazard
mapping”, *Natural Hazards* 30 (2003), pentru curbele ratei de succes și ratei de predicție.

Înrudite: [Analize carstice](karst-analyses.md) · [Geologie](geology.md) · [Relief](terrain.md) ·
[Imaginile terenului, pentru administratori](../admin/terrain-builds.md#imagini-derivate-ale-terenului) ·
[Protecția locațiilor](../admin/location-protection.md) ·
[Spațiul de lucru al hărții](map-workspace.md)
