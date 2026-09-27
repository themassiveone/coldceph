#!/bin/bash
# Gateway host: wait until every storage node has an OSD up, then run RGW.
# Creates the local demo S3 user once the daemon is listening.
# Must not exec the ceph-container entrypoint with extra children: it treats
# any SIGCHLD as "radosgw died" and tears the container down.
set -euo pipefail

# shellcheck source=/dev/null
source /opt/coldceph/fetch-cluster.sh
fetch_cluster rgw

osds_up() {
  python3 - <<'PY'
import json, subprocess, sys
try:
    raw = subprocess.check_output(
        ["ceph", "--format", "json", "osd", "stat"],
        stderr=subprocess.DEVNULL,
    )
    data = json.loads(raw)
    sys.exit(0 if int(data.get("num_up_osds", 0)) >= 3 else 1)
except Exception:
    sys.exit(1)
PY
}

echo "rgw: waiting for three OSDs"
until ceph -s >/dev/null 2>&1; do
  sleep 2
done
until osds_up; do
  sleep 2
done

UID_NAME="${CEPH_DEMO_UID:-coldceph}"
ACCESS="${CEPH_DEMO_ACCESS_KEY:-coldceph}"
SECRET="${CEPH_DEMO_SECRET_KEY:-coldcephsecret}"

ensure_s3_user() {
  echo "rgw: waiting for radosgw on :7480"
  until curl -sS -o /dev/null --max-time 3 http://127.0.0.1:7480/; do
    sleep 2
  done
  if radosgw-admin user info --uid="${UID_NAME}" >/dev/null 2>&1; then
    echo "rgw: s3 user ${UID_NAME} already exists"
    return 0
  fi
  echo "rgw: creating s3 user ${UID_NAME}"
  radosgw-admin user create \
    --uid="${UID_NAME}" \
    --display-name="${UID_NAME}" \
    --access-key="${ACCESS}" \
    --secret="${SECRET}" >/dev/null
  echo "rgw: s3 user ready"
}

echo "rgw: starting radosgw"
/opt/ceph-container/bin/entrypoint.sh rgw &
rgw_pid=$!
trap 'kill -TERM "${rgw_pid}" 2>/dev/null || true; wait "${rgw_pid}" || true' TERM INT
ensure_s3_user
wait "${rgw_pid}"
