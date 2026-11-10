"""Per-episode and aggregate metrics from a climb_behavior_diagnostics report (standard library only)."""
import collections
import csv
import json
from pathlib import Path
import statistics

BINS = ['<0.60', '0.60-0.84', '>=0.84']


def quantile(values, q):
    values = sorted(values)
    p = (len(values)-1)*q
    lo = int(p)
    return values[lo]+(values[min(lo+1, len(values)-1)]-values[lo])*(p-lo)


def stats(values):
    return dict(mean=statistics.mean(values), min=min(values), p50=quantile(values, .5), p95=quantile(values, .95), max=max(values)) if values else None


def load(folder):
    """Join report.json episodes with the newest runtime CSV by (arena, per-arena episode index)."""
    folder = Path(folder)
    report = json.loads((folder/'report.json').read_text())
    runtime = max((folder/'runtime').glob('episodes-*.csv'), key=lambda p: p.stat().st_mtime_ns)
    by_key, counters = {}, collections.Counter()
    with runtime.open() as stream:
        for row in csv.DictReader(stream):
            by_key[row['arena'], counters[row['arena']]] = row
            counters[row['arena']] += 1
    for row in report['episodes']:
        row['terminal'] = by_key[row['physics']['arena'], row['physics']['episode']]
    return report, runtime


def summarize(rows):
    p = [r['physics'] for r in rows]
    n = len(rows)
    grip_ticks = sum(x['gripHandTicks'] for x in p)
    def grip_mean(field): return sum(x[field] for x in p)/grip_ticks if grip_ticks else None
    histogram = [sum(x['gripCommandHistogram'][i] for x in p) for i in range(18)]
    bins = []
    for b in range(3):
        ticks = sum(x['binTicks'][b] for x in p)
        bins.append(dict(range=BINS[b], ticks=ticks, fraction=ticks/grip_ticks if grip_ticks else None,
                         pullMeanN=sum(x['binPullSum'][b] for x in p)/ticks if ticks else None,
                         torsoUpForceMeanN=sum(x['binTorsoUpForceSum'][b] for x in p)/ticks if ticks else None,
                         torsoVyMean=sum(x['binTorsoVyStepSum'][b] for x in p)/ticks if ticks else None,
                         overshootMeanM=sum(x['binOvershootSum'][b] for x in p)/ticks if ticks else None))
    heights = [x['maximumTorsoY'] for x in p]
    free = sum(x['freeHandTicks'] for x in p)
    return dict(
        episodes=n,
        gripEpisodeFraction=sum(x['gripSuccesses'] > 0 for x in p)/n,
        legalContactRate=sum(x['legalContactTicks'] for x in p)/free if free else None,
        legalContactEpisodeFraction=sum(x['legalContactTicks'] > 0 for x in p)/n,
        level1Rate=sum(int(r['terminal']['level1']) for r in rows)/n,
        level2Rate=sum(int(r['terminal']['level2']) for r in rows)/n,
        successRate=sum(int(r['terminal']['success']) for r in rows)/n,
        terminalReasons=dict(collections.Counter(r['terminal']['reason'] for r in rows)),
        maximumTorsoY=stats(heights),
        maximumTorsoYHistogram=dict(edges=[.4+.1*i for i in range(12)], counts=[sum(.4+.1*i <= h < .4+.1*(i+1) for h in heights) for i in range(11)]),
        commandOuterFraction=sum(r['commandTargets']['outerFraction']*r['commandTargets']['samples'] for r in rows)/sum(r['commandTargets']['samples'] for r in rows),
        pdTargetOuterFraction=sum(x['actualOuterBoundary'] for x in p)/sum(x['actualTargetSamples'] for x in p),
        grippedSecondsMean=statistics.mean(x['grippedSeconds'] for x in p),
        grip=dict(handTicks=grip_ticks, commandRadiusMean=grip_mean('gripCommandRadiusSum'), commandOuterFraction=grip_mean('gripCommandOuter'),
                  pdRadiusMean=grip_mean('gripPdRadiusSum'), armLengthMean=grip_mean('gripArmSum'), overshootMean=grip_mean('gripOvershootSum'),
                  forceMagnitudeMeanN=grip_mean('gripForceSum'), pullMeanN=grip_mean('gripPullSum'), pushFraction=grip_mean('gripPushTicks'),
                  torsoUpForceMeanN=grip_mean('gripTorsoUpForceSum'),
                  handBelowShoulderFraction=grip_mean('gripHandBelowShoulderTicks'),
                  handOffsetTorsoFrameMeanM=[sum(x['gripHandOffsetTorsoSum'][k] for x in p)/grip_ticks for k in range(3)] if grip_ticks else None,
                  forceTorsoFrameMeanN=[sum(x['gripForceTorsoSum'][k] for x in p)/grip_ticks for k in range(3)] if grip_ticks else None,
                  commandRadiusHistogram=dict(binWidthM=.05, counts=histogram), byCommandRadius=bins,
                  episodesWithMaxGripTorsoAbove06=sum(x['maximumGripTorsoY'] > .6 for x in p)))
