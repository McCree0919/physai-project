"""Process and player-log guard for every script that starts a Unity player (2026-11-12).

Why: on 2026-11-11 a hand-started smoke run left Builds/ClimbCurriculum/Climb.exe running for ~24 h after its trainer had died, and its
Player-0.log grew to 353 GB. The player's first reset threw on every FixedUpdate (ML-Agents' Academy retries ForcedFullReset until it
succeeds and only quits on a trainer command), and mlagents 1.1 terminates its env worker after 10 s (WORKER_SHUTDOWN_TIMEOUT_S) while
the worker's UnityEnvironment.close() waits timeout_wait (60 s) before killing the player, so nothing killed it.

install(tag) does three things for the calling script:
 1. Before: lists leftover processes (players from either checkout's Builds/, a Unity editor on either project, mlagents trainers) and
    refuses to start if there are any. Every check is appended to results/process-guard.log and printed.
 2. During: puts the script into a Windows Job Object with KILL_ON_JOB_CLOSE. Every process it starts (trainer, env workers, players,
    crash handlers) is in the job, so when the script ends for any reason - normal exit, exception, Ctrl-C, being killed - Windows kills
    them all. A watchdog thread reads the -logFile of every Unity process in the job every 30 s; if one is above LOG_CAP_BYTES (normal
    player logs are <= 19 KB) it logs the event, kills the job's processes and exits the script with code 3.
 3. After (atexit): kills what is left in the job, then lists leftover processes again.
reap(tag) kills the job's processes (except the script itself) between runs, logging any it finds; runners call it after each player.
A guarded script's children inherit PHYSAI_PLAYER_GUARD and stay in the parent's job instead of installing their own.
`python scripts/player_guard.py` prints the leftover-process check and exits 1 if anything is left."""
import atexit
import ctypes
from ctypes import wintypes
import datetime
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
CHECKOUTS = [Path('C:/dev/physai-exp'), Path('C:/dev/physai-project')]
LOG = ROOT/'results/process-guard.log'
LOG_CAP_BYTES = 64*1024*1024
WATCH_SECONDS = 30
ENV_FLAG = 'PHYSAI_PLAYER_GUARD'
_job = None
_tag = None

kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)


class _BasicLimit(ctypes.Structure):
    _fields_ = [('PerProcessUserTimeLimit', ctypes.c_int64), ('PerJobUserTimeLimit', ctypes.c_int64), ('LimitFlags', wintypes.DWORD),
                ('MinimumWorkingSetSize', ctypes.c_size_t), ('MaximumWorkingSetSize', ctypes.c_size_t), ('ActiveProcessLimit', wintypes.DWORD),
                ('Affinity', ctypes.c_size_t), ('PriorityClass', wintypes.DWORD), ('SchedulingClass', wintypes.DWORD)]


class _IoCounters(ctypes.Structure):
    _fields_ = [(n, ctypes.c_uint64) for n in ('ReadOperationCount', 'WriteOperationCount', 'OtherOperationCount', 'ReadTransferCount', 'WriteTransferCount', 'OtherTransferCount')]


class _ExtendedLimit(ctypes.Structure):
    _fields_ = [('BasicLimitInformation', _BasicLimit), ('IoInfo', _IoCounters), ('ProcessMemoryLimit', ctypes.c_size_t), ('JobMemoryLimit', ctypes.c_size_t),
                ('PeakProcessMemoryUsed', ctypes.c_size_t), ('PeakJobMemoryUsed', ctypes.c_size_t)]


kernel32.CreateJobObjectW.restype = wintypes.HANDLE
kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
kernel32.GetCurrentProcess.restype = wintypes.HANDLE
kernel32.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, EXTENDED_LIMIT_INFORMATION, BASIC_PROCESS_ID_LIST = 0x2000, 9, 3


def _log(message):
    line = f'{datetime.datetime.now().isoformat(timespec="seconds")} [{_tag or Path(sys.argv[0]).name}] pid={os.getpid()} {message}'
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print('PLAYER_GUARD '+line, flush=True)


def processes():
    """All processes with path and command line (CIM), as dicts: pid, ppid, name, path, cmd."""
    script = ('Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Compress')
    out = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', script], capture_output=True, text=True, timeout=120).stdout
    rows = json.loads(out) if out.strip() else []
    return [dict(pid=r['ProcessId'], ppid=r['ParentProcessId'], name=(r['Name'] or ''), path=(r['ExecutablePath'] or ''), cmd=(r['CommandLine'] or '')) for r in rows]


def _ancestors(table):
    by = {p['pid']: p for p in table};out = set();pid = os.getpid()
    while pid in by and pid not in out:out.add(pid);pid = by[pid]['ppid']
    return out


def leftovers(table=None, exclude=()):
    """Players started from either checkout's Builds/, Unity editors on either project, and mlagents trainers - except this script,
    its ancestors and the given pids."""
    table = processes() if table is None else table;skip = _ancestors(table)|set(exclude)
    builds = [str(c/'Builds').lower().replace('/', '\\') for c in CHECKOUTS];projects = [str(c).lower().replace('\\', '/') for c in CHECKOUTS]
    found = []
    for p in table:
        if p['pid'] in skip:continue
        path, cmd = p['path'].lower(), p['cmd'].lower().replace('\\', '/')
        player = any(path.startswith(b) for b in builds)
        editor = p['name'].lower() == 'unity.exe' and any(x in cmd for x in projects)
        trainer = p['name'].lower().startswith('python') and ('climb_train_worker' in cmd or 'mlagents-learn' in cmd or 'mlagents.trainers' in cmd)
        if player or editor or trainer:found.append(dict(pid=p['pid'], name=p['name'], cmd=p['cmd'][:300]))
    return found


def _job_pids():
    if _job is None:return []
    class _List(ctypes.Structure):
        _fields_ = [('Assigned', wintypes.DWORD), ('InList', wintypes.DWORD), ('Ids', ctypes.c_size_t*4096)]
    data = _List()
    if not kernel32.QueryInformationJobObject(_job, BASIC_PROCESS_ID_LIST, ctypes.byref(data), ctypes.sizeof(data), None):return []
    return [int(data.Ids[i]) for i in range(data.InList)]


def _kill(pids):
    for pid in pids:subprocess.run(['taskkill', '/PID', str(pid), '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def reap(tag='reap'):
    """Kill every process left in this script's job except the script itself; returns what was found."""
    deadline = time.time()+10  # a closing player and its crash handler need a moment to exit on their own
    while time.time() < deadline and [p for p in _job_pids() if p != os.getpid()]:time.sleep(.5)
    pids = [p for p in _job_pids() if p != os.getpid()]
    if pids:
        table = {p['pid']: p for p in processes()};alive = [p for p in pids if p in table]
        if alive:_log(f'{tag}: killing {len(alive)} LEFTOVER job process(es): '+json.dumps([dict(pid=p, name=table[p]['name'], cmd=table[p]['cmd'][:200]) for p in alive]))
        _kill(pids);time.sleep(1)
        return alive
    return []


def _log_files(table):
    files = []
    for p in table:
        if p['pid'] not in set(_job_pids()):continue
        try:args = shlex.split(p['cmd'], posix=False)
        except ValueError:continue
        for i, a in enumerate(args[:-1]):
            if a.lower() == '-logfile' and args[i+1] != '-':files.append(Path(args[i+1].strip('"')))
    return files


def _watch():
    while True:
        time.sleep(WATCH_SECONDS)
        try:
            for f in _log_files(processes()):
                size = f.stat().st_size if f.exists() else 0
                if size > LOG_CAP_BYTES:
                    _log(f'LOG_CAP exceeded: {f} is {size} bytes (cap {LOG_CAP_BYTES}); killing the job and exiting with code 3')
                    reap('log-cap');os._exit(3)
        except Exception as ex:  # the watchdog must never take the run down by itself
            _log(f'watchdog error (ignored): {ex!r}')


def _at_exit():
    reap('exit')
    left = leftovers()
    _log('after: '+('no leftover processes' if not left else f'{len(left)} LEFTOVER process(es): '+json.dumps(left)))


def install(tag=None):
    """Call first in a script's entry point. Raises if leftover processes exist or the job cannot be created."""
    global _job, _tag
    _tag = tag or Path(sys.argv[0]).name
    if os.environ.get(ENV_FLAG):
        _log(f'inside guarded parent pid={os.environ[ENV_FLAG]}; using its job');return
    left = leftovers()
    _log('before: '+('no leftover processes' if not left else f'{len(left)} LEFTOVER process(es): '+json.dumps(left)))
    if left:raise RuntimeError(f'{len(left)} leftover player/editor/trainer process(es) are running; close them first (see {LOG})')
    job = kernel32.CreateJobObjectW(None, None)
    if not job:raise ctypes.WinError(ctypes.get_last_error())
    info = _ExtendedLimit();info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if not kernel32.SetInformationJobObject(job, EXTENDED_LIMIT_INFORMATION, ctypes.byref(info), ctypes.sizeof(info)):raise ctypes.WinError(ctypes.get_last_error())
    if not kernel32.AssignProcessToJobObject(job, kernel32.GetCurrentProcess()):raise ctypes.WinError(ctypes.get_last_error())
    _job = job;os.environ[ENV_FLAG] = str(os.getpid())
    atexit.register(_at_exit)
    threading.Thread(target=_watch, name='player-log-watchdog', daemon=True).start()
    _log(f'job installed (kill on close), player log cap {LOG_CAP_BYTES} bytes, checked every {WATCH_SECONDS} s')


if __name__ == '__main__':
    _tag = 'check';left = leftovers()
    _log('check: '+('no leftover processes' if not left else f'{len(left)} LEFTOVER process(es): '+json.dumps(left)))
    sys.exit(1 if left else 0)
