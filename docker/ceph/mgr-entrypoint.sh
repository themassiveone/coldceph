#!/bin/bash
# Manager host: pull conf from the monitor, then run ceph-mgr.
set -euo pipefail
# shellcheck source=/dev/null
source /opt/coldceph/fetch-cluster.sh
fetch_cluster mgr
exec /opt/ceph-container/bin/entrypoint.sh mgr
