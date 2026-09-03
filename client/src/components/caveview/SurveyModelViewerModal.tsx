// SPDX-License-Identifier: AGPL-3.0-or-later
import { Modal } from 'antd';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import CaveViewPanel from './CaveViewPanel.tsx';

interface SurveyModelViewerModalProps {
  /** The model to show, or null for a closed modal. */
  model: SurveyModelInfo | null;
  onClose(): void;
}

/**
 * A survey model over the whole window, mounted by whichever page the viewer asked from.
 *
 * Shared rather than copied because two pages now offer it — the cave's own model list, and the
 * map — and because what it carries is not layout but three rules that have to agree: the file
 * name the parser is chosen by, that the viewer is destroyed on close rather than left holding a
 * drawing context for a window nobody is looking at, and that the model is only ever passed in
 * already checked as readable.
 *
 * `destroyOnHidden` is the load-bearing prop. A survey viewer keeps a WebGL context, and a browser
 * keeps only a handful of those alive before it silently drops the oldest — so a hidden-but-mounted
 * viewer is not merely idle, it is one of a small budget that the map and the 3D scene are also
 * drawing from.
 */
export default function SurveyModelViewerModal({ model, onClose }: SurveyModelViewerModalProps) {
  return (
    <Modal
      title={model?.name}
      open={model !== null}
      onCancel={onClose}
      footer={null}
      width="min(1200px, 95vw)"
      destroyOnHidden
    >
      {model && (
        <CaveViewPanel
          fileUrl={model.modelUrl}
          fileName={viewerFileName(model)}
          height="70vh"
          surveyModelId={model.id}
        />
      )}
    </Modal>
  );
}
