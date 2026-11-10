"""Archive the 50-episode grip-period measurement of the pilot checkpoint (and random contrast)."""
import json
from pathlib import Path
import shutil
from climb_eval_metrics import load, summarize

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'results/climb-grip-diagnostics'
OUT = ROOT/'docs/data/climb-grip-diagnostics'


def main():
    summary = {}
    for mode in ['checkpoint', 'random']:
        if not (SOURCE/mode/'report.json').exists():continue
        report, runtime = load(SOURCE/mode)
        summary[mode] = dict(report['summary'], metrics=summarize(report['episodes']))
        destination = OUT/mode;destination.mkdir(parents=True, exist_ok=True)
        (destination/'episodes.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
        shutil.copy2(runtime, destination/'runtime.csv')
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({m: s['metrics'] for m, s in summary.items()}, indent=1))


if __name__ == '__main__':main()
