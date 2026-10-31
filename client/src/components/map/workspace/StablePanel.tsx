// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Panel, type PanelProps } from 'react-resizable-panels';

/**
 * A panel whose starting size is taken once, when it mounts. The layout library re-registers a
 * panel whose `defaultSize` changes, which re-lays the whole row; a starting size read from stored
 * preferences changes whenever the person drags, so it is frozen here at the value it had when the
 * panel appeared — afterwards the panel itself is what is sized, and reports back.
 */
export default function StablePanel({ defaultSize, ...props }: PanelProps) {
  const [initial] = useState(defaultSize);
  return <Panel {...props} defaultSize={initial} />;
}
