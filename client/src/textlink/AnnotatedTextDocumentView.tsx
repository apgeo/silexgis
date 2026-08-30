// SPDX-License-Identifier: AGPL-3.0-or-later
import AnnotatedTextPanel from './AnnotatedTextPanel.tsx';

/**
 * A link-annotated text on the document's own page.
 *
 * The same reader as the one in the panel beside the map, with a height that suits a page rather
 * than a dock. Nothing else differs, deliberately: a second reader for the same format would be a
 * second place for the highlighting, the hover card and the edit mode to be almost the same.
 *
 * Following a link from here reaches whatever views are open in other windows, and — when none
 * are — does nothing visible, which is why the pop-out button stays available on this surface
 * too: the arrangement this is meant for is the text on the page and the map in a second window.
 */
export default function AnnotatedTextDocumentView({ documentId }: { documentId: string }) {
  return (
    <div style={{ width: '100%', minWidth: 0, maxHeight: '75vh', display: 'flex' }}>
      <AnnotatedTextPanel documentId={documentId} />
    </div>
  );
}
