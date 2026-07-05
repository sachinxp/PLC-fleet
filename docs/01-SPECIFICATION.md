# Multi-Brand PLC Simulator — Functional Specification

**Project:** Virtual Operational Factory — PLC Simulator
**Host platform:** Windows 11 (single machine, LAN-reachable)
**Document status:** Draft for review · v0.1
**Companion documents:** [02-ARCHITECTURE.md](02-ARCHITECTURE.md) · [03-TECHNICAL-SPEC.md](03-TECHNICAL-SPEC.md) · [04-IMPLEMENTATION-PLAN.md](04-IMPLEMENTATION-PLAN.md)

---

## 1. Purpose

A Windows application that simulates a fleet of PLCs from multiple vendors, each with its
own IP address on the real LAN, speaking the **genuine wire protocol** of its brand. Any
industrial client — SCADA, data logger, engineering tool, or packet analyzer — should not be
able to tell the difference from a real device at the protocol level.

**Primary validation target:** the six drivers of **NexEdgeLogger**
(`s7`, `rockwell`, `modbus-tcp`, `melsec`, `ads`, `opcua`) must be able to connect, browse
(where the protocol supports it), subscribe, read, and write against the simulator exactly
as they would against real hardware.

## 2. Goals

| # | Goal |
|---|------|
| G1 | Simulate PLCs of 6 protocol families: Siemens S7, Rockwell EtherNet/IP, Modbus TCP, Mitsubishi MELSEC, Beckhoff ADS, OPC UA |
| G2 | Each simulated PLC has a **distinct IPv4 address** on the physical network, reachable from other machines |
| G3 | Tags produce **configurable, deterministic signal patterns** (step, direction, low/high limit, auto-reverse, etc.) covering all common data types |
| G4 | Each brand ships with a **default template**: 5 tags of every supported data type, pre-addressed in that brand's syntax |
| G5 | Traffic is indistinguishable from a real PLC's network stack: real framing, correct identity objects, realistic timing, correct error semantics |
| G6 | Tags can be **exported/imported** — including a CSV drop-in compatible with the provided `tags_export.csv` (Kepware-style) format |
| G7 | Modern, practical web UI for configuring, running, and observing the fleet live |
| G8 | Built on proven, widely adopted open-source libraries wherever they exist |

## 3. Non-Goals (v1)

- No IEC 61131-3 logic execution (no ladder/ST program runtime — tags are signal-generator driven, not program driven).
- No distinct MAC address per PLC (all alias IPs share the physical NIC's MAC; see §8.4).
- No simulation of PROFINET IO, EtherNet/IP implicit (Class 1) cyclic I/O, or EtherCAT — acyclic/messaging channels only (the channels data loggers and SCADA use).
- No cloud/multi-host deployment; single Windows machine.

## 4. Supported PLC Brands and Personalities

Each simulated PLC instance is created from a **brand + personality (model)**. The
personality controls default ports, identity/nameplate data, addressing syntax, supported
data types, and default rack/slot or equivalent.

| Brand | Protocol (wire) | Port(s) | Personalities (v1) | Identity presented |
|---|---|---|---|---|
| Siemens | S7comm (ISO-on-TCP, RFC1006/COTP) | TCP 102 | S7-300 (Rack 0/Slot 2), S7-400 (0/2), S7-1200 (0/1), S7-1500 (0/1) | CPU order code, serial, firmware via SZL |
| Rockwell | EtherNet/IP + CIP (explicit messaging) | TCP 44818, UDP 44818 (ListIdentity) | ControlLogix 1756-L7x, CompactLogix 1769-L33ER; Micro800 (phase 2) | CIP Identity object: Vendor 1 (Rockwell), product name/code, serial |
| Modbus | Modbus TCP | TCP 502 | Generic device, Schneider M340-flavored nameplate | Device Identification (FC 43/14) vendor/product/version |
| Mitsubishi | MC protocol 3E (binary) / SLMP | TCP 5007 (configurable; UDP optional) | FX5U (SLMP), Q03UDE (MC 3E) | CPU model-name read command |
| Beckhoff | ADS/AMS | TCP 48898 (AMS), ADS port 851 (TC3 PLC1) / 801 (TC2) | TwinCAT 3 runtime (CX-series flavored), TwinCAT 2 runtime | ADS ReadDeviceInfo (name + version), AMS NetID |
| OPC UA | OPC UA binary (opc.tcp) | TCP 4840 | Generic server; brand-flavored namespace option (e.g. S7-1500-style layout) | Server nameplate: ApplicationUri, ProductUri, ManufacturerName |

**Fidelity notes (stated openly, carried into acceptance criteria):**

- **S7:** the simulator speaks classic S7comm (protocol ID 0x32) — the same channel Kepware,
  NexEdgeLogger-class drivers, and Snap7/S7Net clients use, including against real
  S7-1200/1500 with "PUT/GET" enabled and non-optimized DBs. It does **not** implement
  S7comm-plus (0x72, TIA-portal-only channel). The S7-1200/1500 personalities therefore
  represent a 1200/1500 with PUT/GET access enabled and absolute-addressed DBs — which is
  exactly the configuration a data logger requires from the real device too.
- **Rockwell:** ControlLogix/CompactLogix tag-based explicit messaging (Read/Write Tag
  service, tag list browse) is v1. Micro800 (different tag model) is phase 2; legacy
  MicroLogix/SLC500 (PCCC) is a stretch goal — see [04-IMPLEMENTATION-PLAN.md](04-IMPLEMENTATION-PLAN.md).
- **MELSEC:** MC 3E binary frames + SLMP; ASCII frame variant is phase 2.
- **ADS:** full symbolic access (browse, handles by name, notifications) — the highest-effort
  protocol in the set; see risk register.

## 5. PLC Instance Model

Every simulated PLC is an independent instance with:

| Property group | Contents |
|---|---|
| **Identity** | Instance name, brand, personality/model, station description, order code / product name, serial number, firmware version string |
| **Network** | IPv4 address (unique per instance), netmask, port overrides, max concurrent client connections (default per brand, e.g. S7-300: 8), optional UDP enable |
| **Behavior** | Base response latency (ms) + jitter (ms), scan-cycle emulation time, connection-drop injection (off by default), error-injection rules (off by default) |
| **Tags** | The tag table (see §6) |
| **Lifecycle** | Created / configured / running / stopped; instances start/stop independently without affecting others |

Instance configurations are persisted as human-readable JSON files (one file per PLC) so
they can be versioned, diffed, copied, and edited outside the UI.

## 6. Tag Model

### 6.1 Common tag definition

Every tag, regardless of brand, has:

| Field | Description |
|---|---|
| `name` | Symbolic name (also the OPC UA browse name / Logix tag name / ADS symbol name where applicable) |
| `address` | Brand-native address string (`DB10.DBW100`, `%MW100`, `40001`, `D100`, `MAIN.fTemp`, `N7:0`, `ns=2;s=...`) — validated per brand syntax |
| `dataType` | One of the common type system (§6.2), mapped to the brand's native type |
| `access` | `ReadOnly` / `ReadWrite` (client-facing) |
| `description` | Free text |
| `engUnit` | Engineering unit string (shown in UI, exposed on OPC UA as EUInformation) |
| `simulation` | Signal profile + parameters (§6.3) |
| `enabled` | Tag served / hidden |

### 6.2 Common type system → brand mapping

| Common type | S7 | Rockwell | Modbus | MELSEC | ADS (IEC) | OPC UA |
|---|---|---|---|---|---|---|
| Bool | BOOL (DBX x.y, M x.y) | BOOL | Coil / discrete input / register bit | M, X, Y, B (bit devices) | BOOL | Boolean |
| Byte / SInt | BYTE / SINT | SINT | — (packed in register) | — | BYTE / SINT | Byte / SByte |
| Int16 | INT (DBW) | INT | 1 holding register | D, W, R (1 word) | INT | Int16 |
| UInt16 | WORD | — (INT reinterpreted) | 1 register unsigned | 1 word unsigned | WORD / UINT | UInt16 |
| Int32 | DINT (DBD) | DINT | 2 registers (word order configurable) | 2 words | DINT | Int32 |
| UInt32 | DWORD | — | 2 registers | 2 words | UDINT / DWORD | UInt32 |
| Int64 | LINT (S7-1500) | LINT | 4 registers | 4 words | LINT | Int64 |
| Float32 | REAL | REAL | 2 registers (word order configurable) | 2 words | REAL | Float |
| Float64 | LREAL (S7-1500) | LREAL | 4 registers | 4 words | LREAL | Double |
| String | STRING (S7String: max+len header) | STRING (Logix 82-char) | register block (raw ASCII) | word block | STRING(n) | String |
| Time/Duration | TIME (ms as DINT), S5TIME (300/400) | DINT ms convention | — | — | TIME | Duration / Int64 |
| DateTime | DATE_AND_TIME (300/400), DTL (1200/1500) | LINT µs (Logix WallClock convention) | — | — | DT | DateTime |

Brand templates only include the types that brand genuinely supports (no fake Modbus
DateTime, etc.). Modbus multi-register types carry a **word-order** attribute
(`ABCD`, `CDAB`, `BADC`, `DCBA`) so NexEdgeLogger's word-order handling can be tested
against every permutation.

### 6.3 Simulation profiles (signal generators)

Each tag has one profile; all parameters are editable live from the UI while the PLC runs.

| Profile | Parameters | Applies to | Behavior |
|---|---|---|---|
| **Static** | `value` | all | Constant until manually changed |
| **Step** | `step`, `direction` (up/down), `lowLimit`, `highLimit`, `updateMs`, `atLimit` = `autoReverse` \| `wrap` \| `clamp` | numeric | Adds ±`step` every `updateMs`; on hitting a limit: reverse direction, wrap to the other limit, or hold |
| **Ramp** | `lowLimit`, `highLimit`, `periodMs`, `updateMs` | numeric | Sawtooth from low to high over `period` |
| **Sine / Cosine** | `lowLimit`, `highLimit`, `periodMs`, `phaseDeg`, `updateMs`, `noise%` | float (int rounded) | Smooth analog-style signal, optional Gaussian noise |
| **Square / Triangle** | `lowLimit`, `highLimit`, `periodMs`, `duty%` | numeric | Classic test waveforms |
| **Random** | `lowLimit`, `highLimit`, `distribution` (uniform/normal), `updateMs` | numeric | New random value each interval |
| **Toggle** | `intervalMs` | bool | TRUE/FALSE alternation |
| **Pulse** | `periodMs`, `duty%` | bool | Asymmetric on/off (e.g. 10 s period, 20 % on) |
| **Counter** | `step`, `rolloverAt`, `updateMs` | int | Monotonic production-counter style, rolls over like a real totalizer |
| **Clock** | `format` | time/datetime/string | Emits current wall-clock (tests timestamp handling end-to-end) |
| **TextCycle** | `values[]`, `intervalMs` | string | Cycles a list (e.g. `RUNNING`, `IDLE`, `FAULT`) |
| **Echo** | — | all | Value is whatever a client last wrote (write-back testing); initial value configurable |

**Client writes:** if a client writes a tag whose profile is not `Echo`/`Static`, the write
is accepted on the wire (correct protocol ack), logged in the traffic monitor, and by
configurable policy either (a) *overridden on next generator tick* (default) or (b) *pauses
the generator* and holds the written value until resumed from the UI. This makes write-path
testing observable and deterministic.

### 6.4 Determinism

All generators are driven by a per-PLC monotonic scheduler; a given profile with the same
parameters produces the same waveform shape every run (random profiles accept an optional
seed). This makes NexEdgeLogger regression tests repeatable.

## 7. Default Templates

Selecting a brand + personality when creating a PLC pre-loads its default template:
**5 tags of every data type the brand supports** (per G4), named and addressed idiomatically,
with a spread of profiles so the default fleet is immediately "alive":

- Tag 1 — `Step` with auto-reverse (the canonical requested behavior)
- Tag 2 — `Sine` (analog realism)
- Tag 3 — `Random`
- Tag 4 — `Static`
- Tag 5 — `Echo` (ReadWrite, for write testing)

Examples of template addressing:

| Brand | Example default tags |
|---|---|
| S7 | `DB1.DBX0.0` (Bool), `DB1.DBW2` (Int16), `DB1.DBD4` (Float32), `MW20`, `IW0`, `QW0`, `DB2.DBB0 STRING[32]` |
| Rockwell | Controller tags `Sim_Bool_01…05`, `Sim_Dint_01…05`, `Sim_Real_01…05`, `Sim_String_01…05`, plus one array tag per type (`Sim_Real_Arr[10]`) |
| Modbus | Coils 1–5, discrete inputs 10001–10005, input registers 30001+, holding registers 40001+ with Int16/Int32/Float32/Float64 blocks in all four word orders |
| MELSEC | `M0–M4`, `X0–X4`, `Y0–Y4`, `D100+`, `W0+`, `R0+` word/dword/float layouts |
| ADS | `MAIN.bPump1…5`, `MAIN.nCount1…5`, `MAIN.fTemp1…5`, `MAIN.sStatus1…5`, `GVL.stMachine.*` (nested struct to exercise symbol browsing) |
| OPC UA | `Objects/Simulator/<PLCName>/…` one folder per type, 5 variables each, incl. arrays and DateTime |

Templates are themselves editable JSON files (`templates/<brand>.json`) — users can define
their own and set a different default.

## 8. Networking Requirements

### 8.1 Per-PLC IP addresses (primary mode)

- Each PLC instance is assigned a unique IPv4 address added as a **secondary address
  (alias)** on a selected physical NIC, with `SkipAsSource=true` so host outbound traffic is
  unaffected.
- The application manages alias lifecycle (add on PLC start, remove on stop/delete —
  removal-on-stop configurable) and creates the required Windows Firewall inbound rules.
- Operations requiring elevation are isolated in a small elevated helper (see architecture);
  the UI clearly reports when elevation is missing and what will not work.

### 8.2 Loopback mode (fallback)

For single-machine testing without touching the NIC: instances bind `127.0.0.2`,
`127.0.0.3`, … Only local clients can connect; the UI labels the mode clearly.

### 8.3 Port policy

Standard IANA/vendor default ports per protocol (102, 502, 4840, 5007, 44818, 48898) —
because every PLC has its own IP, **no port remapping is ever needed**, which is itself part
of the realism requirement. Ports remain overridable per instance for edge-case testing.

### 8.4 Stated limitation — MAC addresses

All alias IPs answer ARP with the physical NIC's MAC. Clients identifying devices purely by
IP (the norm for the six target protocols) are unaffected. If per-device MACs are ever
needed (e.g. testing a discovery tool that fingerprints by OUI), the documented path is
Hyper-V VMs or dedicated hardware — out of scope for v1.

## 9. Realism Requirements ("traffic as real as an actual PLC")

| # | Requirement |
|---|---|
| R1 | Protocol framing produced by real/proven protocol stacks — a Wireshark capture must dissect cleanly with the standard dissectors (s7comm, cip/enip, modbus, mc/slmp, ams, opcua) with zero malformed packets |
| R2 | Correct identity/nameplate responses per brand (SZL, CIP Identity, Modbus Device ID, MELSEC model name, ADS DeviceInfo, UA nameplate) matching the chosen personality |
| R3 | Configurable base latency + jitter per instance to emulate PLC scan-cycle response times (e.g. S7-300 ~2–10 ms; defaults per personality) |
| R4 | Per-instance concurrent-connection limits with correct on-the-wire refusal behavior when exceeded |
| R5 | Correct protocol error semantics for invalid requests: Modbus exception codes (ILLEGAL DATA ADDRESS…), CIP general status codes, S7 return codes, ADS error codes (e.g. 0x710 symbol not found), UA StatusCodes |
| R6 | TCP behavior from the OS's real stack (handshakes, keepalive, RST on refused connections) — inherent to binding real sockets on real IPs |
| R7 | Optional fault injection (off by default): delayed responses, dropped connections, error responses at a configured rate — for testing NexEdgeLogger's retry/reconnect logic |

## 10. Export / Import

| Format | Direction | Notes |
|---|---|---|
| **CSV (Kepware-style)** | Export + Import | Exactly the provided format: `Name,Address,DataType,ScanRate,Protocol,Description,ClientAccess,Enabled`. Export per PLC or whole fleet (Protocol column disambiguates). DataType/address rendered in the brand's Kepware vocabulary so the file imports into KEPServerEX/NexEdgeLogger without editing |
| **JSON** | Export + Import | Full fidelity: everything in §5/§6 including simulation profiles — the round-trip/backup format |
| **XLSX** | Export | One sheet per PLC, formatted for human review |

`ScanRate` in CSV maps from the tag's `updateMs`; `ClientAccess` from `access`; `Protocol`
from the brand's driver id (`s7`, `rockwell`, `modbus-tcp`, `melsec`, `ads`, `opcua` — the
NexEdgeLogger driver ids).

## 11. User Interface Requirements

Web UI served locally by the application (opens in the default browser; usable from another
machine on the LAN if permitted).

| Area | Contents |
|---|---|
| **Fleet dashboard** | Card/grid of all PLCs: brand badge, personality, IP, running state (start/stop), active client connections, requests/s, error count. Fleet-wide start/stop |
| **PLC detail** | Live tag grid (virtualized — smooth at 1000+ tags): name, address, type, live value, profile summary, sparkline; inline edit of simulation parameters while running; add/remove/clone tags |
| **Tag editor** | Full profile editor with live preview chart of the configured waveform before applying |
| **New PLC wizard** | Brand → personality → IP assignment (suggests next free) → template selection → review |
| **Traffic monitor** | Per-PLC connection list (client IP, connect time, request count), rolling protocol event log (read/write/browse/subscribe operations with addresses and outcomes), per-protocol counters |
| **Trends** | Selectable tags on a live strip chart (verify waveforms visually; compare against what NexEdgeLogger records) |
| **Network settings** | NIC selection, IP pool/range for auto-assignment, alias & firewall status with health checks, loopback-mode toggle |
| **Export/Import** | Format pickers, per-PLC or fleet scope, drag-drop import with validation preview |
| **System** | Elevation status, port-conflict checks (incl. Windows excluded-port-range detection), logs, config folder shortcut |

UI quality bar: modern component library with strong adoption, dark/light themes, responsive
down to laptop widths, no page reloads (live data over WebSocket), every destructive action
confirmed.

## 12. Non-Functional Requirements

| Category | Requirement |
|---|---|
| Scale | ≥ 20 concurrent PLC instances; ≥ 1 000 tags per instance; tag update intervals down to 100 ms sustained (50 ms best-effort); ≥ 10 concurrent client connections per instance |
| Performance | Protocol response overhead added by simulation < 5 ms beyond configured latency at nominal load |
| Reliability | One PLC instance crashing must not affect others (process isolation); supervisor auto-restarts failed instances (configurable) |
| Persistence | All configuration on disk as JSON; atomic writes; app restart restores the fleet (auto-start previously running instances configurable) |
| Install | Single installer or self-contained folder; no external DB server; runs on stock Windows 11 Pro |
| Licensing | All runtime dependencies under permissive/weak-copyleft licenses compatible with commercial internal use (noted per library in the technical spec) |
| Observability | Structured application logs; per-protocol debug logging toggleable at runtime |

## 13. Acceptance Criteria (v1)

For **each** of the six protocols:

1. NexEdgeLogger's driver connects to a simulated PLC on its own LAN IP from a **different
   machine**, browses (where the driver supports it: ADS symbols, OPC UA nodes, Rockwell tag
   list), subscribes/polls all 5×N template tags, and logs correct values for ≥ 30 min
   without protocol errors.
2. Writes from NexEdgeLogger to `Echo` tags round-trip correctly for every data type.
3. A second, independent reference client (per-protocol list in the technical spec, e.g.
   UaExpert, pycomm3, pymcprotocol, Kepware demo) validates the same instance concurrently.
4. Wireshark capture of a 5-minute session dissects with 0 malformed packets.
5. Step/auto-reverse behavior recorded by NexEdgeLogger matches the configured waveform
   exactly (limits, reversal points, step size, timing within ±1 update interval).
6. Fleet test: 6 PLCs (one per brand) running simultaneously on 6 IPs, all polled
   concurrently, for 8 hours with zero simulator crashes or leaks.
