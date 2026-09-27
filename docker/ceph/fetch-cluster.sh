#!/bin/bash
# Copy cluster files from the monitor's in-memory HTTP publish dir.
# Used by other node entrypoints; not executed on its own.

fetch_url() {
  local url="$1"
  local dest="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -fsS "${url}" -o "${dest}" 2>/dev/null
  else
    python3 - "${url}" "${dest}" <<'PY'
import sys, urllib.request
urllib.request.urlretrieve(sys.argv[1], sys.argv[2])
PY
  fi
}

fetch_cluster() {
  local mon_ip="${MON_IP:-172.28.90.10}"
  local port="${CEPH_CONFIG_PORT:-3799}"
  local base="http://${mon_ip}:${port}"
  local extra="${1:-}"

  mkdir -p /etc/ceph \
    /var/lib/ceph/bootstrap-osd \
    /var/lib/ceph/bootstrap-rgw \
    /var/lib/ceph/bootstrap-mgr

  echo "waiting for cluster config at ${base}"
  until fetch_url "${base}/ceph.conf" /etc/ceph/ceph.conf \
    && fetch_url "${base}/ceph.client.admin.keyring" /etc/ceph/ceph.client.admin.keyring \
    && [[ -s /etc/ceph/ceph.conf && -s /etc/ceph/ceph.client.admin.keyring ]]; do
    sleep 2
  done

  case "${extra}" in
    osd)
      until fetch_url "${base}/bootstrap-osd.keyring" /var/lib/ceph/bootstrap-osd/ceph.keyring \
        && [[ -s /var/lib/ceph/bootstrap-osd/ceph.keyring ]]; do
        sleep 2
      done
      ;;
    mgr)
      until fetch_url "${base}/bootstrap-mgr.keyring" /var/lib/ceph/bootstrap-mgr/ceph.keyring \
        && [[ -s /var/lib/ceph/bootstrap-mgr/ceph.keyring ]]; do
        sleep 2
      done
      ;;
    rgw)
      until fetch_url "${base}/bootstrap-rgw.keyring" /var/lib/ceph/bootstrap-rgw/ceph.keyring \
        && [[ -s /var/lib/ceph/bootstrap-rgw/ceph.keyring ]]; do
        sleep 2
      done
      ;;
  esac
}
