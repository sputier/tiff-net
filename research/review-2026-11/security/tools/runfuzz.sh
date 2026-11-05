#!/bin/bash
# Restarts a fuzz worker if it exits early (watchdog kill or process death), until the time budget is used.
S=<scratchpad>/review-security
H=$S/harness/bin/Release/net10.0/Harness.exe
export DOTNET_GCHeapHardLimit=0xA0000000
R=$2
worker() { mode=$1; id=$2; budget=$3; end=$((SECONDS+budget)); rs=$id; n=0; while [ $SECONDS -lt $end ]; do left=$((end-SECONDS)); o=$S/fuzz/$R/w$id-$mode; mkdir -p $o; $H fuzz $mode $o $left $rs $S/seeds $S/seeds2 /c/data/DEV/git/tiff-net/TestCase > /dev/null 2> $o/stderr-$n.txt; ec=$?; echo "exit $ec" >> $o/exits.txt; if [ $ec -ne 0 ] && [ $ec -ne 3 ]; then cp $o/current.bin $o/died-$n.bin; fi; n=$((n+1)); rs=$((rs+1000)); done; }
B=$1
worker ifd 1 $B & worker ifd 2 $B & worker payload 3 $B & worker payload 4 $B & worker havoc 5 $B & worker mixed 6 $B & worker mixed 7 $B &
wait
echo ALLDONE
