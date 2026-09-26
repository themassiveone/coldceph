# ColdCeph

Cold-storage orchestration layer for Ceph. Keep the Ceph control plane available while HDD-backed
OSDs stop and enter hardware standby. An S3 gateway wakes the storage plane before forwarding
requests to RGW.

## What this is

- **Control** (`coldceph-control`): operator UI, `/v1` management API, and the S3 listener.
- **Agent** (`coldceph-agent`): per-node OSD process and disk power control.
- **Ceph** stays responsible for objects, replication, CRUSH, scrubbing, and recovery.

Full specification: [`docs/spec/cold-storage.md`](docs/spec/cold-storage.md).

## Operator ports

| Listener | Default | Purpose |
|----------|---------|---------|
| Operator MVC + `/v1` | `8080` (or `WEB_PORT`) | Dashboard, login, management API |
| S3 | `7480` (or `S3_PORT`) | Client object endpoint; proxies to RGW when READY |

Do not send S3 traffic to the operator port.

## Install (Debian/Ubuntu)

Packages are published as GitHub Release assets:

- `coldceph-control_*.deb`
- `coldceph-agent_*.deb`

```bash
sudo apt install ./coldceph-control_*.deb
sudo apt install ./coldceph-agent_*.deb
```

Edit `/etc/coldceph/control.yaml` and `/etc/coldceph/agent.yaml`, then:

```bash
sudo systemctl enable --now coldceph-control
sudo systemctl enable --now coldceph-agent
```

State lives on a local SSD path (`/var/lib/coldceph`), never on the HDDs being put to sleep.

## Development

```bash
dotnet test coldceph.slnx
dotnet run --project ColdCeph.Control
```

Under Agent-Up, Control consumes `WEB_PORT` from `agent-up.json`.
