"""Compute the reference topology metrics for the committed karst networks.

Run inside the pinned container by karstnet-oracle.mjs; not importable by the server and
never on a request path. It reads a <name>_nodes.dat / <name>_links.dat pair, hands it to
the reference implementation, and writes one JSON document per network. Those documents
are committed, and the .NET suite asserts against them without needing Python installed.

Two things this file exists to pin, because both are readings of the published method that
the reference implementation settles and prose does not:

  * length entropy is a NORMALISED entropy over ten bins spanning the fixed range 0..100
    after the branch lengths are scaled by their own maximum, not a Shannon entropy over
    the observed values, and a single branch yields 0 rather than an undefined value;
  * orientation entropy is length-weighted over eighteen bins spanning the fixed range
    0..180, and it is computed over the COMPLETE graph's edges while every other metric
    here is computed over the reduced graph.

Everything reported is read back off the reference objects rather than recomputed, except
the counts and ratios the reference only prints (they are recomputed here from the same
two graphs it prints them from) and the clustering coefficient, which the reference does
not define at all and which is therefore taken straight from the graph library and
labelled as such.
"""

import json
import math
import os
import sys

import networkx as nx
import numpy as np

import karstnet as kn


def _finite(value):
    """JSON has no NaN. A metric the reference leaves undefined is reported as null."""
    if value is None:
        return None
    number = float(value)
    return None if math.isnan(number) or math.isinf(number) else number


def _degree_summary(graph):
    degrees = np.array([d for _, d in graph.degree()], dtype=float)
    mean = float(np.mean(degrees))
    # ddof=1 throughout, matching the reference: these are sample estimates over one
    # surveyed network, not the population of every network that could have been surveyed.
    sd = float(np.std(degrees, ddof=1)) if degrees.size > 1 else 0.0
    histogram = {}
    for _, degree in graph.degree():
        histogram[str(degree)] = histogram.get(str(degree), 0) + 1
    return {
        "mean": mean,
        "standardDeviation": sd,
        "coefficientOfVariation": (sd / mean) if mean else 0.0,
        "histogram": dict(sorted(histogram.items(), key=lambda kv: int(kv[0]))),
    }


def measure(basename):
    graph = kn.from_nodlink_dat(basename)

    complete = graph.graph
    reduced = graph.graph_simpl

    node_count = nx.number_of_nodes(reduced)
    edge_count = nx.number_of_edges(reduced)
    component_count = nx.number_connected_components(reduced)
    cyclomatic = edge_count - node_count + component_count

    extremities = sum(1 for n in reduced.nodes() if reduced.degree(n) == 1)
    junctions = sum(1 for n in reduced.nodes() if reduced.degree(n) > 2)

    branch_lengths = np.asarray(graph.br_lengths, dtype=float)
    tortuosities = np.asarray(graph.br_tort, dtype=float)
    looping_branches = int(np.count_nonzero(np.isnan(tortuosities)))

    mean_degree, cv_degree = graph.mean_degree_and_CV()

    return {
        "network": basename,
        # The complete graph is every surveyed station and shot, after the graph library
        # has collapsed parallel edges. The reduced graph is that graph with every chain
        # of degree-2 stations contracted to a single branch. Almost every metric below
        # is a property of the reduced graph; the two entropies are the exception noted
        # in the module docstring.
        "completeGraph": {
            "nodeCount": nx.number_of_nodes(complete),
            "edgeCount": nx.number_of_edges(complete),
            "componentCount": nx.number_connected_components(complete),
        },
        "reducedGraph": {
            "nodeCount": node_count,
            "edgeCount": edge_count,
            "componentCount": component_count,
            "cyclomaticNumber": cyclomatic,
            "extremityCount": extremities,
            "junctionCount": junctions,
        },
        "howard": {
            "alpha": cyclomatic / (2 * node_count - 5),
            "beta": edge_count / node_count,
            "gamma": edge_count / (3 * (node_count - 2)),
        },
        "degree": _degree_summary(reduced),
        "branches": {
            "count": int(branch_lengths.size),
            "loopingCount": looping_branches,
            "meanLengthM": _finite(graph.mean_length()),
            "coefficientOfVariation": _finite(graph.coef_variation_length()),
            "minLengthM": _finite(branch_lengths.min()) if branch_lengths.size else None,
            "maxLengthM": _finite(branch_lengths.max()) if branch_lengths.size else None,
        },
        "metrics": {
            "meanLengthM": _finite(graph.mean_length()),
            "lengthCoefficientOfVariation": _finite(graph.coef_variation_length()),
            "lengthEntropy": _finite(graph.length_entropy()),
            "orientationEntropy": _finite(graph.orientation_entropy()),
            "meanTortuosity": _finite(graph.mean_tortuosity()),
            "averageShortestPathLength": _finite(graph.average_SPL()),
            "centralPointDominance": _finite(graph.central_point_dominance()),
            "meanDegree": _finite(mean_degree),
            "degreeCoefficientOfVariation": _finite(cv_degree),
            "correlationOfVertexDegree": _finite(graph.correlation_vertex_degree()),
        },
        # Not a reference-implementation figure: it defines no clustering coefficient.
        # Taken from the graph library directly so the .NET implementation still has
        # something independent to be wrong against, and labelled so nobody later reads
        # it as part of the published method.
        "graphLibraryOnly": {
            "averageClusteringCoefficient": _finite(nx.average_clustering(reduced)),
        },
    }


def main(argv):
    if len(argv) < 3:
        print("usage: oracle.py <fixture-dir> <network> [<network> ...]", file=sys.stderr)
        return 2

    fixture_dir = argv[1]
    os.chdir(fixture_dir)

    for network in argv[2:]:
        result = measure(network)
        out = network.lower() + ".golden.json"
        with open(out, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(result, handle, indent=2, sort_keys=False)
            handle.write("\n")
        print("wrote " + out)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
