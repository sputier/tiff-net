#!/bin/bash
S=<scratchpad>/review-security
H=$S/harness/bin2/Harness.exe
export DOTNET_GCHeapHardLimit=0xA0000000
R=r3
worker() { mode=$1; id=$2; budget=$3; seeds=$4; end=$((SECONDS+budget)); rs=$((id*7+300)); n=0; while [ $SECONDS -lt $end ]; do left=$((end-SECONDS)); o=$S/fuzz/$R/w$id-$mode; mkdir -p $o; $H fuzz $mode $o $left $rs $seeds > /dev/null 2> $o/stderr-$n.txt; ec=$?; echo "exit $ec" >> $o/exits.txt; if [ $ec -ne 0 ] && [ $ec -ne 3 ]; then cp $o/current.bin $o/died-$n.bin; fi; n=$((n+1)); rs=$((rs+1000)); done; }
B=$1
J=$S/seedsjpeg
worker payload 1 $B $J & worker payload 2 $B $J & worker payload 3 $B $J & worker ifd 4 $B $J & worker havoc 5 $B $J & worker mixed 6 $B "$S/seeds $S/seeds2 /c/data/DEV/git/tiff-net/TestCase" & worker ifd 7 $B "$S/seeds"&
wait
echo ALLDONE
