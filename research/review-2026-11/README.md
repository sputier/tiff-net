# Artefacts de la revue du 5 novembre 2026

Matériel de travail de la [revue](../../docs/review-2026-11-05.md) de la première implémentation (façade sur LibTiff.Net),
conservé tel quel pour la réécriture. **Rien ici n'est compilé ni exécuté par la solution** : les projets C# référencent les
sources de l'ancienne implémentation et des chemins locaux de la machine de revue. Ils servent de référence et seront repris,
adaptés, dans `tests/`, `tools/` et `benchmarks/`.

| Dossier | Contenu | Réutilisation prévue |
|---|---|---|
| `security/repro/` | 20 fichiers TIFF hostiles mesurés (dénis de service mémoire et CPU, bombes de décompression, JPEG progressif) — voir SEC-01 à SEC-07 | fixtures de non-régression : chaque fichier doit échouer vite, avec une `TiffException`, sous une borne d'allocation |
| `security/tools/` | générateurs Python de fichiers hostiles (`tiffcraft.py`, `craft_dos*.py`, `craft_progjpeg.py`) et scripts de campagne de fuzzing | `tools/` : génération des fixtures hostiles |
| `security/harness/` | fuzzer orienté structure (IFD, charge utile par codec, havoc), graines JPEG, mesure temps/allocations | harnais de fuzzing du dépôt (MAINT-09) |
| `performance/bench/` | benchmarks BenchmarkDotNet, prototype parallèle (`Proto.cs`), noyaux optimisés (prédicteurs Span, décodeur LZW, `Kernels.cs`) | `benchmarks/` et implémentation des codecs |
| `performance/py/` | génération de données synthétiques de type astro (tifffile) et mesures de référence tifffile | jeu de benchmarks |
| `performance/*.txt` | résultats bruts (mesures de référence de l'ancienne implémentation) | base de comparaison |
| `interop/` | génération du corpus tifffile (2 208 fichiers : types, couches, prédicteurs, ordres d'octets, BigTIFF) et empreintes SHA-256 attendues | tests d'interopérabilité (MAINT-05), oracle indépendant |
| `domain/siril/` | scripts `siril-cli` qui produisent les fichiers Siril 1.4.4 (Astro-TIFF, ICC, 8/16/32 bits, gris/RGB) | régénérer les fixtures Siril (les fichiers font jusqu'à 150 Mo) |
| `domain/py/` | génération de cas métier (miniature en IFD0, gris à 3 canaux, alpha) et vérifications | fixtures métier |
| `domain/probes/` | sondes C# de la revue métier (mémoire, aller-retour) | — |
