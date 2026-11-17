# Geology: units, structural readings and the stereonet

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/geology.md)

[← Feature reference](README.md) · Related: [Surface features](surface-features.md) ·
[Measurements and statistics](measurements-and-statistics.md) ·
[Location protection](../admin/location-protection.md)

---

Two kinds of geological record the application keeps on the map, and what it works out from them:

| Record | What it is | What it feeds |
|---|---|---|
| **Geological unit** | An outline of one kind of rock, as a geology map draws it | The **Geology inside the area** split on a karst area's statistics |
| **Structural measurement** | One reading taken at a point — bedding, a joint, a fault plane — as dip direction and dip | A **stereonet** of the readings, and the **Watson U²** test of a cave's passages against their strikes |

Both are ordinary [features](surface-features.md): you draw them, edit them, set their visibility
and protect their position the way you do any other feature. What is particular to them is
described here.

---

## Geological units

A **Geological unit** is an area kind: an outline (one polygon or several) of ground that is all
one rock class.

| Field | |
|---|---|
| **Lithology** | The rock class, chosen from a fixed list (below) |
| **Formation** | Free text: the formation's name, as your geology map gives it |
| **Age** | Free text |
| **Source** | Free text: which map or publication the outline came from |

### The lithology list

The list is fixed by the application, not by your installation. Each class is marked
**karstifiable** or not: whether the rock dissolves readily enough to host caves.

| Lithology | Karstifiable |
|---|---|
| Limestone | yes |
| Dolomite | yes |
| Marble | yes |
| Gypsum / anhydrite | yes |
| Rock salt | yes |
| Conglomerate (calcareous) | yes |
| Sandstone | no |
| Shale / marl | no |
| Granite | no |
| Crystalline schist | no |
| Volcanic rock | no |
| Unconsolidated deposits | no |
| Other | no |

**Other is deliberately not karstifiable.** A statistic that cannot say what the rock is does not
add its ground to the ground caves are expected in.

An administrator can edit the form of any feature kind under
[Vocabularies](../admin/vocabularies.md), this one included. That changes what the form offers; it
does **not** change which classes count as karstifiable. A unit carrying a value outside the list
is shown with that value as stored, tagged **not in the vocabulary**, and is never counted as
karstifiable ground. A unit with no lithology at all is shown as **Unclassified**.

### Lithology is not the cave's rock type

A cave's form has its own **Rock type** field (under *Geology*). That is what somebody declared
about the cave; a geological unit is what a map says about the ground. The two are kept apart and
neither is filled in from the other. The karst area statistics show both: the rock types declared
on the area's caves, and the mapped geology beneath the area.

### On the map

Each unit is filled in the colour of its lithology. The karstifiable classes take the blues,
violets and teals, and are outlined more heavily; the others take earth tones and greys, so the
karstifiable ground stands out as one family before any single class is told apart. A unit with no
lithology is left unfilled behind a dashed grey outline, and one whose stored value is not in the
list behind a dotted magenta outline — the second is a record somebody should correct. While units
are loaded on the map, the layers panel shows a key, **Geological units**, listing the classes
drawn.

A unit is [protected](../admin/location-protection.md) like every other outline: a reader who may
not see its exact outline gets no outline at all on the map — an outline cannot be snapped to a
grid the way a point can.

### Importing a geology map

A geology map that is already digital — a shapefile, GeoPackage, GeoJSON or KML of polygons with a
rock-type column — comes in through the [vector import](geodata.md#imports). In the review:

1. Under **Outlines (areas)**, choose **Import each as an area feature** and the kind
   **Geological unit**. Every outline in the file is then proposed as a unit, so they can all be
   ticked at once.
2. Under **Which field is which**, name the file's rock-type column as **Rock type (geological
   units)**. Left on *Work it out*, a column called `lithology`, `litologie`, `rock` or similar is
   used.
3. The card **Rock types in the file** lists every distinct value in that column, how many outlines
   carry it and which class it becomes. A value that is a class's name — in English or Romanian,
   ignoring case and accents, such as `Calcar`, `LIMESTONE` or `gresii` — is **Recognised**. Any
   other value (`Jurassic limestone with chert`, `Flysch`) is **No match** and listed first: pick
   its class, or leave it and those units are created **Unclassified**. You can also change a
   recognised value's class.

Nothing is guessed from part of a value: a mixed or qualified name is yours to classify, because a
wrong karstifiable class moves every density worked out over that ground. Only classes on the list
can be chosen.

---

## Geology inside a karst area

On the page of a **karst area**, a **massif** or a **hill**, the *Karst area statistics* card has a
section **Geology inside the area**: the area's outline cut up by the geological units mapped over
it.

| Column | |
|---|---|
| **Lithology** | The class, with a **karstifiable** tag where it is one |
| **Outcrop (km²)** | How much of the outline that class covers |
| **Share of area** | That outcrop as a share of the whole outline |
| **Entrances** | Entrances standing on that outcrop |
| **Entrances per km²** | Those entrances over that class's own outcrop |

Under the table:

| Figure | |
|---|---|
| **Karstifiable outcrop (km²)** | The karstifiable classes' outcrop added up |
| **Entrances per km² of karstifiable outcrop** | Entrances on karstifiable ground over that ground alone |
| **Ground with no unit mapped (km²)** | The part of the outline no unit covers |
| **Entrances on unmapped ground** | Entrances standing there |

**Why the karstifiable density matters.** A density over the whole outline rewards an area for
having less sandstone in it. Dividing by the karstifiable outcrop instead compares two areas by the
ground caves could actually be in.

### Measured on the map, not declared

Every other figure on that card counts the caves *declared* to be in the area. This split is
different: a geology map is not a hierarchy, so it is measured on the map, by which outlines overlap
which. The card says so:

> *Measured on the map, over the geological units and entrances you may place exactly — so its
> entrance counts are not the declared totals above.*

That last clause is meant literally: a unit whose exact outline you may not see does not shape the
split, and an entrance you may not place is counted on no ground at all. Two readers with different
rights can see different figures here, and the entrance counts will not, in general, add up to the
area's declared entrance count.

Two units of the **same** class that overlap are merged first, so their ground is not counted
twice. Two units of **different** classes that overlap are a mapping error, and each is counted —
the application does not decide which of the two is right.

Where the area's statistics are taken out as a table (CSV), the same split comes with them, with
the basis it was measured on.

---

## Structural measurements

A **Structural measurement** is a point kind: one reading taken with a compass-clinometer, at an
outcrop or in a cave.

| Field | |
|---|---|
| **Measured plane** | Bedding · Joint · Fault plane · Cleavage · Fracture · Other — required |
| **Dip direction (°)** | The direction the plane dips towards, clockwise from north, 0 to under 360 — required |
| **Dip (°)** | The plane's angle below the horizontal, 0 to 90 — required |
| **Note** | Free text |
| **Measured on (YYYY-MM-DD)** | The day of the reading |

### Strike and dip by the right-hand rule

Dip direction and dip are what is stored. If you took the reading as strike and dip, switch
**Orientation entered as** on the form to **Strike and dip (right-hand rule)** and type the strike
instead:

> *Strike (°, right-hand rule: the plane dips to the right of it)*

So a plane striking 030° and dipping to the south-east is stored with a dip direction of 120°.
Switching between the two ways of entering clears the number you typed, because the same number
means a different plane in each. A dip direction of 360° is stored as 0°.

### Underground readings and protection

A reading needs no parent: most are taken at outcrops. A reading taken in a cave can be put
**inside the cave** (the cave as its parent), and then it inherits the cave's
[protection](../admin/location-protection.md) like anything else the cave contains. A reading's
position is otherwise protected exactly as any point is.

### On the map

A reading is drawn as the geologist's strike-and-dip symbol: a long bar along the strike, a short
tick pointing down the dip, and the dip written beside it. It turns with the map, so it keeps
pointing the right way when the map is rotated. A reading whose orientation is incomplete is drawn
as a plain point of its kind.

The orientation says how a plane lies, not where it is, so it is drawn wherever the point itself is
drawn — snapped or not.

---

## The stereonet

A **stereonet** plots many readings at once, so that a family of joints or the attitude of the
bedding shows up as a cluster.

| Where | Which readings |
|---|---|
| A cave's page, in **Passage against the mapped structure** | Readings within reach of the cave — a kilometre by default, the same reach the fracture-trace comparison uses — as *n readings within r m of the cave* |
| The page of any outline (a karst area, a massif, a work area…), as **Structural readings in this area** | Readings **inside** that area — put in it as their parent, or in something it contains. The card is left out entirely when the area holds none |

**Readings of** chooses one kind of plane, or **every kind**. Mixing bedding with joints puts two
different questions on one net; choosing one kind is usually what you want.

### Reading the net

The application's own description:

> *Equal-area net, lower hemisphere: each dot is the pole of one plane (the line at right angles
> to it, taken pointing down). A plane dipping gently plots near the centre; a steep one near the
> rim, on the side opposite its dip.*

Pointing at a dot names the reading: its kind, its dip and its dip direction.

The shading is the density of the poles, by **Kamb's method**: a band every two standard
deviations, where readings scattered at random would read three everywhere. The legend under the
net gives the densest spot's value — a cluster reading 9 is six standard deviations above what
chance would put there.

Two choices behind the picture, both standard in structural geology:

- **Equal-area, not equal-angle.** On an equal-area net, equal pieces of the sphere are drawn at
  equal sizes, so a cluster near the rim is not made to look sparser than one near the centre.
- **Kamb, not a fixed 1 % circle.** The counting circle is sized from the number of readings, so
  the same rock does not look clustered or smooth depending only on how many readings were taken.

---

## Passage against measured strikes (Watson U²)

On a cave's page, the card **Passage against the mapped structure** compares the cave's passage
directions with structure in two ways:

| Against | How |
|---|---|
| Mapped fracture and fault **lines** | Rose against rose, by length |
| Structural **readings** of the chosen kind | Their strikes, by **Watson's U²** test |

The second answers a statistician's question: *could the passage trends and the strikes have come
from one distribution?* It reports:

| | |
|---|---|
| **U²** | The test statistic: nought when the two are spread round the circle alike |
| **Probability** | How likely a U² this large would be if they did come from one distribution |
| A verdict | Below 0.05: *"…unlikely to come from one distribution"*; otherwise *"Nothing here shows the passage trends and the strikes to differ"* |
| **Strikes of the readings** | A rose of the strikes that took part |

How it is computed, and why it matters to how you read it:

- **Directions are axes.** A leg surveyed 030° and one surveyed 210° run along the same passage;
  a plane striking 030° strikes 210° too. Both sides are treated that way.
- **Each survey leg and each reading counts once.** Nothing is weighted by length. The card
  warns: *"Legs along one passage are not independent of each other, so a small probability says
  less than it reads."*
- **The whole spread is compared, not only the mean.** A straight passage against scattered
  strikes with the same mean direction still differs.
- **A flat reading has no strike** and is left out of the strikes, rather than given an arbitrary
  one.
- **At least five on each side.** With fewer survey legs or fewer readings with a strike, the
  card says how many there are and gives no figure.

The readings are gathered from the same reach as the fracture lines, and **Readings of** on the
card chooses which kind of plane both the test and the stereonet use.

---

## Who sees what

- **Only what you may place exactly.** A reading, a unit or an entrance whose exact position is
  withheld from you is in no pole, no density, no count, no outcrop and no test. The counts shown
  are what you may see, never how many records exist.
- **A protected cave you may not place** shows no *Passage against the mapped structure* card at
  all: the readings around it would outline where it is.
- **Signed in only.** The stereonet, the strike comparison and the geology split are not offered on
  a shared link or a public page.

## What it never does

- It never counts a lithology outside its own list as karstifiable, whatever the form says.
- It never fills a cave's **Rock type** from the geology map, or the other way round.
- It never guesses a strike for a reading lying flat.
- It never decides which of two overlapping units of different rock is right.

---

Related: [Surface features](surface-features.md) ·
[Measurements and statistics](measurements-and-statistics.md) ·
[Geodata files](geodata.md) ·
[Cave potential](cave-potential.md) ·
[Location protection](../admin/location-protection.md) ·
[Vocabularies](../admin/vocabularies.md)
