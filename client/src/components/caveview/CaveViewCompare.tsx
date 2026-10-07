// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Alert, Button, Checkbox, Dropdown, Segmented, Spin, Switch, Typography } from 'antd';
import { CloseOutlined, DownOutlined, SwapOutlined, UnorderedListOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { CaveViewToolbarOptions, CaveViewer } from '../../caveview/loadCaveView.ts';
import {
  COMPARE_COLOURS,
  COMPARE_SIDES,
  NOTHING_HIDDEN,
  applyHiddenSurveys,
  compareLabels,
  compareToolbarButtons,
  comparisonUnderWay,
  fileSurvey,
  setSurveyShown,
  showAllSurveys,
  surveysLieTogether,
  surveysOf,
  type CompareHidden,
  type CompareLists,
  type CompareMode,
  type CompareSide,
  type SurveyCompareOffer,
  type SurveyComparison,
} from '../../caveview/surveyCompare.ts';
import CompareViewerHost from './CompareViewerHost.tsx';

export interface CaveViewCompareProps {
  /** What this panel's survey can be compared with. Absent, nothing here is drawn. */
  offer: SurveyCompareOffer | undefined;
  /** The comparison asked for, which the panel holds: its own surface is laid out by it. */
  comparison: SurveyComparison | null;
  onComparisonChange(comparison: SurveyComparison | null): void;
  /** The panel's own viewer, or null while it has no model drawn. Read when needed, never kept. */
  getPrimaryViewer(): CaveViewer | null;
  /**
   * How many models the panel's own viewer has drawn. What is set on that viewer — which parts
   * are hidden, whose view it follows — is set again on the next one, and this is what says a
   * next one has arrived.
   */
  primaryLoads: number;
  /** The viewer's own controls as the panel shows them, which the second viewer is given too. */
  toolbar: CaveViewToolbarOptions | null;
  crsLookup?: (code: string) => Promise<string | null>;
  /** The panel's own drawing surface, which stays where it is in the tree whatever is drawn here. */
  children: ReactNode;
}

/** Whether two surveys laid over one another turned out to lie in the same place. */
type Verdict = 'unknown' | 'together' | 'apart';

/**
 * Two surveys of one cave, looked at together: what is drawn around the panel's own viewer when
 * its survey is compared with another.
 *
 * <b>Two arrangements, because each answers what the other cannot.</b> Laid over one another, in
 * two colours, a passage that was resurveyed sits on its earlier self and what differs is what
 * shows — but only where both surveys were made in the same coordinates. Side by side, each in a
 * viewer of its own, nothing has to line up: each is framed on itself, and turning one turns the
 * other.
 *
 * <b>The panel's own viewer is never loaded with a second file.</b> Side by side it is the first
 * of the two, exactly as it was, with everything the panel attaches to it still working. Laid
 * over one another, both surveys are drawn in a viewer built for the purpose and the panel's own
 * is put out of sight until the comparison ends — see {@link CompareViewerHost} for why.
 *
 * <b>Which parts are shown is one state for both arrangements.</b> It is kept by each survey's
 * own paths and applied to whichever viewers are on the screen, so switching the arrangement
 * leaves hidden what was hidden.
 *
 * Nothing here is remembered: a comparison is a way of looking, and ends with the panel.
 */
export default function CaveViewCompare({
  offer,
  comparison,
  onComparisonChange,
  getPrimaryViewer,
  primaryLoads,
  toolbar,
  crsLookup,
  children,
}: CaveViewCompareProps) {
  const { t } = useTranslation();
  const under = comparisonUnderWay(offer, comparison);
  const mode = under?.mode ?? null;
  const otherId = under?.other.id ?? null;

  /** Whether turning, tilting or zooming one of two viewers does the same to the other. */
  const [linked, setLinked] = useState(true);
  /** Whether showing or hiding a part does the same to the part of that path in the other survey. */
  const [inStep, setInStep] = useState(true);
  const [listsOpen, setListsOpen] = useState(true);
  const [hidden, setHidden] = useState<CompareHidden>(NOTHING_HIDDEN);
  const [lists, setLists] = useState<CompareLists>({ primary: null, other: null });
  const [verdict, setVerdict] = useState<Verdict>('unknown');

  // The comparison's own viewers live in refs; state holds only a count of the models each has
  // drawn, which is what the effects that set things on them are run again for.
  const overlayRef = useRef<CaveViewer | null>(null);
  const [overlayLoads, setOverlayLoads] = useState(0);
  const otherRef = useRef<CaveViewer | null>(null);
  const [otherLoads, setOtherLoads] = useState(0);

  const labels = compareLabels(under?.current.name ?? '', under?.other.name ?? '');
  const labelsRef = useRef(labels);
  labelsRef.current = labels;
  const getPrimaryRef = useRef(getPrimaryViewer);
  getPrimaryRef.current = getPrimaryViewer;

  // ---- A comparison begun, changed or ended ----
  //
  // What is hidden belongs to the pair being compared: another survey has other parts, and a
  // comparison ended leaves the panel's own viewer as it was found — with everything shown, since
  // the list that would show a part again goes with the comparison.
  useEffect(() => {
    setHidden(NOTHING_HIDDEN);
    setLists((before) => ({ primary: before.primary, other: null }));
    if (otherId === null) {
      getPrimaryRef.current()?.showAllSections();
    }
  }, [otherId]);

  // Whether two surveys lie together is found out each time they are laid over one another.
  useEffect(() => {
    setVerdict('unknown');
  }, [otherId, mode]);

  // ---- The parts of the panel's own survey ----
  //
  // Read only where a comparison is on offer: every panel draws through this component, and one
  // that will never compare anything has no use for a walk of its survey's tree.
  const offered = offer !== undefined;
  useEffect(() => {
    const viewer = offered ? getPrimaryRef.current() : null;
    if (viewer === null) {
      return;
    }
    setLists((before) => ({ ...before, primary: surveysOf(viewer.getSurveyTree()) }));
  }, [primaryLoads, offered]);

  const onOverlayViewer = useCallback((viewer: CaveViewer | null) => {
    overlayRef.current = viewer;
    if (viewer !== null) {
      const named = labelsRef.current;
      const together = surveysLieTogether(
        viewer.getSectionBounds([named.primary]),
        viewer.getSectionBounds([named.other]),
      );
      setVerdict(together ? 'together' : 'apart');
      if (together) {
        const tree = viewer.getSurveyTree();
        setLists({
          primary: surveysOf(fileSurvey(tree, named.primary)),
          other: surveysOf(fileSurvey(tree, named.other)),
        });
      }
    }
    setOverlayLoads((count) => count + 1);
  }, []);

  const onOtherViewer = useCallback((viewer: CaveViewer | null) => {
    otherRef.current = viewer;
    if (viewer !== null) {
      setLists((before) => ({ ...before, other: surveysOf(viewer.getSurveyTree()) }));
    }
    setOtherLoads((count) => count + 1);
  }, []);

  // ---- Which parts are drawn ----
  //
  // One state, applied to whichever viewers are showing the two surveys. Laid over one another
  // both are in one viewer, each under its label; side by side each has a viewer to itself.
  useEffect(() => {
    if (mode === 'overlaid') {
      const viewer = overlayRef.current;
      if (viewer === null || verdict !== 'together') {
        return;
      }
      for (const side of COMPARE_SIDES) {
        applyHiddenSurveys(viewer, [labelsRef.current[side]], lists[side] ?? [], hidden[side]);
      }
    } else if (mode === 'sideBySide') {
      const primary = getPrimaryRef.current();
      if (primary !== null) {
        applyHiddenSurveys(primary, [], lists.primary ?? [], hidden.primary);
      }
      if (otherRef.current !== null) {
        applyHiddenSurveys(otherRef.current, [], lists.other ?? [], hidden.other);
      }
    }
  }, [mode, hidden, lists, verdict, overlayLoads, otherLoads, primaryLoads]);

  // ---- Two viewers looked at as one ----
  //
  // Each tells of a change the reader made to its view and of none that was set on it, so each is
  // given the other's as it changes and neither answers the other. What is passed across is a
  // direction, a zoom relative to the model's own extent and an offset relative to the screen:
  // the two models need not be the same size, nor in the same coordinates.
  useEffect(() => {
    if (mode !== 'sideBySide' || !linked) {
      return;
    }
    const first = getPrimaryRef.current();
    const second = otherRef.current;
    if (first === null || second === null) {
      return;
    }
    const follow = (from: CaveViewer, to: CaveViewer) => () => {
      const view = from.getViewpoint();
      if (view !== null) {
        to.setViewpoint(view);
      }
    };
    const firstMoved = follow(first, second);
    const secondMoved = follow(second, first);
    first.addEventListener('viewpoint', firstMoved);
    second.addEventListener('viewpoint', secondMoved);
    // The second model is brought to where the reader already has the first.
    firstMoved();
    return () => {
      first.removeEventListener('viewpoint', firstMoved);
      second.removeEventListener('viewpoint', secondMoved);
    };
  }, [mode, linked, otherLoads, primaryLoads]);

  const nameOf = (side: CompareSide) =>
    (side === 'primary' ? offer?.current.name : under?.other.name) ?? '';
  const compareToolbar =
    toolbar === null || mode === null
      ? null
      : { ...toolbar, buttons: compareToolbarButtons(toolbar.buttons ?? [], mode) };

  const start = (id: string, as: CompareMode) => onComparisonChange({ otherId: id, mode: as });

  const swatch = (side: CompareSide) => (
    <span
      className="caveview-compare-swatch"
      style={{ background: COMPARE_COLOURS[side] }}
      aria-hidden
    />
  );

  // One shape for every panel, with or without anything to compare. The panel's own surface is
  // the third thing drawn here whatever stands before it, so that an offer arriving after the
  // model — or a comparison begun — never moves it in the tree: the viewer owns that element, and
  // an element taken out and put back is a new one with nothing drawn in it.
  return (
    <>
      {offer !== undefined && (
        <div className="caveview-compare-bar" data-testid="caveview-compare-bar">
          {under === null ? (
            <Dropdown
              trigger={['click']}
              menu={{
                // One gesture names both choices: the survey, and how the two are shown.
                items: offer.others.map((model) => ({
                  type: 'group' as const,
                  key: model.id,
                  label: model.name,
                  children: (['overlaid', 'sideBySide'] as const).map((as) => ({
                    key: `${as}:${model.id}`,
                    label: t(`caveview.compare.${as}`),
                    onClick: () => start(model.id, as),
                  })),
                })),
              }}
            >
              <Button size="small" icon={<SwapOutlined />} data-testid="caveview-compare-open">
                {t('caveview.compare.open')} <DownOutlined />
              </Button>
            </Dropdown>
          ) : (
            <>
              <Segmented<CompareMode>
                size="small"
                aria-label={t('caveview.compare.mode')}
                data-testid="caveview-compare-mode"
                value={under.mode}
                onChange={(next) => onComparisonChange({ otherId: under.other.id, mode: next })}
                options={[
                  { value: 'overlaid', label: t('caveview.compare.overlaid') },
                  { value: 'sideBySide', label: t('caveview.compare.sideBySide') },
                ]}
              />
              {under.mode === 'sideBySide' && (
                <label className="caveview-compare-switch" title={t('caveview.compare.linkViewsHint')}>
                  <Switch
                    size="small"
                    checked={linked}
                    onChange={setLinked}
                    data-testid="caveview-compare-link"
                  />
                  <Typography.Text>{t('caveview.compare.linkViews')}</Typography.Text>
                </label>
              )}
              {verdict !== 'apart' && (
                <Button
                  size="small"
                  type={listsOpen ? 'primary' : 'default'}
                  ghost={listsOpen}
                  icon={<UnorderedListOutlined />}
                  aria-pressed={listsOpen}
                  onClick={() => setListsOpen((open) => !open)}
                  data-testid="caveview-compare-lists-toggle"
                >
                  {t('caveview.compare.surveys')}
                </Button>
              )}
              {under.mode === 'overlaid' && verdict === 'together' && (
                // Which colour is which survey. The names are written in the page's own ink and the
                // colour is carried by the mark beside each, as it is on the model.
                <span
                  className="caveview-compare-legend"
                  role="group"
                  aria-label={t('caveview.compare.legend')}
                  data-testid="caveview-compare-legend"
                >
                  {COMPARE_SIDES.map((side) => (
                    <span key={side} className="caveview-compare-legend-entry">
                      {swatch(side)}
                      <Typography.Text ellipsis title={nameOf(side)}>
                        {nameOf(side)}
                      </Typography.Text>
                    </span>
                  ))}
                </span>
              )}
              <Button
                size="small"
                icon={<CloseOutlined />}
                className="caveview-compare-stop"
                onClick={() => onComparisonChange(null)}
                data-testid="caveview-compare-stop"
              >
                {t('caveview.compare.stop')}
              </Button>
            </>
          )}
        </div>
      )}

      {under?.mode === 'sideBySide' &&
        COMPARE_SIDES.map((side) => (
          <div
            key={side}
            className={`caveview-compare-name caveview-compare-name-${side}`}
            data-testid={`caveview-compare-name-${side}`}
          >
            <Typography.Text strong ellipsis title={nameOf(side)}>
              {nameOf(side)}
            </Typography.Text>
          </div>
        ))}

      {children}

      {under?.mode === 'sideBySide' && (
        <div className="caveview-compare-cell caveview-compare-cell-other">
          <CompareViewerHost
            key={under.other.id}
            files={[{ url: under.other.fileUrl, fileName: under.other.fileName }]}
            filesKey={under.other.id}
            toolbar={compareToolbar}
            crsLookup={crsLookup}
            onViewer={onOtherViewer}
            testId="caveview-compare-other"
          />
        </div>
      )}

      {under?.mode === 'overlaid' && (
        <div className="caveview-compare-cell caveview-compare-cell-overlay">
          {verdict === 'apart' ? (
            // Said in place of the picture, not over it: two surveys drawn where their own
            // numbers put them, kilometres apart, are not a comparison of anything.
            <Alert
              type="warning"
              showIcon
              className="caveview-compare-apart"
              data-testid="caveview-compare-apart"
              title={t('caveview.compare.apart.title')}
              description={t('caveview.compare.apart.body')}
              action={
                <Button
                  size="small"
                  type="primary"
                  onClick={() => onComparisonChange({ otherId: under.other.id, mode: 'sideBySide' })}
                  data-testid="caveview-compare-apart-side-by-side"
                >
                  {t('caveview.compare.apart.sideBySide')}
                </Button>
              }
            />
          ) : (
            <CompareViewerHost
              key={`${under.current.id}:${under.other.id}`}
              files={COMPARE_SIDES.map((side) => {
                const model = side === 'primary' ? under.current : under.other;
                return {
                  url: model.fileUrl,
                  fileName: model.fileName,
                  label: labels[side],
                  color: COMPARE_COLOURS[side],
                };
              })}
              filesKey={`${under.current.id}:${under.other.id}`}
              colourBySurvey
              toolbar={compareToolbar}
              crsLookup={crsLookup}
              // Not shown until the two are known to lie together: a reader should never be
              // looking at two surveys that only seem to be laid over one another.
              concealed={verdict !== 'together'}
              onViewer={onOverlayViewer}
              testId="caveview-compare-overlay"
            />
          )}
        </div>
      )}

      {under !== null && listsOpen && verdict !== 'apart' && (
        <div className="caveview-compare-lists" data-testid="caveview-compare-lists">
          <label className="caveview-compare-switch" title={t('caveview.compare.syncHint')}>
            <Switch
              size="small"
              checked={inStep}
              onChange={setInStep}
              data-testid="caveview-compare-sync"
            />
            <Typography.Text>{t('caveview.compare.sync')}</Typography.Text>
          </label>
          <div className="caveview-compare-list-columns">
            {COMPARE_SIDES.map((side) => {
              const surveys = lists[side];
              return (
                <div
                  key={side}
                  className="caveview-compare-list"
                  role="group"
                  aria-label={t('caveview.compare.surveysOf', { model: nameOf(side) })}
                  data-testid={`caveview-compare-list-${side}`}
                >
                  <div className="caveview-compare-list-head">
                    {under.mode === 'overlaid' && swatch(side)}
                    <Typography.Text strong ellipsis title={nameOf(side)}>
                      {nameOf(side)}
                    </Typography.Text>
                    <Button
                      size="small"
                      type="link"
                      disabled={surveys === null || hidden[side].size === 0}
                      onClick={() => setHidden((before) => showAllSurveys(before, side, inStep, lists))}
                      data-testid={`caveview-compare-show-all-${side}`}
                    >
                      {t('caveview.compare.showAll')}
                    </Button>
                  </div>
                  {surveys === null ? (
                    <Spin size="small" />
                  ) : surveys.length === 0 ? (
                    <Typography.Text type="secondary">{t('caveview.compare.noSurveys')}</Typography.Text>
                  ) : (
                    surveys.map((survey) => (
                      <Checkbox
                        key={survey.key}
                        checked={!hidden[side].has(survey.key)}
                        onChange={(event) =>
                          setHidden((before) =>
                            setSurveyShown(before, side, [survey.key], event.target.checked, inStep, lists),
                          )
                        }
                        data-testid={`caveview-compare-survey-${side}-${survey.path.join('.')}`}
                      >
                        {survey.name}
                      </Checkbox>
                    ))
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </>
  );
}
