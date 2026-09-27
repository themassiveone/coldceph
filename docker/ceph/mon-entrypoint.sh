#!/bin/bash
# Monitor host: run ceph-mon, publish conf/keyrings over HTTP from tmpfs.
# Nothing is written to a Docker volume.
set -euo pipefail

PUB=/tmp/ceph-publish
PORT="${CEPH_CONFIG_PORT:-3799}"
mkdir -p "${PUB}" \
  /var/lib/ceph/bootstrap-osd \
  /var/lib/ceph/bootstrap-rgw \
  /var/lib/ceph/bootstrap-mgr

ensure_bootstrap_key() {
  local name="$1"
  local profile="$2"
  local dest="$3"
  [[ -s "${dest}" ]] && return 0
  ceph auth get-or-create "${name}" mon "allow profile ${profile}" -o "${dest}" >/dev/null 2>&1 || true
}

publish_loop() {
  while true; do
    if ceph -s >/dev/null 2>&1; then
      ceph config set mon auth_allow_insecure_global_id_reclaim false >/dev/null 2>&1 || true
      ceph config set mon mon_warn_on_insecure_global_id_reclaim false >/dev/null 2>&1 || true
      ensure_bootstrap_key client.bootstrap-osd bootstrap-osd /var/lib/ceph/bootstrap-osd/ceph.keyring
      ensure_bootstrap_key client.bootstrap-mgr bootstrap-mgr /var/lib/ceph/bootstrap-mgr/ceph.keyring
      ensure_bootstrap_key client.bootstrap-rgw bootstrap-rgw /var/lib/ceph/bootstrap-rgw/ceph.keyring
    fi
    [[ -s /etc/ceph/ceph.conf ]] && cp -f /etc/ceph/ceph.conf "${PUB}/ceph.conf" || true
    [[ -s /etc/ceph/ceph.client.admin.keyring ]] \
      && cp -f /etc/ceph/ceph.client.admin.keyring "${PUB}/ceph.client.admin.keyring" || true
    [[ -s /var/lib/ceph/bootstrap-osd/ceph.keyring ]] \
      && cp -f /var/lib/ceph/bootstrap-osd/ceph.keyring "${PUB}/bootstrap-osd.keyring" || true
    [[ -s /var/lib/ceph/bootstrap-rgw/ceph.keyring ]] \
      && cp -f /var/lib/ceph/bootstrap-rgw/ceph.keyring "${PUB}/bootstrap-rgw.keyring" || true
    [[ -s /var/lib/ceph/bootstrap-mgr/ceph.keyring ]] \
      && cp -f /var/lib/ceph/bootstrap-mgr/ceph.keyring "${PUB}/bootstrap-mgr.keyring" || true
    sleep 2
  done
}

publish_loop &
# Python 3.6 in this image has no --directory; serve cwd instead.
(
  cd "${PUB}"
  exec python3 -m http.server "${PORT}" --bind 0.0.0.0
) &

exec /opt/ceph-container/bin/entrypoint.sh mon
