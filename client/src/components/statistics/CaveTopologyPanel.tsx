// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Descriptions, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { caveHasMeasurableSurvey, useCaveTopology, useSurveyModels } from '../../api/hooks.ts';

/**
 * The shape of a cave's passage network, as the figures the karst literature uses to tell one kind
 * of cave from another.
 *
 * <p>
 * Every figure carries a sentence saying what it means, and that is the point of the panel rather
 * than decoration on it. "Cyclomatic number: 14" tells a caver nothing; "independent loops: 14 —
 * how many ways round the network there are that are not simply the same way twice" tells them
 * what kind of cave they are looking at. These are terms out of a literature most readers of this
 * page have not read, and a number nobody can interpret is worse than no number, because it looks
 * like information.
 * </p>
 * <p>
 * A missing figure is a dash, never a zero. Several of these are undefined rather than nought for
 * a small network — a network of two junctions has no connectivity ratio, a single branch has no
 * spread of lengths — and printing zero would state something about the cave that was never
 * measured.
 * </p>
 * <p>
 * The counts of what the reading dropped and merged sit under the figures on every answer, not
 * behind a control. A network that silently lost legs still produces entirely reasonable-looking
 * numbers, and this sentence is the only thing that says they were computed over less than the
 * file contained. It distinguishes nothing lost (a count of nought) from not known (no count at
 * all), because those are different claims.
 * </p>
 */
export default function CaveTopologyPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  // Shares the cave page's existing list rather than adding a request. Two jobs: it turns the
  // upload these figures came from into the name the reader sees in that list, and it says whether
  // there is anything to measure at all — a cave with no read line plot is asked nothing, because
  // the answer would be a refusal and a refused request is an error on the console of every cave
  // page in the application.
  const { data: models } = useSurveyModels(caveId);
  const { data, isError } = useCaveTopology(caveId, caveHasMeasurableSurvey(models));

  // Three refusals arrive here as one: a cave the caller may not read, one they may read but not
  // place exactly, and one whose passage network has never been measured. In all three a panel of
  // blanks would be a claim about the cave rather than about what is known, so there is no panel.
  //
  // The same holds while the answer is still being fetched, which is why there is no skeleton
  // here: a grid of dashes that resolves into either figures or nothing at all reads as a cave
  // measured to be empty for however long it is on screen.
  if (isError || !data) return null;

  const dash = '—';
  const metres = (value: number | null | undefined) =>
    typeof value === 'number' ? t('trips.metres', { value: Math.round(value * 10) / 10 }) : dash;
  // Two decimals on every ratio and every entropy: these are quotients and normalised measures
  // taken over a survey whose own lengths are recorded to the centimetre, and a third decimal
  // claims a precision that survey does not carry.
  const ratio = (value: number | null | undefined) => (typeof value === 'number' ? value.toFixed(2) : dash);
  const count = (value: number | null | undefined) => (typeof value === 'number' ? String(value) : dash);

  const groups: {
    key: string;
    title: string;
    rows: { key: string; label: string; gloss: string; value: string }[];
  }[] = [
    {
      key: 'size',
      title: t('statistics.topology.sizeTitle'),
      rows: [
        {
          key: 'nodes',
          label: t('statistics.topology.nodes'),
          gloss: t('statistics.topology.nodesGloss'),
          value: count(data.nodeCount),
        },
        {
          key: 'edges',
          label: t('statistics.topology.edges'),
          gloss: t('statistics.topology.edgesGloss'),
          value: count(data.edgeCount),
        },
        {
          key: 'components',
          label: t('statistics.topology.components'),
          gloss: t('statistics.topology.componentsGloss'),
          value: count(data.componentCount),
        },
        {
          key: 'junctions',
          label: t('statistics.topology.junctions'),
          gloss: t('statistics.topology.junctionsGloss'),
          value: count(data.junctionCount),
        },
        {
          key: 'extremities',
          label: t('statistics.topology.extremities'),
          gloss: t('statistics.topology.extremitiesGloss'),
          value: count(data.extremityCount),
        },
        {
          key: 'cyclomatic',
          label: t('statistics.topology.cyclomatic'),
          gloss: t('statistics.topology.cyclomaticGloss'),
          value: count(data.cyclomaticNumber),
        },
      ],
    },
    {
      key: 'reduced',
      title: t('statistics.topology.reducedTitle'),
      rows: [
        {
          key: 'reducedNodes',
          label: t('statistics.topology.reducedNodes'),
          gloss: t('statistics.topology.reducedNodesGloss'),
          value: count(data.reducedNodeCount),
        },
        {
          key: 'reducedEdges',
          label: t('statistics.topology.reducedEdges'),
          gloss: t('statistics.topology.reducedEdgesGloss'),
          value: count(data.reducedEdgeCount),
        },
        {
          key: 'reducedComponents',
          label: t('statistics.topology.reducedComponents'),
          gloss: t('statistics.topology.reducedComponentsGloss'),
          value: count(data.reducedComponentCount),
        },
        {
          key: 'alpha',
          label: t('statistics.topology.alpha'),
          gloss: t('statistics.topology.alphaGloss'),
          value: ratio(data.alpha),
        },
        {
          key: 'beta',
          label: t('statistics.topology.beta'),
          gloss: t('statistics.topology.betaGloss'),
          value: ratio(data.beta),
        },
        {
          key: 'gamma',
          label: t('statistics.topology.gamma'),
          gloss: t('statistics.topology.gammaGloss'),
          value: ratio(data.gamma),
        },
      ],
    },
    {
      key: 'degree',
      title: t('statistics.topology.degreeTitle'),
      rows: [
        {
          key: 'meanDegree',
          label: t('statistics.topology.meanDegree'),
          gloss: t('statistics.topology.meanDegreeGloss'),
          value: ratio(data.meanDegree),
        },
        {
          key: 'degreeSpread',
          label: t('statistics.topology.degreeSpread'),
          gloss: t('statistics.topology.degreeSpreadGloss'),
          value: ratio(data.degreeStandardDeviation),
        },
        {
          key: 'degreeVariation',
          label: t('statistics.topology.degreeVariation'),
          gloss: t('statistics.topology.degreeVariationGloss'),
          value: ratio(data.degreeCoefficientOfVariation),
        },
        {
          key: 'degreeCorrelation',
          label: t('statistics.topology.degreeCorrelation'),
          gloss: t('statistics.topology.degreeCorrelationGloss'),
          value: ratio(data.correlationOfVertexDegree),
        },
      ],
    },
    {
      key: 'branches',
      title: t('statistics.topology.branchesTitle'),
      rows: [
        {
          key: 'branches',
          label: t('statistics.topology.branches'),
          gloss: t('statistics.topology.branchesGloss'),
          value: count(data.branchCount),
        },
        {
          key: 'loopingBranches',
          label: t('statistics.topology.loopingBranches'),
          gloss: t('statistics.topology.loopingBranchesGloss'),
          value: count(data.loopingBranchCount),
        },
        {
          key: 'meanBranchLength',
          label: t('statistics.topology.meanBranchLength'),
          gloss: t('statistics.topology.meanBranchLengthGloss'),
          value: metres(data.meanBranchLengthM),
        },
        {
          key: 'shortestBranch',
          label: t('statistics.topology.shortestBranch'),
          gloss: t('statistics.topology.shortestBranchGloss'),
          value: metres(data.minBranchLengthM),
        },
        {
          key: 'longestBranch',
          label: t('statistics.topology.longestBranch'),
          gloss: t('statistics.topology.longestBranchGloss'),
          value: metres(data.maxBranchLengthM),
        },
        {
          key: 'branchLengthVariation',
          label: t('statistics.topology.branchLengthVariation'),
          gloss: t('statistics.topology.branchLengthVariationGloss'),
          value: ratio(data.branchLengthCoefficientOfVariation),
        },
      ],
    },
    {
      key: 'shape',
      title: t('statistics.topology.shapeTitle'),
      rows: [
        {
          key: 'lengthEntropy',
          label: t('statistics.topology.lengthEntropy'),
          gloss: t('statistics.topology.lengthEntropyGloss'),
          value: ratio(data.lengthEntropy),
        },
        {
          key: 'orientationEntropy',
          label: t('statistics.topology.orientationEntropy'),
          gloss: t('statistics.topology.orientationEntropyGloss'),
          value: ratio(data.orientationEntropy),
        },
        {
          key: 'tortuosity',
          label: t('statistics.topology.tortuosity'),
          gloss: t('statistics.topology.tortuosityGloss'),
          value: ratio(data.meanTortuosity),
        },
        {
          key: 'averagePath',
          label: t('statistics.topology.averagePath'),
          gloss: t('statistics.topology.averagePathGloss'),
          value: ratio(data.averageShortestPathLength),
        },
        {
          key: 'centralPointDominance',
          label: t('statistics.topology.centralPointDominance'),
          gloss: t('statistics.topology.centralPointDominanceGloss'),
          value: ratio(data.centralPointDominance),
        },
        {
          key: 'clustering',
          label: t('statistics.topology.clustering'),
          gloss: t('statistics.topology.clusteringGloss'),
          value: ratio(data.averageClusteringCoefficient),
        },
      ],
    },
  ];

  // Which of the cave's uploads produced these figures. Named where the name is known, and by its
  // identifier otherwise — a corrected re-export is a new upload rather than a replacement, so two
  // sets of numbers about one cave describe the same passage only if they came from the same one.
  const answeringModel = data.surveyModelId
    ? (models?.find((model) => model.id === data.surveyModelId)?.name ?? data.surveyModelId)
    : null;

  return (
    <Card size="small" title={t('statistics.topology.title')} style={{ marginTop: 16 }}>
      <div data-testid="cave-topology">
        {groups.map((group) => (
          <Descriptions
            key={group.key}
            size="small"
            bordered
            column={{ xs: 1, sm: 1, md: 2 }}
            title={group.title}
            style={{ marginTop: group.key === 'size' ? 0 : 16 }}
            items={group.rows.map((row) => ({
              key: row.key,
              label: row.label,
              children: (
                <span data-testid={`topology-${row.key}`}>
                  <Typography.Text strong>{row.value}</Typography.Text>
                  <br />
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {row.gloss}
                  </Typography.Text>
                </span>
              ),
            }))}
          />
        ))}

        <Typography.Paragraph
          type="secondary"
          data-testid="cave-topology-completeness"
          style={{ marginTop: 16, marginBottom: 0 }}
        >
          {typeof data.droppedShotCount !== 'number' || typeof data.mergedStationCount !== 'number'
            ? t('statistics.topology.completenessUnknown')
            : data.droppedShotCount === 0 && data.mergedStationCount === 0
              ? t('statistics.topology.completenessWhole')
              : t('statistics.topology.completenessPartial', {
                  dropped: data.droppedShotCount,
                  merged: data.mergedStationCount,
                })}
        </Typography.Paragraph>

        {answeringModel !== null && (
          <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
            {t('statistics.topology.measuredFromModel', {
              name: answeringModel,
            })}
          </Typography.Paragraph>
        )}
      </div>
    </Card>
  );
}
