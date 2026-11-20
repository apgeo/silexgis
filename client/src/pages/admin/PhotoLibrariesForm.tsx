// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Card,
  Flex,
  Form,
  Switch,
  Typography,
} from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { queryKeys, usePhotoLibraries, type PhotoLibrarySuspension } from '../../api/hooks.ts';
import { isSettingsConflict, sectionIfMatch, settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

/**
 * Which stored switch belongs to which library.
 *
 * Deliberately not claimed to be exhaustive. The contract names a library with a free string
 * rather than a closed set, so a product added on the server arrives here as a name this build has
 * no switch for — and that is answered by showing the library without a control rather than by
 * posting a body the server would ignore. A switch that silently saves nothing is worse than a
 * library listed without one, because only the second is visible.
 */
const SUSPENSION_FIELD: Partial<Record<string, keyof PhotoLibrarySuspension>> = {
  immich: 'immichSuspended',
  photoprism: 'photoPrismSuspended',
};

/**
 * What it takes to stop the library itself, which is the deployment rather than this page.
 *
 * The commands are the ones the shipped overlays define, and they are printed rather than run:
 * this application is deliberately given no way to start or stop the containers beside it, and a
 * web application that could would be a different kind of program than this one.
 */
const STOP_COMMAND: Partial<Record<string, string>> = {
  immich: 'docker compose stop immich-server',
  photoprism: 'docker compose stop photoprism',
};

/**
 * The one-way brake on each neighbouring photo library.
 *
 * <p>
 * It stops this installation using a library it already has, and starts it again. It cannot
 * connect one — which libraries exist is settled by the deployment that gave them addresses and
 * credentials — so a product nobody connected gets a sentence here and no switch. That asymmetry
 * is the design rather than an unfinished screen: a control that could switch a library "on" would
 * be claiming a container nobody started, and the state it produced is one no operator could act
 * on.
 * </p>
 * <p>
 * Which libraries this installation has is read from the photo-library status route rather than
 * held as a second list here. That route is already the one answer to "what has this installation
 * been given", and a screen with its own idea of it would go on offering a switch for a library
 * somebody removed from the deployment.
 * </p>
 */
export default function PhotoLibrariesForm({ settings, onSaved, onStale }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  // Health polling off: this page is about a stored decision, not about whether a container is up,
  // and a settings tab left open should not put a request a minute at a neighbour.
  const { data: status } = usePhotoLibraries({ watchingHealth: false });
  const [saving, setSaving] = useState<string | null>(null);

  const save = async (
    source: string,
    field: keyof PhotoLibrarySuspension,
    suspended: boolean,
  ) => {
    setSaving(source);
    try {
      // Read the stored decisions again, immediately before changing one of them. Both switches
      // have to travel, because saving replaces the whole stored section and a request carrying one
      // library's value alone would release the other library's brake as a side effect of touching
      // this one — but the copy this tab loaded may be minutes old, and posting it back would undo
      // a brake somebody else pulled meanwhile. Re-reading does not make the write atomic; it
      // shortens the window from "however long this tab has been open" to one round trip.
      const { data: current } = await api.GET('/api/v1/admin/settings');
      const body: PhotoLibrarySuspension = {
        ...(current?.photoLibraries ?? settings.photoLibraries),
        [field]: suspended,
      };
      const { data, error } = await api.PUT('/api/v1/admin/settings/photo-libraries', {
        headers: sectionIfMatch(settings.versions?.photoLibraries),
        body,
      });
      if (error !== undefined || !data) {
        message.error(settingsSaveProblem(error, t));
        if (isSettingsConflict(error)) {
          onStale?.();
        }
        return;
      }
      onSaved(data);
      // Every surface that offers a library reads the status route, so it has to be re-asked here:
      // a map still drawing an overlay for a library this page has just stopped is the one thing
      // an operator watching the screen would read as the switch not working.
      await queryClient.invalidateQueries({ queryKey: queryKeys.photoLibraryStatus });
      message.success(t('common.saved'));
    } finally {
      setSaving(null);
    }
  };

  const connected = status?.providers ?? [];
  const absent = status?.unconfigured ?? [];

  return (
    <Flex vertical gap={16} style={{ maxWidth: 640 }}>
      <Alert type="info" showIcon title={t('admin.messaging.photoLibrariesIntro')} />

      {connected.length === 0 && absent.length === 0 && (
        <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
          {t('libraryPhotos.admin.none')}
        </Typography.Paragraph>
      )}

      {connected.map((library) => {
        const field = SUSPENSION_FIELD[library.source];
        const command = STOP_COMMAND[library.source];
        return (
          <Card key={library.source} size="small" title={library.name}>
            {field !== undefined && (
              <Form layout="vertical" requiredMark={false}>
                <Form.Item
                  label={t('libraryPhotos.admin.suspend')}
                  extra={t('libraryPhotos.admin.suspendHint')}
                  style={{ marginBottom: 8 }}
                >
                  <Switch
                    // Shown from the same answer the save is built from, so the switch can never
                    // render one position and post the other. The status route reports the same
                    // decision, but through a separately cached query with a slower refresh, and a
                    // control that displays one source and writes another eventually shows a state
                    // it would not post.
                    checked={settings.photoLibraries[field]}
                    loading={saving === library.source}
                    onChange={(checked) => void save(library.source, field, checked)}
                    data-testid={`photo-library-suspend-${library.source}`}
                  />
                </Form.Item>
              </Form>
            )}
            {/* The sentence that otherwise gets left out, and the one that matters most: the switch
                will be read as "the photographs are private again", which is false in every
                particular. A stopped-but-running library still indexes, still scans, still holds
                its own accounts and still serves anyone who signs into it directly. */}
            <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }}>
              {t('libraryPhotos.admin.stillHoldsEverything')}
            </Typography.Paragraph>
            {command !== undefined && (
              <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
                {t('libraryPhotos.admin.stopCommand', { command })}
              </Typography.Paragraph>
            )}
          </Card>
        );
      })}

      {/* Named, and given no switch. This is where the one-way shape is visible on the screen: a
          library the deployment never supplied is something this page can describe and not
          something it can create. */}
      {absent.map((library) => (
        <Typography.Paragraph key={library.source} type="secondary" style={{ margin: 0 }}>
          {t('libraryPhotos.admin.cannotConnect', { library: library.name })}
        </Typography.Paragraph>
      ))}
    </Flex>
  );
}
