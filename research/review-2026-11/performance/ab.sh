#!/usr/bin/env bash
# Interleaved A/B (baseline = repo sources, patched = apply_patches.py): B, P, B, P per case so that
# thermal / turbo drift on this laptop hits both sides equally. Output: ab_results.txt
cd "$(dirname "$0")"
B=bench/bin/Release/${TFM:-net10.0}/Bench.dll
P=patched/bin/Release/${TFM:-net10.0}/Patched.dll
OUT=${OUT:-ab_results.txt}
: > $OUT
cases="u16_24_lzw.tif u16_24_lzw_p2.tif u16_24_zip_p2.tif u16_24_zip.tif u16_24_none_be.tif u16_24_zip_p2_be.tif f32rgb_24_zip_p3.tif f32rgb_24_lzw_p3.tif f32rgb_24_planar_zip_p3.tif u16_61_zip_p2.tif w:u16_24:Lzw w:u16_24:Deflate w:u16_24:PackBits w:f32rgb_24:Lzw w:f32rgb_24:Deflate"
for c in ${CASES:-$cases}; do
  for round in 1 2; do
    dotnet $B quick 3 $c | grep -E "^(read|write)" | sed "s/^/B$round /" >> $OUT
    dotnet $P quick 3 $c | grep -E "^(read|write)" | sed "s/^/P$round /" >> $OUT
  done
done
echo finished >> $OUT
