import sys, glob, re, os
tot = {}; oom = 0; hangs = 0
for root in sys.argv[1:]:
    for log in glob.glob(os.path.join(root, 'w*', 'log.txt')):
        mode = os.path.basename(os.path.dirname(log)).split('-', 1)[1]
        last = 0; s = 0; o = 0; lo = 0
        for line in open(log, encoding='utf-8', errors='replace'):
            if line.startswith('start'):
                s += last; o += lo; last = 0; lo = 0
            m = re.search(r'execs=(\d+)', line)
            if m: last = int(m.group(1))
            m = re.search(r'oom=(\d+)', line)
            if m: lo = int(m.group(1))
        s += last; o += lo
        tot[mode] = tot.get(mode, 0) + s; oom += o
        hangs += len(glob.glob(os.path.join(os.path.dirname(log), 'hang', '*.tif')))
    print(root, tot, 'total', sum(tot.values()), 'oom', oom, 'hang files', hangs)
    tot = {}; oom = 0; hangs = 0
