#!/bin/bash
set -euo pipefail

# demo.sh skips RGW user/bucket bootstrap only when this sentinel exists. The
# file is written under /opt/ceph-container/tmp (image layer), not /var/lib/ceph,
# so a volume restart retries `user create`, hits "user exists", and `set -e`
# exits PID 1. Seed the sentinel whenever the persisted demo cluster is present.
if [[ -f /var/lib/ceph/I_AM_A_DEMO || -f /etc/ceph/I_AM_A_DEMO ]]; then
  mkdir -p /opt/ceph-container/tmp
  printf 'Access key: %s\nSecret key: %s\n' \
    "${CEPH_DEMO_ACCESS_KEY:-coldceph}" \
    "${CEPH_DEMO_SECRET_KEY:-coldcephsecret}" \
    > /opt/ceph-container/tmp/ceph-demo-user
fi

exec /opt/ceph-container/bin/entrypoint.sh demo
