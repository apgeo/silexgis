// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useState } from 'react';
import { Tooltip } from 'antd';
import { EnvironmentOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../components/Button.tsx';

const PlaceMapModal = lazy(() => import('./PlaceMapModal.tsx'));

interface Props {
  documentId: string;
  title: string | null;
  canEdit: boolean;
}

/**
 * "Open as map" on a map picture filed on a cave — how a phone's map is reached in the browser, from
 * the attachment the phone's upload hung on the cave. The screen is loaded only when opened.
 */
export default function PlaceMapAction({ documentId, title, canEdit }: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Tooltip title={t('placeMap.openAsMap')}>
        <Button
          size="small"
          type="text"
          aria-label={t('placeMap.openAsMap')}
          data-testid="placemap-open"
          icon={<EnvironmentOutlined />}
          onClick={() => setOpen(true)}
        />
      </Tooltip>
      {open && (
        <Suspense fallback={null}>
          <PlaceMapModal
            open={open}
            onClose={() => setOpen(false)}
            documentId={documentId}
            title={title}
            canEdit={canEdit}
          />
        </Suspense>
      )}
    </>
  );
}
