// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, Button, Modal, Space } from 'antd';
import { LinkOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import type { PickedModelPart } from '../../caveview/modelParts.ts';
import AddMemberModal from '../reslinks/AddMemberModal.tsx';
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
 *
 * <b>Linking a part of the survey starts here, from the click that selected it.</b> Which station
 * or which stretch of passage somebody means is a question only the survey can answer, so it is
 * answered by pointing at it in the survey rather than by composing an anchor in a form — the
 * same shape as selecting a passage of a text before linking it. What was picked stays on offer
 * until something else is picked, because a click that immediately opened a dialog would make
 * looking around the model impossible.
 */
export default function SurveyModelViewerModal({ model, onClose }: SurveyModelViewerModalProps) {
  const { t } = useTranslation();
  const [picked, setPicked] = useState<PickedModelPart | null>(null);
  const [linking, setLinking] = useState(false);

  // A different model's parts are not this one's, and a closed viewer has nothing selected.
  useEffect(() => {
    setPicked(null);
    setLinking(false);
  }, [model?.id]);

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
        <Space direction="vertical" size="small" style={{ width: '100%' }}>
          {picked !== null && (
            <Alert
              type="info"
              showIcon
              message={t(`caveview.picked.${picked.anchorKind}`, { name: picked.label })}
              action={
                <Button size="small" icon={<LinkOutlined />} onClick={() => setLinking(true)}>
                  {t('caveview.linkThisPart')}
                </Button>
              }
              closable
              onClose={() => setPicked(null)}
            />
          )}
          <CaveViewPanel
            fileUrl={model.modelUrl}
            fileName={viewerFileName(model)}
            height="70vh"
            surveyModelId={model.id}
            onPartPick={setPicked}
          />
        </Space>
      )}
      {model && picked !== null && (
        <AddMemberModal
          open={linking}
          onClose={() => setLinking(false)}
          origin={{
            targetType: 'surveyModel',
            targetId: model.id,
            title: model.name,
            // The part that was clicked, not the model as a whole. Composed here rather than in
            // the dialog because it was chosen by pointing at it, which has already happened.
            anchor: { anchorKind: picked.anchorKind, anchor: picked.anchor },
          }}
          onCreated={() => {
            setLinking(false);
            setPicked(null);
          }}
        />
      )}
    </Modal>
  );
}
