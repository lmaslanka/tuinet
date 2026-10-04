#!/usr/bin/env bash
# Checks the packed Tuinet package before it ships: README, icon, XML docs and the library are in the
# nupkg, nothing else is (no tests, samples or benchmarks), the snupkg carries the PDB, and the nuspec
# records the source commit (SourceLink). Usage: tools/pack/verify.sh <dir with .nupkg/.snupkg>
set -euo pipefail

dir="${1:?usage: verify.sh <package dir>}"
shopt -s nullglob
nupkgs=("$dir"/Tuinet.*.nupkg)
snupkgs=("$dir"/Tuinet.*.snupkg)
fail() { echo "verify: $*" >&2; exit 1; }

[ ${#nupkgs[@]} -eq 1 ] || fail "expected one Tuinet .nupkg in $dir, found ${#nupkgs[@]}"
[ ${#snupkgs[@]} -eq 1 ] || fail "expected one Tuinet .snupkg in $dir, found ${#snupkgs[@]}"
nupkg="${nupkgs[0]}"
snupkg="${snupkgs[0]}"

files="$(unzip -Z1 "$nupkg")"
for required in README.md icon.png Tuinet.nuspec lib/net10.0/Tuinet.dll lib/net10.0/Tuinet.xml; do
  grep -qx "$required" <<<"$files" || fail "$nupkg is missing $required"
done

# The only assembly is the library itself.
dlls="$(grep -E '\.(dll|exe)$' <<<"$files")"
[ "$dlls" = "lib/net10.0/Tuinet.dll" ] || fail "unexpected assemblies in $nupkg:"$'\n'"$dlls"
if grep -iE 'test|sample|bench' <<<"$files"; then fail "$nupkg contains test, sample or benchmark files"; fi

unzip -Z1 "$snupkg" | grep -qx 'lib/net10.0/Tuinet.pdb' || fail "$snupkg is missing lib/net10.0/Tuinet.pdb"

nuspec="$(unzip -p "$nupkg" Tuinet.nuspec)"
grep -qE '<repository type="git" url="https://github.com/lmaslanka/tuinet(\.git)?" ([a-z]+="[^"]*" )*commit="[0-9a-f]{40}"' <<<"$nuspec" \
  || fail "nuspec has no repository url + commit (SourceLink):"$'\n'"$(grep -i repository <<<"$nuspec" || true)"
grep -q '<icon>icon.png</icon>' <<<"$nuspec" || fail "nuspec does not reference icon.png"
grep -q '<readme>README.md</readme>' <<<"$nuspec" || fail "nuspec does not reference README.md"

echo "verify: OK $(basename "$nupkg") ($(grep -c . <<<"$files") files) + $(basename "$snupkg")"
