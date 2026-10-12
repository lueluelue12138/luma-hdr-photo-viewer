"""Fresh-process startup and idle resources. OS/file caches are not flushed.
GPU usage comes from Windows' process counters, not our texture estimate.
"""
import argparse, json, pathlib, sqlite3, subprocess, time
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('photo',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--runs',type=int,default=2);a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True);results=[]
for scenario in ['gallery','SDR','HDR']:
    for run in range(a.runs):
        folder=a.output/f'{scenario}-{run+1}';folder.mkdir()
        if scenario=='HDR':
            with sqlite3.connect(folder/'library.db') as db:
                db.execute('CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL)')
                db.execute('INSERT INTO settings VALUES(?,?)',('viewer.preferences',json.dumps({'Hdr':True})))
        cmd=[str(root/'artifacts/Luma/Luma.App.exe'),'--test-library',str(folder.resolve()),'--verify-startup']
        if scenario!='gallery':cmd.append(str(a.photo.resolve()))
        start=time.perf_counter();proc=subprocess.Popen(cmd)
        while not (folder/'startup-ready.json').exists() and proc.poll() is None and time.perf_counter()-start<40:time.sleep(.02)
        external=(time.perf_counter()-start)*1000
        gpu=None
        try:
            capture=subprocess.run(['powershell','-NoProfile','-Command',f'Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory | Where-Object {{ $_.Name -like "pid_{proc.pid}_*" }} | Select-Object Name,DedicatedUsage,SharedUsage,TotalCommitted | ConvertTo-Json -Compress'],capture_output=True,text=True,timeout=15)
            if capture.stdout.strip():gpu=json.loads(capture.stdout)
        except subprocess.TimeoutExpired:pass
        try:code=proc.wait(timeout=50)
        except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=15);raise
        data=json.loads((folder/'startup.json').read_text(encoding='utf-8'));assert code==0 and data['complete'],data
        samples=data['results'];idle=next(r for r in samples if r['phase']=='idle-start');last=samples[-1]
        one_core=(last['cpuMilliseconds']-idle['cpuMilliseconds'])/(last['milliseconds']-idle['milliseconds'])*100
        row={'scenario':scenario,'run':run+1,'externalReadyMilliseconds':external,'idleOneCorePercent':one_core,'gpuProcessCounters':gpu,**data}
        results.append(row);print(scenario,run+1,f'ready {external:.0f} ms, idle {one_core:.2f}% of one core, working {last["workingBytes"]/1048576:.1f} MiB, private {last["privateBytes"]/1048576:.1f} MiB',flush=True)
        a.output.joinpath('startup-summary.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(a.output.resolve())
