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
tiny ramdisk. Bring that stack up with `cc-debug`, which also starts Control if `/health` is not
already ready.

Defaults are baked in. Copy `.env.example` only if you want to change them.

```bash
cp .env.example .env          # optional
./cc-debug up
docker/ceph/ceph osd tree     # three hosts, one OSD each
```

First boot takes a couple of minutes (OSD prepare + peering with size 3). The cluster is
ephemeral: no named volumes, Ceph state is tmpfs. `./cc-debug down` wipes it;
`./cc-debug up` bootstraps a new cluster. Compose pins `MON_IP` (`172.28.90.10`). Ready is
`ceph -s` healthy, three OSDs up, and RGW on host 7481.

`./cc-debug up` starts Ceph and Control if `/health` is not already ready. Start the `Node-a`,
`Node-b`, and `Node-c` launch profiles. Open **Hosts** and **Allow** `node-a`, `node-b`, and
`node-c`. Allowed Nodes then push OSD and disk inventory; reload OSDs and Physical drives.
Open **http://127.0.0.1:8080** or **http://localhost:8080** — unauthenticated
visits go to `/auth/login` (password `changeme`). `/health` is anonymous JSON if you want a bind
check without the UI.

| What | Default | Override |
|------|---------|----------|
| Operator UI | http://127.0.0.1:8080 | `WEB_PORT` |
| Operator login password | `changeme` | `COLDCEPH_OPERATOR_PASSWORD` |
| ColdCeph S3 listener | http://127.0.0.1:7480 | `S3_PORT` |
| RGW (direct) | http://127.0.0.1:7481 | `RGW_HOST_PORT` / `COLDCEPH_RGW` |
| S3 access key | `coldceph` | `CEPH_DEMO_ACCESS_KEY` |
| S3 secret key | `coldcephsecret` | `CEPH_DEMO_SECRET_KEY` |
| Demo user / bucket | `coldceph` / `cold` | `CEPH_DEMO_UID` / `CEPH_DEMO_BUCKET` |
| Node a / b / c | http://127.0.0.1:7081–7083 | `NODE_PORT` on the Node-a/b/c profiles |
| Node joins Control at | http://127.0.0.1:8080 | `COLDCEPH_CONTROL_ENDPOINT` / `WEB_PORT` |
| Node token (Control→Node only) | `changeme` | `COLDCEPH_NODE_TOKEN` |
| Node host ids | `node-a` / `node-b` / `node-c` | `COLDCEPH_HOST_ID` |
| Control Ceph CLI | `docker exec coldceph-mon ceph` | `COLDCEPH_CEPH_CONTAINER` |
| Ceph image | `quay.io/ceph/daemon:v7.0.3-stable-7.0-quincy-centos-stream8` | `CEPH_IMAGE` |

Path-style S3 through ColdCeph (once the plane is READY):

```bash
aws --endpoint-url http://127.0.0.1:7480 s3 ls \
  --access-key coldceph --secret-key coldcephsecret
```

Run `Node-a` / `Node-b` / `Node-c` from the IDE. Each process docker-execs into the matching OSD
container (`coldceph-node-a` / `coldceph-node-b` / `coldceph-node-c`) and joins Control on
`127.0.0.1:8080`. Compose is Ceph only; do not put ColdCeph.Node in `compose.yaml`.

Talk to Ceph from a shell without installing `ceph-common` (this helper is not what Control
runs):

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

Local compose + operator HTML. Do not `docker compose up` or walk the UI by hand:

```bash
./cc-debug up
./cc-debug status
./cc-debug allow
./cc-debug
./cc-debug screenshot /hosts
./cc-debug pages
./cc-debug down
```

Screenshots write under `.git/coldceph/debug/` (`WEB_PORT` /
`COLDCEPH_OPERATOR_URL`).

CI runs both jobs. Dependabot opens weekly PRs for NuGet, npm, and GitHub Actions. The E2E job
does not start `compose.yaml`; Testcontainers owns the cluster. `compose.yaml` is only for local
IDE getting-started against a long-lived multi-host cluster.

Under Agent-Up, Control uses `--no-launch-profile` and consumes `WEB_PORT` from `agent-up.json`. Add the same `COLDCEPH_*` variables there if that Control process should also target compose Ceph.
