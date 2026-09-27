# ColdCeph

Cold-storage orchestration layer for Ceph. Keep the Ceph control plane available while HDD-backed
OSDs stop and enter hardware standby. An S3 gateway wakes the storage plane before forwarding
requests to RGW.

## What this is

- **Control** (`coldceph-control`): operator UI, `/v1` management API, and the S3 listener.
- **Node** (`coldceph-node`): per-node OSD process and disk power control.
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
- `coldceph-node_*.deb`

```bash
sudo apt install ./coldceph-control_*.deb
sudo apt install ./coldceph-node_*.deb
```

Edit `/etc/coldceph/control.yaml` and `/etc/coldceph/node.yaml`, then:

```bash
sudo systemctl enable --now coldceph-control
sudo systemctl enable --now coldceph-node
```

State lives on a local SSD path (`/var/lib/coldceph`), never on the HDDs being put to sleep.

## Getting started (local Ceph)

This repo ships a **multi-host** Ceph: one container per node (`mon`, `mgr`, three storage
nodes, `rgw`). Each storage node (`node-a` / `node-b` / `node-c`) runs one BlueStore OSD on a
tiny ramdisk. Start that, then run Control and Node from the IDE.

Defaults are baked in. Copy `.env.example` only if you want to change them.

```bash
cp .env.example .env          # optional
docker compose up -d --wait
docker/ceph/ceph osd tree     # three hosts, one OSD each
```

First boot takes a couple of minutes (OSD prepare + peering with size 3). The cluster is
ephemeral: no named volumes, Ceph state is tmpfs. `docker compose down` wipes it;
`docker compose up` bootstraps a new cluster. Compose pins `MON_IP` (`172.28.90.10`). Ready is
`ceph -s` healthy, three OSDs up, and RGW on host 7481.

Then start **ColdCeph.Control**, then **ColdCeph.Node**. Control does not need a Node. The Node
asks to join; open **Hosts** and click **Allow**. Open **http://127.0.0.1:8080** or
**http://localhost:8080** — unauthenticated visits go to `/auth/login` (password `changeme`).
`/health` is anonymous JSON if you want a bind check without the UI.

| What | Default | Override |
|------|---------|----------|
| Operator UI | http://127.0.0.1:8080 | `WEB_PORT` |
| Operator login password | `changeme` | `COLDCEPH_OPERATOR_PASSWORD` |
| ColdCeph S3 listener | http://127.0.0.1:7480 | `S3_PORT` |
| RGW (direct) | http://127.0.0.1:7481 | `RGW_HOST_PORT` / `COLDCEPH_RGW` |
| S3 access key | `coldceph` | `CEPH_DEMO_ACCESS_KEY` |
| S3 secret key | `coldcephsecret` | `CEPH_DEMO_SECRET_KEY` |
| Demo user / bucket | `coldceph` / `cold` | `CEPH_DEMO_UID` / `CEPH_DEMO_BUCKET` |
| Node | http://127.0.0.1:7080 | `NODE_PORT` / `COLDCEPH_ADVERTISE_URL` |
| Node joins Control at | http://127.0.0.1:8080 | `COLDCEPH_CONTROL_ENDPOINT` |
| Node token (Control→Node only) | `changeme` | `COLDCEPH_NODE_TOKEN` |
| Node host id | `dev` | `COLDCEPH_HOST_ID` / `COLDCEPH_NODE_HOST_ID` |
| Ceph CLI | `docker/ceph/ceph` | `COLDCEPH_CEPH_BINARY` |
| Ceph image | `quay.io/ceph/daemon:v7.0.3-stable-7.0-quincy-centos-stream8` | `CEPH_IMAGE` |

Path-style S3 through ColdCeph (once the plane is READY):

```bash
aws --endpoint-url http://127.0.0.1:7480 s3 ls \
  --access-key coldceph --secret-key coldcephsecret
```

The compose OSDs live inside the three storage containers. A ColdCeph.Node you launch from the
IDE does not systemd-manage those OSDs; it is still the node endpoint Control enrolls on `/hosts`.

Talk to Ceph without installing `ceph-common`:

```bash
docker/ceph/ceph status --format json
```

## Tests

Unit and architecture tests (no Docker):

```bash
dotnet test coldceph.slnx --filter "FullyQualifiedName!~ColdCeph.E2E.Tests"
```

E2E starts a Testcontainers Ceph demo once for the test project (HiveShard-style assembly
`[SetUpFixture]`), then reuses Control + Node across Xcepto journeys (fluent adapters,
3–5 steps each). Do not `docker compose up` first — the suite hosts Ceph:

```bash
dotnet test coldceph.slnx --filter "FullyQualifiedName~ColdCeph.E2E.Tests"
```

Inspect operator HTML without walking the UI. `./cc-debug` signs in and screenshots every
operator page from a running Control (`WEB_PORT` / `COLDCEPH_OPERATOR_URL`):

```bash
./cc-debug
./cc-debug screenshot /hosts
./cc-debug pages
```

CI runs both jobs. The E2E job does not start `compose.yaml`; Testcontainers owns the cluster.
`compose.yaml` is only for local IDE getting-started against a long-lived multi-host cluster.

Under Agent-Up, Control uses `--no-launch-profile` and consumes `WEB_PORT` from `agent-up.json`. Add the same `COLDCEPH_*` variables there if that Control process should also target compose Ceph.
