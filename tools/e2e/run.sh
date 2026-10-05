#!/usr/bin/env bash
# End-to-end checks in a real terminal (tmux): publish the apps with Native AOT, then run e2e.py.
#
#   tools/e2e/run.sh [publish-dir]
#
# Binaries already in publish-dir/{Showcase,Stress,Inline} are reused (CI publishes them first); anything missing
# is published there. Default publish-dir: artifacts/e2e. Needs tmux and python3.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${1:-$root/artifacts/e2e}" && cd "${1:-$root/artifacts/e2e}" && pwd)"

for tool in tmux python3 dotnet; do
  command -v "$tool" >/dev/null || { echo "e2e: $tool is required" >&2; exit 1; }
done

publish() {   # publish <project dir> <output dir> <binary name>
  if [ ! -x "$2/$3" ]; then
    dotnet publish "$1" -c Release -p:PublishAot=true -o "$2" --nologo -v quiet
  fi
}

publish "$root/samples/Tuinet.Samples.Showcase" "$out/Showcase" Tuinet.Samples.Showcase
publish "$root/samples/Tuinet.Samples.Stress" "$out/Stress" Tuinet.Samples.Stress
publish "$root/samples/Tuinet.Samples.Inline" "$out/Inline" Tuinet.Samples.Inline
publish "$root/tools/e2e/ClusterScreen" "$out/ClusterScreen" ClusterScreen

exec python3 "$root/tools/e2e/e2e.py" \
  --showcase "$out/Showcase/Tuinet.Samples.Showcase" \
  --stress "$out/Stress/Tuinet.Samples.Stress" \
  --inline "$out/Inline/Tuinet.Samples.Inline" \
  --clusters "$out/ClusterScreen/ClusterScreen" \
  --cases "$root/tools/e2e/clusters.txt"
