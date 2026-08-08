// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined } from '@ant-design/icons';
import { App, Button, Popconfirm, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useFeaturePositionFromPhoto, type FileInfo } from '../../api/hooks.ts';

interface Props {
  file: FileInfo;
  /** The feature this picture hangs on. Absent for anything attached elsewhere. */
  featureId?: string;
  canEdit: boolean;
}

/**
 * Offers an object the position one of its pictures records.
 *
 * A photograph never moves anything by itself — pressing this is the act of accepting what it
 * proposes, and what it performs is an ordinary geometry write: it needs write access to the
 * object, it lands in that object's own history, and it is undone there. There is no queue and
 * nothing to approve, because somebody who may change the object may change it.
 *
 * It appears only where all three things are true: the picture records a position, the caller
 * may see that position, and the picture hangs on a feature. The first two are the same answer —
 * the server sends no position to a caller it would not hand the original to — so a control
 * that would move an object to coordinates its presser cannot see never draws.
 */
export default function PhotoPositionAction({ file, featureId, canEdit }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const apply = useFeaturePositionFromPhoto();
  const [open, setOpen] = useState(false);

  if (!canEdit || !featureId || file.position?.geom?.type !== 'Point') {
    return null;
  }

  const onConfirm = async () => {
    try {
      await apply.mutateAsync({ featureId, fileId: file.id });
      message.success(t('photoFacts.positionApplied'));
      setOpen(false);
    } catch {
      message.error(t('photoFacts.positionApplyFailed'));
    }
  };

  return (
    <Popconfirm
      open={open}
      onOpenChange={setOpen}
      title={t('photoFacts.usePositionTitle')}
      description={t('photoFacts.usePositionHint')}
      okText={t('photoFacts.usePositionOk')}
      okButtonProps={{ loading: apply.isPending }}
      onConfirm={() => void onConfirm()}
    >
      <Tooltip title={t('photoFacts.usePosition')}>
        <Button size="small" type="text" icon={<AimOutlined />} aria-label={t('photoFacts.usePosition')} />
      </Tooltip>
    </Popconfirm>
  );
}
