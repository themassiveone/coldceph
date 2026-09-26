#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:-0.0.0}"
out="${2:-$root/artifacts/debs}"
rid="linux-x64"

rm -rf "$out"
mkdir -p "$out"

publish() {
  local project="$1"
  local dest="$2"
  dotnet publish "$root/$project/$project.csproj" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -o "$dest"
}

stage_deb() {
  local pkg="$1"
  local bin_src="$2"
  local unit="$3"
  local conf="$4"
  local dest_name="$5"
  local staged="$out/stage/$pkg"
  rm -rf "$staged"
  mkdir -p "$staged/DEBIAN"
  mkdir -p "$staged/opt/coldceph/$dest_name"
  mkdir -p "$staged/lib/systemd/system"
  mkdir -p "$staged/etc/coldceph"
  mkdir -p "$staged/var/lib/coldceph"

  cp -a "$bin_src/." "$staged/opt/coldceph/$dest_name/"
  cp "$root/packaging/linux/$unit" "$staged/lib/systemd/system/"
  cp "$root/packaging/linux/$conf" "$staged/etc/coldceph/"

  cat > "$staged/DEBIAN/control" <<EOF
Package: $pkg
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: ColdCeph <coldceph@local>
Description: ColdCeph $dest_name
EOF

  cat > "$staged/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
systemctl daemon-reload || true
EOF
  chmod 755 "$staged/DEBIAN/postinst"

  dpkg-deb --build "$staged" "$out/${pkg}_${version}_amd64.deb"
}

control_pub="$out/publish/control"
agent_pub="$out/publish/agent"
publish ColdCeph.Control "$control_pub"
publish ColdCeph.Agent "$agent_pub"
stage_deb coldceph-control "$control_pub" coldceph-control.service control.yaml control
stage_deb coldceph-agent "$agent_pub" coldceph-agent.service agent.yaml agent

(cd "$out" && sha256sum ./*.deb > SHA256SUMS)
