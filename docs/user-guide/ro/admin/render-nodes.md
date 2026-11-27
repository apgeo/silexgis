# Noduri de randare

🇬🇧 [English](../../admin/render-nodes.md) · 🇷🇴 **Română**

[← Administrare](README.md) · Înrudite: [Survoluri — randarea pe server](../features/flybys.md#randarea-pe-server)

---

Un **nod de randare** este un calculator — de obicei unul cu placă grafică — pe care rulezi
programul de randare al ThIDE. Preia survolurile pe care membrii cer să fie randate pe server, le
randează cu Blender și trimite filmele înapoi. **SilexGIS nu pornește nimic pe nod și nu se
conectează niciodată la el**: nodul se anunță singur, prin HTTPS, cu un token pe care îl emiți
aici. SilexGIS nu are Blender și nu randează nimic.

Pagina este la **Administrare → Noduri de randare**, pentru cine are voie să schimbe setările
instalării.

## Emiterea unui nod

1. Scrie un nume pentru calculator (*stația clubului*) și apasă **Emite un token**.
2. Tokenul este afișat **o singură dată**. Pune-l într-un fișier pe acel calculator pe care să-l
   poată citi doar proprietarul (`chmod 600` pe Linux și macOS; programul refuză un fișier pe care
   îl pot citi și alții).
3. Pe acel calculator, cu ThIDE și Blender instalate, pornește programul — pagina arată linia cu
   adresa acestui server:

   ```
   therion-cli render-worker --server https://<acest site>/api/v1/render-nodes/ --token-file <fișier>
   ```

   Cere de lucru la fiecare jumătate de minut, randează câte un survol pe rând și continuă până este
   oprit. `--blender <cale>` spune ce Blender să folosească, `--scratch <dosar>` unde lucrează.

După ce nodul s-a anunțat, rândul lui spune **Așteaptă lucru**, când s-a anunțat ultima dată,
dispozitivul și versiunea Blender, iar membrii văd **Randare pe server** în panoul de film al
survolului. Un nod care nu s-a mai anunțat de trei minute apare ca **Nu se anunță**, iar panoul nu
mai oferă randarea.

## Retragerea unui nod

**Retrage** refuză următorul apel al nodului; programul lui se oprește. O randare la care lucra
revine în coadă după două minute fără vești de la nod, iar alt nod o preia (o randare este
încercată de cinci ori în total). Retrage un nod al cărui token s-ar putea să-l fi văzut altcineva
și emite unul nou.

## Ce poate și ce nu poate face un nod

- Descarcă pachetul unei randări pe care a preluat-o — făcut pentru acel membru după aceleași reguli
  despre cine ce peșteri are voie să vadă ca la descărcare — și nimic altceva. Fiecare descărcare
  este înregistrată în istoricul modificărilor cu numele nodului.
- **Primește pachetele tuturor membrilor care randează pe server, și niciodată o peșteră, o intrare
  sau un obiect cu locație protejată**: acestea sunt lăsate deoparte din orice pachet făcut pentru
  un nod, chiar și pentru un membru care le poate vedea. Numele și textele survolului merg totuși cu
  el, așa că dă un token doar unei mașini al cărei proprietar ar putea vedea și celelalte peșteri ale
  clubului.
- Primește doar randări pe care le poate face: un film într-un format pe care programul lui îl
  scrie și — când spune cât spațiu liber are pe disc — unul care încape. Rândul lui spune când a
  lăsat randări altor noduri, și de ce.
- Nu este un cont: tokenul lui deschide rutele nodurilor de randare și nicio altă pagină sau rută.
- Trimite filmele înapoi; un film se păstrează pentru membrul lui două săptămâni, apoi se șterge.

Limitele — cât timp ține un nod o randare fără să dea vreun semn, cât de mare poate fi un film,
cât spațiu pot ocupa randările, cât timp se păstrează filmele — sunt setări ale instalării; vezi
[INSTALL.md](../../../INSTALL.md#render-nodes).
