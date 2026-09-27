#!/bin/bash
# One storage host: one BlueStore OSD on a ramdisk-backed loop device.
# Disk image, OSD data, and keys live on tmpfs. Compose down = gone.
set -euo pipefail

# shellcheck source=/dev/null
source /opt/coldceph/fetch-cluster.sh

HOST="$(hostname -s)"
IMG="/mnt/ramdisk/osd.img"
SIZE_MB="${OSD_SIZE_MB:-1024}"

fetch_cluster osd
until ceph -s >/dev/null 2>&1; do
  sleep 2
done

purge_this_host() {
  if ceph osd crush tree 2>/dev/null | grep -qw "${HOST}"; then
    echo "${HOST}: dropping OSDs left in CRUSH after this container was recreated"
    while read -r osd_id; do
      [[ -z "${osd_id}" ]] && continue
      ceph osd purge "${osd_id}" --yes-i-really-mean-it || true
    done < <(ceph osd ls-tree "${HOST}" 2>/dev/null || true)
    ceph osd crush rm "${HOST}" || true
  fi
}

bootstrap_osd() {
  purge_this_host
  losetup -D >/dev/null 2>&1 || true
  rm -rf /var/lib/ceph/osd/*
  rm -f "${IMG}"
  mkdir -p /mnt/ramdisk /var/lib/ceph/osd
  echo "${HOST}: creating ${SIZE_MB}MiB ramdisk image"
  if ! fallocate -l "${SIZE_MB}M" "${IMG}" 2>/dev/null; then
    dd if=/dev/zero of="${IMG}" bs=1M count="${SIZE_MB}" status=none
  fi
  LOOP="$(losetup --find --show "${IMG}")" || return 1
  echo "${HOST}: OSD device ${LOOP}"
  echo "${HOST}: ceph-volume raw prepare"
  ceph-volume raw prepare --bluestore --data "${LOOP}" || return 1
  echo "${HOST}: ceph-volume raw activate"
  ceph-volume raw activate --no-systemd --device "${LOOP}" || return 1
  OSD_DIR="$(find /var/lib/ceph/osd -mindepth 1 -maxdepth 1 -type d | head -n 1)"
  [[ -n "${OSD_DIR}" && -f "${OSD_DIR}/whoami" ]]
}

attempt=1
until bootstrap_osd; do
  if [[ "${attempt}" -ge 5 ]]; then
    echo "${HOST}: OSD bootstrap failed after ${attempt} attempts" >&2
    exit 1
  fi
  echo "${HOST}: bootstrap failed, retry ${attempt}/5"
  attempt=$((attempt + 1))
  sleep $((attempt * 4))
done

OSD_DIR="$(find /var/lib/ceph/osd -mindepth 1 -maxdepth 1 -type d | head -n 1)"
OSD_ID="$(cat "${OSD_DIR}/whoami")"
LOOP="$(losetup -j "${IMG}" | cut -d: -f1)"
chown -R ceph:ceph "${OSD_DIR}" ${LOOP:+"${LOOP}"} || true
echo "${HOST}: starting osd.${OSD_ID}"
exec ceph-osd -f --cluster ceph --id "${OSD_ID}" --setuser ceph --setgroup ceph
