"""Existing-library startup probe; SQLite backup is isolated, originals read-only.

Does not clear OS caches or alter the live library. Dispatcher/frame callback
timings and app-level window moves do not measure physical screen latency.
"""
import argparse, json, pathlib, sqlite3, statistics, subprocess

root = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--database', type=pathlib.Path, required=True)
p.add_argument('--app', type=pathlib.Path, required=True)
p.add_argument('--output', type=pathlib.Path, required=True)
p.add_argument('--runs', type=int, default=2)
p.add_argument('--trace', action='store_true')
a = p.parse_args()
assert a.runs > 0
a.output.mkdir(parents=True, exist_ok=True)
database = a.database.resolve()
with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True) as source:
    counts = {'albums': source.execute('SELECT COUNT(*) FROM albums').fetchone()[0],
              'photos': source.execute('SELECT COUNT(*) FROM photos').fetchone()[0]}
    results = []
    for run in range(a.runs):
        folder = a.output / f'run-{run+1}'
        folder.mkdir(exist_ok=True)
        with sqlite3.connect(folder / 'library.db') as target:
            source.backup(target)
        command = [str(a.app.resolve()), '--test-library', str(folder.resolve()), '--verify-gallery-startup']
        if a.trace:
            command = [str(root / '.tools/performance/dotnet-trace.exe'), 'collect',
                       '--profile', 'cpu-sampling', '--format', 'Speedscope',
                       '--output', str(folder / 'startup.nettrace'), '--'] + command
        process = subprocess.Popen(command)
        try:
            code = process.wait(timeout=100)
        except subprocess.TimeoutExpired:
            process.terminate(); process.wait(timeout=15)
            raise
        report = json.loads((folder / 'gallery-startup.json').read_text(encoding='utf-8'))
        assert code == 0 and report['complete'], report
        phases = {}
        for phase in sorted(set(s['phase'] for s in report['samples'])):
            delays = sorted(s['delay'] for s in report['samples'] if s['phase'] == phase)
            phases[phase] = {'count': len(delays), 'median': statistics.median(delays),
                             'p95': delays[min(len(delays)-1, int(len(delays)*.95))], 'max': max(delays)}
        row = {'run': run+1, 'readyMilliseconds': report['readyMilliseconds'],
               'refreshMilliseconds': report['refreshMilliseconds'],
               'allocatedBytes': report['allocatedBytes'], 'gcCollections': report['gcCollections'],
               'wicLoads': report['wicLoads'], 'phases': phases}
        results.append(row)
        print(json.dumps(row), flush=True)
        (a.output / 'summary.json').write_text(json.dumps({'counts': counts, 'results': results}, indent=2), encoding='utf-8')
