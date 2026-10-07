// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Alert, Button, Flex, Switch, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useChooseMapBackground,
  useMapBackgrounds,
  type MapBackground,
  type MapBackgroundChoice,
} from '../../api/hooks.ts';

/**
 * Which map backgrounds a document this installation produces may copy — one switch each.
 *
 * Showing a tile on a screen and putting it into a file that is then mailed and printed are
 * different uses, and a provider's terms decide the second separately. The installation ships
 * with an answer for each source; this is where an administrator says otherwise for theirs. It
 * is deliberately not a layer editor: what a source is, where its tiles come from and whether it
 * is offered at all stay in the catalogue file, and the one thing decided here is the one thing
 * that is a statement about terms rather than about a map.
 *
 * A decision made here is kept apart from the shipped answer, so the page can say which rows
 * somebody decided and offer the way back — to whatever the catalogue says now, which is not
 * necessarily what it said when the switch was pressed.
 */
export default function MapBackgroundsForm() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading, isError } = useMapBackgrounds();
  const choose = useChooseMapBackground();

  const decide = async (background: MapBackground, choice: MapBackgroundChoice) => {
    try {
      await choose.mutateAsync({ id: background.id, choice });
      message.success(t('common.saved'));
    } catch (e) {
      message.error(
        e instanceof ApiError && e.code === 'map_layer.attribution_required'
          ? t('admin.mapBackgrounds.needsCredit')
          : t('common.saveFailed'),
      );
    }
  };

  const busy = (background: MapBackground) => choose.isPending && choose.variables?.id === background.id;

  return (
    <Flex vertical gap={16} style={{ maxWidth: 860 }}>
      <Alert
        type="info"
        showIcon
        title={t('admin.mapBackgrounds.intro')}
        description={t('admin.mapBackgrounds.reason')}
      />
      {isError && <Alert type="error" showIcon title={t('admin.mapBackgrounds.loadFailed')} />}
      {/* Said only once the list has answered, and only when it is true: with nothing switched
          on a write-up still gets its map, drawn on a plain ground that says why. */}
      {data && data.every((background) => !background.inDocuments) && (
        <Alert type="warning" showIcon title={t('admin.mapBackgrounds.noneOn')} data-testid="map-backgrounds-none" />
      )}
      <Table<MapBackground>
        rowKey="id"
        size="middle"
        data-testid="map-backgrounds"
        loading={isLoading}
        dataSource={data}
        pagination={false}
        scroll={{ x: 'max-content' }}
        columns={[
          {
            title: t('admin.mapBackgrounds.columns.background'),
            key: 'name',
            render: (_, background) => (
              <Flex vertical>
                <Flex gap={8} align="center" wrap>
                  <Typography.Text strong>{background.name}</Typography.Text>
                  {/* The heading the catalogue files the source under, in the catalogue's own
                      words: it is where a source's standing is said, and the administrator
                      should be reading it at the moment they decide. */}
                  {background.groupName && <Tag>{background.groupName}</Tag>}
                </Flex>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {background.attribution ?? t('admin.mapBackgrounds.noCredit')}
                </Typography.Text>
              </Flex>
            ),
          },
          {
            title: t('admin.mapBackgrounds.columns.inDocuments'),
            key: 'inDocuments',
            width: 150,
            render: (_, background) => (
              <Switch
                checked={background.inDocuments}
                // A source with no credit can be switched off and never on: there would be
                // nothing to write under the picture.
                disabled={!background.canBeCopied && !background.inDocuments}
                loading={busy(background)}
                aria-label={t('admin.mapBackgrounds.switchLabel', { name: background.name })}
                data-testid={`map-background-switch-${background.id}`}
                onChange={(checked) => void decide(background, checked ? 'on' : 'off')}
              />
            ),
          },
          {
            title: t('admin.mapBackgrounds.columns.shipped'),
            key: 'shipped',
            width: 260,
            render: (_, background) => (
              <Flex gap={8} align="center" wrap>
                <Typography.Text type="secondary">
                  {background.catalogueDefault
                    ? t('admin.mapBackgrounds.shippedOn')
                    : t('admin.mapBackgrounds.shippedOff')}
                </Typography.Text>
                {background.choice !== 'default' && (
                  <>
                    <Tag color="blue">{t('admin.mapBackgrounds.decidedHere')}</Tag>
                    <Button
                      type="link"
                      size="small"
                      style={{ padding: 0 }}
                      disabled={busy(background)}
                      data-testid={`map-background-default-${background.id}`}
                      onClick={() => void decide(background, 'default')}
                    >
                      {t('admin.mapBackgrounds.useShipped')}
                    </Button>
                  </>
                )}
              </Flex>
            ),
          },
        ]}
      />
    </Flex>
  );
}
