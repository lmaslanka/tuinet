#!/usr/bin/env bash
# Checks the packed Tuinet and Tuinet.Testing packages before they ship: README, icon, XML docs and the library are
# in each nupkg, nothing else is (no tests, samples or benchmarks), each snupkg carries the PDB, and each nuspec
# records the source commit (SourceLink). Usage: tools/pack/verify.sh <dir with .nupkg/.snupkg>
set -euo pipefail

dir="${1:?usage: verify.sh <package dir>}"
shopt -s nullglob
fail() { echo "verify: $*" >&2; exit 1; }

# verify <package id>: the id's packages are <id>.<version>.nupkg, and a version starts with a digit.
verify() {
  local id="$1"
  local nupkgs=("$dir/$id".[0-9]*.nupkg)
  local snupkgs=("$dir/$id".[0-9]*.snupkg)
  [ ${#nupkgs[@]} -eq 1 ] || fail "expected one $id .nupkg in $dir, found ${#nupkgs[@]}"
  [ ${#snupkgs[@]} -eq 1 ] || fail "expected one $id .snupkg in $dir, found ${#snupkgs[@]}"
  local nupkg="${nupkgs[0]}"
  local snupkg="${snupkgs[0]}"

  local files
  files="$(unzip -Z1 "$nupkg")"
  for required in README.md icon.png "$id.nuspec" "lib/net10.0/$id.dll" "lib/net10.0/$id.xml"; do
    grep -qx "$required" <<<"$files" || fail "$nupkg is missing $required"
  done

  # The only assembly is the library itself.
  local dlls
  dlls="$(grep -E '\.(dll|exe)$' <<<"$files")"
  [ "$dlls" = "lib/net10.0/$id.dll" ] || fail "unexpected assemblies in $nupkg:"$'\n'"$dlls"
  if grep -vxF -e "lib/net10.0/$id.dll" -e "lib/net10.0/$id.xml" -e "$id.nuspec" <<<"$files" | grep -iE 'test|sample|bench'; then
    fail "$nupkg contains test, sample or benchmark files"
  fi

  unzip -Z1 "$snupkg" | grep -qx "lib/net10.0/$id.pdb" || fail "$snupkg is missing lib/net10.0/$id.pdb"

  local nuspec
  nuspec="$(unzip -p "$nupkg" "$id.nuspec")"
  grep -qE '<repository type="git" url="https://github.com/lmaslanka/tuinet(\.git)?" ([a-z]+="[^"]*" )*commit="[0-9a-f]{40}"' <<<"$nuspec" \
    || fail "nuspec has no repository url + commit (SourceLink):"$'\n'"$(grep -i repository <<<"$nuspec" || true)"
  grep -q '<icon>icon.png</icon>' <<<"$nuspec" || fail "$id nuspec does not reference icon.png"
  grep -q '<readme>README.md</readme>' <<<"$nuspec" || fail "$id nuspec does not reference README.md"

  echo "verify: OK $(basename "$nupkg") ($(grep -c . <<<"$files") files) + $(basename "$snupkg")"
}

verify Tuinet
verify Tuinet.Testing

# Tuinet.Testing builds on the library: it must depend on the Tuinet package of the same version.
testing="$(echo "$dir"/Tuinet.Testing.[0-9]*.nupkg)"
version="$(basename "$testing" .nupkg)"
version="${version#Tuinet.Testing.}"
unzip -p "$testing" Tuinet.Testing.nuspec | grep -qF "<dependency id=\"Tuinet\" version=\"$version\"" \
  || fail "Tuinet.Testing does not depend on Tuinet $version"
