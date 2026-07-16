// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { Button, Drawer, Flex, Modal, Tooltip } from 'antd';
import { PicCenterOutlined, PicRightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import { useUiPrefsStore, type DialogKind } from '../stores/uiPrefsStore.ts';

interface DialogHostProps {
  kind: DialogKind;
  open: boolean;
  title: ReactNode;
  onCancel: () => void;
  /** Confirm action; when absent the footer only offers Cancel/close. */
  onOk?: () => void;
  okLoading?: boolean;
  width?: number;
  /** Fires after the open/close transition — same contract on Modal and Drawer. */
  afterOpenChange?: (open: boolean) => void;
  children: ReactNode;
}

/**
 * Hosts a dialog as a centered modal or as a right-side panel (drawer) over the
 * map, per the user's persisted choice for that dialog kind. A flip button in
 * the header switches placement live — children are re-parented, so their form
 * instances (antd form stores) must live in the calling component, where values
 * survive the remount. The drawer is maskless on purpose: the map stays fully
 * visible and interactive next to the open form.
 */
export default function DialogHost({
  kind,
  open,
  title,
  onCancel,
  onOk,
  okLoading,
  width,
  afterOpenChange,
  children,
}: DialogHostProps) {
  const { t } = useTranslation();
  const isMobile = useIsMobile();
  const placement = useUiPrefsStore((s) => s.dialogPlacement[kind] ?? 'modal');
  const setDialogPlacement = useUiPrefsStore((s) => s.setDialogPlacement);

  const flipLabel = placement === 'modal' ? t('dialog.dockToSide') : t('dialog.floatAsDialog');
  const flipButton = (
    <Tooltip title={flipLabel}>
      <Button
        type="text"
        size="small"
        icon={placement === 'modal' ? <PicRightOutlined /> : <PicCenterOutlined />}
        aria-label={flipLabel}
        data-testid="dialog-placement-flip"
        onClick={() => setDialogPlacement(kind, placement === 'modal' ? 'drawer' : 'modal')}
      />
    </Tooltip>
  );

  if (placement === 'drawer') {
    return (
      <Drawer
        title={title}
        open={open}
        onClose={onCancel}
        afterOpenChange={afterOpenChange}
        // A side panel next to the map is the point on desktop; on a phone there is no
        // "next to", so the drawer takes the screen and the placement preference survives
        // as the transition the user sees when they widen the window again.
        // (`size` rather than `width`: antd deprecated the latter in favour of it.)
        size={isMobile ? '100%' : (width ?? 520)}
        mask={false}
        destroyOnHidden
        extra={flipButton}
        footer={
          <Flex justify="flex-end" gap={8}>
            <Button onClick={onCancel}>{t('common.cancel')}</Button>
            {onOk && (
              <Button type="primary" loading={okLoading} onClick={onOk}>
                {t('common.ok')}
              </Button>
            )}
          </Flex>
        }
      >
        {children}
      </Drawer>
    );
  }

  return (
    <Modal
      // The flip control rides the title row; margin keeps it clear of the close X.
      title={
        <Flex justify="space-between" align="center" style={{ marginRight: 24 }}>
          <span>{title}</span>
          {flipButton}
        </Flex>
      }
      open={open}
      onCancel={onCancel}
      onOk={onOk}
      confirmLoading={okLoading}
      okButtonProps={onOk ? undefined : { style: { display: 'none' } }}
      afterOpenChange={afterOpenChange}
      width={width}
      destroyOnHidden
    >
      {children}
    </Modal>
  );
}
