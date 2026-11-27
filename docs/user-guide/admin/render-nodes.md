# Render nodes

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/render-nodes.md)

[← Administration](README.md) · Related: [Flybys — rendering on the server](../features/flybys.md#rendering-on-the-server)

---

A **render node** is a machine — usually one with a graphics card — on which you run ThIDE's render
worker. It takes the flybys members ask to be rendered on the server, renders them with Blender and
sends the films back. **SilexGIS never starts anything on the node and never connects to it**: the
node calls in, over HTTPS, with a token you issue here. SilexGIS itself has no Blender and renders
nothing.

The page is under **Administration → Render nodes**, for whoever may change the installation's
settings.

## Issuing a node

1. Type a name for the machine (*the club's workstation*) and press **Issue a token**.
2. The token is shown **once**. Put it in a file on that machine that only its owner can read
   (`chmod 600` on Linux and macOS; the worker refuses a file others can read).
3. On that machine, with ThIDE and Blender installed, start the worker — the page shows the line
   with this server's address:

   ```
   therion-cli render-worker --server https://<this site>/api/v1/render-nodes/ --token-file <file>
   ```

   It asks for work every half minute, renders one flyby at a time and keeps going until stopped.
   `--blender <path>` names the Blender to use, `--scratch <folder>` where it works.

Once the node has called, its row says **Waiting for work**, when it last called, its device and
its Blender, and members see **Render on the server** in the flyby's movie panel. A node that has
not called for three minutes reads **Not calling**, and the panel stops offering it.

## Withdrawing a node

**Withdraw** refuses the node's next call; its worker stops. A render it was in the middle of goes
back to the queue when the node has not been heard from for two minutes, and another node takes it
(a render is tried five times in all). Withdraw a node whose token may have been seen by somebody
else, and issue a new one.

## What a node can and cannot do

- It downloads the bundle of a render it has taken — made for that member under the same rules
  about who may see which caves as a download — and nothing else. Each download is recorded in the
  change history with the node's name.
- **It receives the bundles of every member who renders on the server, and never a cave, entrance
  or feature under location protection**: those are left out of every bundle made for a node, even
  for a member who may see them. The flyby's name and texts still go with it, so give a token only
  to a machine whose owner you would show the club's other caves to.
- It is given only renders it can make: a film in a format its worker writes, and — when it says how
  much disk it has free — one that fits. Its row says when it left renders to other nodes, and why.
- It is not an account: its token opens the render-node routes and no other page or route.
- It sends films back; a film is kept for its member for two weeks and then deleted.

The limits — how long a node keeps a render without a word, how large a film may be, how much space
renders may take, how long films are kept — are settings of the installation; see
[INSTALL.md](../../INSTALL.md#render-nodes).
