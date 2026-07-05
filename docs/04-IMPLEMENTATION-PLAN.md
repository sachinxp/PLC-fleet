# Multi-Brand PLC Simulator — Implementation Plan

**Project:** Virtual Operational Factory — PLC Simulator
**Document status:** Draft for review · v0.1
**Supersedes:** 01-SPECIFICATION.md

---

## Overview

This plan decomposes the specification into **8 phases** with **~120 granular tasks**. Each
phase produces a tangible, testable milestone. The plan accounts for dependencies, parallel
work streams, risk areas, and verification against every acceptance criterion.

**Total estimated effort:** ~1 800–2 400 person-hours (11–15 person-months for a single
full-time developer; 4–6 months for a team of 3).

---

## Phase Dependency Graph

```
Phase 1: Foundation ──────► Phase 2: Core Protocols ──────► Phase 4: Tag & Signals
       │                                                           │
       ▼                                                           ▼
Phase 3: Remaining Protocols ──────────────────────────────► Phase 5: Export/Import
                                                                    │
                                                                    ▼
                                                            Phase 6: UI Polish & Realism
                                                                    │
                                                                    ▼
                                                            Phase 7: Test & Acceptance
                                                                    │
                                                                    ▼
                                                            Phase 8: Package & Ship
```

Phases 1–2–4 form the critical path. Phases 3 (remaining protocols) can be parallelized:
all 4 remaining protocols can be implemented simultaneously by different developers after
Phase 1 is stable.

---

## Phase 1: Foundation (Weeks 1–3)

**Goal:** Skeleton application that compiles, runs, shows a web UI, can spawn/stop a
no-op worker process, and manages IP aliases on the NIC.

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 1.1 | Create solution structure: `.sln`, 8 projects, folder layout per §16 of ARCHITECTURE.md | 4 | Solution builds `dotnet build` clean | — |
| 1.2 | Define `PLC.Shared` models: `PlcInstance`, `TagDefinition`, `SimulationProfile`, enums `PlcState`, `TagAccess`, `ProfileType`, `Brand`, `Personality` | 8 | Domain model compiles | 1.1 |
| 1.3 | Define IPC message DTOs in `PLC.Shared/Ipc/` (StartWorker, StopWorker, TagUpdate, ConfigSnapshot, StatsReport) | 4 | IPC contracts ready | 1.2 |
| 1.4 | Implement `NamedPipeServer` / `NamedPipeClient` in `PLC.Shared/Ipc/` with MessagePack framing | 12 | Bidirectional IPC works with test harness | 1.3 |
| 1.5 | Implement `PLC.Elevator`: named pipe server, `AddIpHandler` (calls `netsh interface ip add address`), `RemoveIpHandler`, `ListNicsHandler` | 16 | Elevation helper can add/remove IPs | 1.1 |
| 1.6 | Implement `IpPool` in supervisor: address tracking, conflict detection via ping + `IPGlobalProperties` | 6 | IPAM class with unit tests | 1.2 |
| 1.7 | Implement `PortConflictChecker`: enumerate active TCP listeners, check Windows excluded port ranges | 4 | Port conflict detection | 1.1 |
| 1.8 | Implement `ConfigPersistence`: atomic JSON save/load for `fleet/*.json` and `config.json`, `$schemaVersion` migration stub | 8 | Config reads/writes with test coverage | 1.2 |
| 1.9 | Implement `PlcProcessManager`: spawn `PLC.Worker.exe` with args, connect IPC pipe, detect crash, auto-restart (max 3) | 10 | Process lifecycle management | 1.4 |
| 1.10 | Implement `FleetService`: CRUD for PLC instances, start/stop lifecycle calling `PlcProcessManager` + `NetworkService` | 12 | Fleet service with unit tests | 1.5, 1.8, 1.9 |
| 1.11 | Implement `NetworkService` + `ElevatorClient`: interface to elevated helper, fallback loopback mode | 8 | Network ops with graceful degradation | 1.5, 1.6 |
| 1.12 | Create `PLC.Supervisor` ASP.NET Core project: middleware, error handling, CORS for dev | 4 | HTTP server runs on `localhost:5000` | 1.1 |
| 1.13 | Implement `FleetController` (GET/POST/PUT/DELETE `/api/plcs`) and `NetworkController` (GET `/api/network/*`) | 8 | REST API functional | 1.10, 1.11, 1.12 |
| 1.14 | Implement `SystemController`: elevation status, port conflicts, logs endpoint | 4 | System API | 1.7, 1.12 |
| 1.15 | Set up SignalR `FleetHub` for real-time PLC state + tag value pushes | 6 | Real-time updates | 1.12 |
| 1.16 | Create React SPA scaffold with Vite + TypeScript + React Router + Mantine UI | 6 | `npm run dev` shows blank app shell | — |
| 1.17 | Implement `AppShell` layout: sidebar, header, theme toggle, error boundary | 8 | Layout renders | 1.16 |
| 1.18 | Implement `FleetGrid` + `PlcCard`: display hardcoded mock PLCs with state badges | 6 | Fleet dashboard renders | 1.17 |
| 1.19 | Implement SignalR connection in React (`useSignalR` hook), connect to `FleetHub` | 4 | Real-time data in browser | 1.15, 1.18 |
| 1.20 | Implement `New PLC Wizard` UI shell: 5-step stepper (Brand → Personality → Network → Template → Review) | 16 | Wizard navigates (API calls in Phase 2) | 1.17 |
| 1.21 | Implement `Network Settings` page: NIC selector, IP pool table, loopback toggle | 8 | Network settings page | 1.11, 1.17 |
| 1.22 | Implement minimal `PLC.Worker`: connects to supervisor pipe, acknowledges config, runs empty message loop | 8 | Worker process runs and communicates | 1.4 |
| 1.23 | Integration test: supervisor + worker + elevator full lifecycle (create → start → stop → delete) | 8 | End-to-end test passes | 1.10, 1.11, 1.22 |

**Phase 1 exit criteria:**
- [ ] Application starts, web UI opens at `localhost:5000`
- [ ] Can create a PLC instance via UI → JSON persists to disk
- [ ] Can start a PLC → worker process spawns, IPC connected
- [ ] Can stop a PLC → worker exits cleanly
- [ ] IP alias is added/removed on NIC (elevated mode) or loopback mode works
- [ ] Port conflicts detected and reported in UI
- [ ] All unit tests pass (≥ 80 % coverage on foundation modules)

---

## Phase 2: Core Protocols — Modbus TCP + S7comm (Weeks 4–7)

**Goal:** Two of the six protocols fully implemented. These are chosen first because:
- Modbus is the simplest protocol → validates the protocol implementation pattern
- S7 is the highest-priority brand (most NexEdgeLogger drivers target Siemens)

### Tasks

#### Modbus TCP (2A)

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 2A.1 | Implement `PLC.Protocols.Modbus`: MBAP header parser/serializer, byte buffer | 6 | MBAP unit tests pass | 1.1 |
| 2A.2 | Implement FC 01 (Read Coils), 02 (Read Discrete Inputs), 03 (Read Holding Registers), 04 (Read Input Registers) | 10 | Read functions with test vectors | 2A.1 |
| 2A.3 | Implement FC 05 (Write Single Coil), 06 (Write Single Register), 15 (Write Multiple Coils), 16 (Write Multiple Registers) | 8 | Write functions | 2A.1 |
| 2A.4 | Implement FC 22 (Mask Write Register), 23 (Read/Write Multiple Registers) | 4 | Combined read/write | 2A.2, 2A.3 |
| 2A.5 | Implement FC 43/14 (Read Device Identification) — return personality nameplate | 4 | Device ID response | 2A.1 |
| 2A.6 | Implement `ModbusAddressMap`: address ranges → tag lookup, word-order conversion (ABCD/CDAB/BADC/DCBA) | 6 | Address resolution | 2A.1 |
| 2A.7 | Implement exception codes: 01–04, 06 for invalid requests | 3 | Error responses match spec | 2A.2 |
| 2A.8 | Implement `ModbusListener` (TCP :502): async accept, `ModbusSession` per connection, max-connection enforcement | 8 | TCP server with sessions | 2A.1–2A.7 |
| 2A.9 | Implement `TagTable` + `TagValueStore` in worker: thread-safe concurrent dictionary, read/write with `Interlocked` | 6 | Tag engine | 1.2 |
| 2A.10 | Integrate Modbus listener with tag engine: read requests resolve tags → format response | 6 | Modbus ↔ tag integration | 2A.8, 2A.9 |
| 2A.11 | Create Modbus personality defaults (Generic, M340): port 502, unit ID 1, device ID strings | 3 | Personalities defined | 2A.5 |
| 2A.12 | Modbus-specific unit tests: all FCs, all error codes, all word orders, address boundary conditions | 8 | ≥ 90 % line coverage | 2A.1–2A.11 |
| 2A.13 | Wireshark validation: capture Modbus session → verify 0 malformed packets, correct dissector parsing | 4 | Wireshark clean | 2A.12 |
| 2A.14 | Integration test: `pymodbus` / `ModbusPoll` reads/writes against simulator | 4 | Third-party client connects | 2A.12 |

#### S7comm (2B)

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 2B.1 | Implement ISO-on-TCP (TPKT) + COTP layer: CR/CC handshake, TSAP mapping per rack/slot personality | 10 | Wireshark decodes TPKT/COTP correctly | 1.1 |
| 2B.2 | Implement S7 PDU header parser/serializer: Protocol ID 0x32, PDU ref, parameter/data length, data unit | 6 | PDU format correct | 2B.1 |
| 2B.3 | Implement `S7RequestParser`: parse Read Var (0x04), Write Var (0x05), Request Die (0x1C), Read SZL (0x1D) | 12 | Request parsing | 2B.2 |
| 2B.4 | Implement `S7ResponseBuilder`: build response PDU with return codes (0xFF = success), data | 8 | Response format | 2B.2 |
| 2B.5 | Implement `S7AddressParser`: `DB10.DBW100`, `M5.3`, `I0.0`, `Q0.4`, `PE0`, `PA0`, `MW20`, etc. | 8 | Address parsing with test vectors | 1.2 |
| 2B.6 | Implement `DataView`: translate parsed S7 address → tag lookup (area + DB# + offset + size) | 6 | Address → tag resolution | 2B.5, 2A.9 |
| 2B.7 | Implement read/write multi-parameter (up to 4 parameters per PDU, variable count) | 8 | Multi-var read/write | 2B.3, 2B.4 |
| 2B.8 | Implement `SzlProvider`: respond to SZL IDs 0x0011 (order code), 0x001C (CPU features), 0x0001 (boot), 0x011C (component ID) with correct data per personality | 10 | SZL responses match real CPU | 2B.2 |
| 2B.9 | Implement SZL 0x011C serial number: return configured serial per instance | 2 | Serial number responds | 2B.8 |
| 2B.10 | Implement personality defaults: S7-300 (R0/S2), S7-400 (R0/S2), S7-1200 (R0/S1), S7-1500 (R0/S1) with unique order codes, serial formats, firmware versions | 6 | All 4 personalities distinct | 2B.8 |
| 2B.11 | Implement `S7Listener` (TCP :102): async accept, COTP handshake, session creation, max-connection enforcement | 8 | TCP server | 2B.1 |
| 2B.12 | Integrate S7 listener with tag engine + SZL provider | 4 | End-to-end read/write | 2B.7, 2B.10, 2B.11, 2A.9 |
| 2B.13 | Implement response timing: configurable latency + jitter per instance, defaults per personality | 4 | Timing realistic | 2B.12 |
| 2B.14 | S7-specific unit tests: address parser (100+ patterns), SZL responses match personality, multi-parameter edge cases | 10 | ≥ 85 % coverage | 2B.1–2B.12 |
| 2B.15 | Wireshark validation: capture S7 session → verify s7comm dissector parses cleanly, identity matches | 4 | 0 malformed packets | 2B.14 |
| 2B.16 | Integration test: Snap7 client / S7.Net reads/writes against simulator, browse SZL | 4 | Third-party client connects | 2B.14 |

#### Signal Generation Integration

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 2C.1 | Implement `IProfileGenerator` interface + base class with common parameter validation | 3 | Profile interface | 1.2 |
| 2C.2 | Implement all 13 profile generators (see §6.2 of SPEC): Static, Step, Ramp, Sine, Cosine, Square, Triangle, Random, Toggle, Pulse, Counter, Clock, TextCycle, Echo | 20 | All profiles implemented with unit tests | 2C.1 |
| 2C.3 | Implement `SignalScheduler` (hierarchical timing wheel): 10ms / 100ms / 1s / 10s / 1 min levels | 14 | Scheduler with O(1) tick | 2C.2 |
| 2C.4 | Integrate scheduler with tag engine: compute values → push to worker via IPC | 8 | Tags generate live values | 2C.3, 1.4 |
| 2C.5 | Implement write policy: override vs. pause, configurable per-tag | 4 | Write behavior matches spec §6.3 | 2C.2, 2A.9 |
| 2C.6 | Implement deterministic seeding: `Random` profiles accept seed for reproducible sequences | 3 | Determinism requirement met | 2C.2 |

**Phase 2 exit criteria:**
- [ ] Modbus TCP: all FCs pass, Wireshark clean, third-party client connects and reads/writes
- [ ] S7comm: all functions pass (read/write/SZL), all 4 personality types respond, Wireshark clean, Snap7/S7.Net client connects
- [ ] Signal generators produce correct waveforms verified by unit tests
- [ ] Tags update at configured intervals in the UI
- [ ] Write policy (override/pause) works correctly

---

## Phase 3: Remaining Protocols — Parallel Tracks (Weeks 6–11)

All four remaining protocols can be implemented concurrently. Each track is sized at
1–2 weeks.

### Track 3A: EtherNet/IP + CIP

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 3A.1 | Implement ENIP encapsulation layer: RegisterSession (0x0065), forwardClose, session handle management | 10 | ENIP session handshake works | 1.1 |
| 3A.2 | Implement UDP ListIdentity (0x0063) responder: CIP Identity object with Vendor=1 (Rockwell), device type 14 (Controller), product code per personality | 6 | ListIdentity responds | 3A.1 |
| 3A.3 | Implement CIP common packet format: response path (EPATH), general/extra status codes | 8 | CIP format correct | 3A.1 |
| 3A.4 | Implement Read Tag service (0x4C/0x4D): by symbol name, fragmented read support | 10 | Tag read works | 3A.3, 2A.9 |
| 3A.5 | Implement Write Tag service (0x4E): by symbol name, type validation | 6 | Tag write works | 3A.3, 2A.9 |
| 3A.6 | Implement Tag list browse (0x55 Get Instance Attributes List): return all tag names, types, array dimensions | 8 | Browse works | 3A.3, 2A.9 |
| 3A.7 | Implement CIP Identity object (class 0x01, instance 1): Get Attributes All via 0x01 service | 4 | Identity matches personality | 3A.3 |
| 3A.8 | Implement CIP error codes: 0x04 (Path destination unknown), 0x05 (Path size), 0x08 (Service not supported), 0x0E (Attribute not supported), 0x13 (Not enough data), 0x1F (Vendor specific), 0x20 (Invalid parameter), etc. | 6 | Correct error semantics | 3A.3 |
| 3A.9 | Implement personality defaults: ControlLogix 1756-L7x, CompactLogix 1769-L33ER (Micro800 is phase 2) | 4 | Distinct nameplates | 3A.2, 3A.7 |
| 3A.10 | Implement `EnipAddressParser`: Logix tag names, array subscript syntax `MyArray[5]`, nested structs `MyStruct.Fld` | 6 | Address parsing | 1.2 |
| 3A.11 | Implement `EnipListener` (TCP :44818 + UDP :44818): dual listener, session management, connection limits | 8 | TCP/UDP server | 3A.1 |
| 3A.12 | Integrate with tag engine + signal generators | 4 | End-to-end | 3A.4, 3A.11, 2A.9 |
| 3A.13 | Unit tests + test vectors from Wireshark pcaps of real ControlLogix | 10 | ≥ 80 % coverage | 3A.1–3A.12 |
| 3A.14 | Integration test: `pycomm3` / `libplctag` connects, reads/writes/browses | 4 | Third-party client | 3A.12 |
| 3A.15 | Wireshark validation: capture → CIP dissector parses cleanly | 4 | 0 malformed packets | 3A.13 |

### Track 3B: MELSEC MC Protocol 3E

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 3B.1 | Implement MC 3E binary frame: subheader, network/PLC/station numbers, request length, monitoring timer | 6 | Frame format correct | 1.1 |
| 3B.2 | Implement batch read: 0x0401 (bit units), 0x0403 (word units) — device code + address → data | 8 | Batch read works | 3B.1 |
| 3B.3 | Implement batch write: 0x1401 (bit units), 0x1403 (word units) | 6 | Batch write works | 3B.1 |
| 3B.4 | Implement random read/write: 0x0604 (random read), 0x1604 (random write) — variable list of device/address pairs | 8 | Random access works | 3B.1 |
| 3B.5 | Implement `McDeviceMapper`: device codes M(0x4D), X(0x58), Y(0x59), B(0x42), D(0x44), W(0x57), R(0x52), ZR(0xEA) → internal tag map | 6 | Device mapping | 3B.1 |
| 3B.6 | Implement `MelsecAddressParser`: `D100`, `M0`, `X10`, `Y4`, `W0F`, `R0`, `ZR100` | 4 | Address parsing | 1.2 |
| 3B.7 | Implement error codes: 0xC05C (device designation error), 0xC051 (request data length error), other common codes | 4 | Error responses | 3B.1 |
| 3B.8 | Implement personality defaults: FX5U (model name "FX5U-32MT/ES"), Q03UDE (model name "Q03UDE") | 3 | Nameplate response | 3B.1 |
| 3B.9 | Implement model name read command: special subcommand returns CPU model string | 3 | Model name responds | 3B.8 |
| 3B.10 | Implement `MelsecListener` (TCP :5007): async accept, session tracking, connection limits | 6 | TCP server | 3B.1 |
| 3B.11 | Integrate with tag engine + signal generators | 4 | End-to-end | 3B.2, 3B.10, 2A.9 |
| 3B.12 | Unit tests + Wireshark validation from real FX5U/Q03UDE captures | 8 | ≥ 80 % coverage | 3B.1–3B.11 |
| 3B.13 | Integration test: `pymcprotocol` reads/writes against simulator | 4 | Third-party connects | 3B.11 |

### Track 3C: Beckhoff ADS

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 3C.1 | Implement AMS/TCP framing: 4-byte length prefix + AMS header (32 bytes: target/source NetID + port, command ID, state flags, length) | 8 | AMS packet format | 1.1 |
| 3C.2 | Implement AMS NetID derivation from IP: `w.x.y.z.1.1` | 2 | NetID matches IP | 3C.1 |
| 3C.3 | Implement ReadDeviceInfo (0x0001): device name + version matching personality | 4 | Device info responds | 3C.1 |
| 3C.4 | Implement Read (0x0002) and Write (0x0003) by handle | 6 | Basic read/write | 3C.1 |
| 3C.5 | Implement ReadWrite (0x0005): combined read/write in single command | 4 | Combined operation | 3C.4 |
| 3C.6 | Implement GetHandle (0x0009): symbol name → 4-byte handle, ReleaseHandle (0x000A) | 6 | Handle management | 3C.1 |
| 3C.7 | Implement symbol table service: GetSymbolInfo (0x0012), browse all symbols with name/type/size/comment | 12 | Symbol browsing | 3C.1 |
| 3C.8 | Implement notification subscriptions: AddNotification (0x0006), DeleteNotification (0x0007), Notify (0x0008) with cyclic rate | 14 | Notifications work | 3C.4, 3C.6 |
| 3C.9 | Implement ReadState (0x000F): return ADS state + device state | 3 | State response | 3C.1 |
| 3C.10 | Implement error codes: 0x0 (OK), 0x700 (client port), 0x710 (symbol not found), 0x712 (symbol version), 0x740 (service not supported), 0x741 (invalid parameter) | 4 | Error codes match spec | 3C.1 |
| 3C.11 | Implement nested struct symbol support: `GVL.stMachine.nCount` → hierarchical browse | 6 | Struct browsing | 3C.7 |
| 3C.12 | Implement personality defaults: TwinCAT 3 (3.1.4024.x), TwinCAT 2 (2.11.0.x) | 3 | Dual personality | 3C.3 |
| 3C.13 | Implement `AdsListener` (TCP :48898): async accept, AMS router behavior, connection tracking | 8 | TCP server | 3C.1 |
| 3C.14 | Integrate with tag engine + signal generators | 6 | End-to-end | 3C.4, 3C.13, 2A.9 |
| 3C.15 | Unit tests + Wireshark validation from real TwinCAT captures | 12 | ≥ 80 % coverage | 3C.1–3C.14 |
| 3C.16 | Integration test: `ads-client` (Node.js) / `pyads` connects, browses symbols, reads/writes, subscribes notifications | 6 | Third-party connects | 3C.14 |

### Track 3D: OPC UA

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 3D.1 | Set up OPC UA server project with OPC Foundation SDK: `ApplicationDescription`, `ServerConfiguration`, endpoint registration | 8 | UA server starts, responds to discovery | 1.1 |
| 3D.2 | Implement `UaNodeManager` (custom `INodeManager`): map tags → `VariableNode` with correct `DataType` (built-in types), `NodeId` (ns=2,i=N pattern) | 12 | Tags exposed as UA variables | 3D.1, 2A.9 |
| 3D.3 | Build UA address space: `Objects → Simulator → {PLC Name} → {Numeric, Boolean, String, DateTime, Array} → 5 tags each` | 8 | Browseable hierarchy | 3D.2 |
| 3D.4 | Implement Browse service: root → objects → filtering, correct `ReferenceDescription` for all nodes | 6 | Browse works | 3D.3 |
| 3D.5 | Implement Read service: read value + `DataValue` with timestamp, status code | 4 | Read works | 3D.2 |
| 3D.6 | Implement Write service: write value with type validation, `StatusCode.BadTypeMismatch` on error | 4 | Write works | 3D.2 |
| 3D.7 | Implement subscription support: `MonitoredItem` with `Notification` pump, `SamplingInterval` from tag updateMs, `PublishingInterval` configurable | 14 | Subscriptions deliver data changes | 3D.2 |
| 3D.8 | Implement `TranslateBrowsePathsToNodeIds`: resolve `ns=2;s=Simulator/Plc1/Temperature` → NodeId | 4 | Path translation | 3D.3 |
| 3D.9 | Implement `EUInformation` per tag: `AnalogUnit` with engineering unit from `tag.engUnit`, `EURange` from simulation limits | 4 | Engineering units | 3D.2 |
| 3D.10 | Implement server nameplate: `ApplicationUri`, `ProductUri`, `ManufacturerName` configurable per brand | 4 | Nameplate matches personality | 3D.1 |
| 3D.11 | Implement security: None + Basic256Sha256 (sign only) | 6 | Multiple security modes | 3D.1 |
| 3D.12 | Unit tests: browse 1000 nodes, read 100 items single request, subscribe 50 items | 8 | Performance + correctness | 3D.1–3D.11 |
| 3D.13 | Integration test: UaExpert connects, browses, reads/writes, subscribes | 4 | Third-party connects | 3D.11 |
| 3D.14 | Wireshark validation: capture → OPC UA dissector parses cleanly | 4 | 0 malformed packets | 3D.12 |

**Phase 3 exit criteria:**
- [ ] EtherNet/IP: CIP explicit messaging (read/write/browse/identity), third-party client connects, Wireshark clean
- [ ] MELSEC MC 3E: all device types, batch/random read/write, model name, third-party connects
- [ ] ADS: symbol browse, handle-by-name, read/write, notifications, third-party connects
- [ ] OPC UA: browseable address space, read/write/subscribe, UaExpert connects
- [ ] All 6 protocols run simultaneously without interference
- [ ] Default templates exist in `templates/*.json` for all 6 brands

---

## Phase 4: Tag Engine & Default Templates (Weeks 5–8, overlaps Phases 2–3)

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 4.1 | Design and implement template JSON schema per brand: 5 tags × each supported data type, with profile distribution (1×Step, 1×Sine, 1×Random, 1×Static, 1×Echo) | 12 | 6 template JSON files | 1.2 |
| 4.2 | Implement `TagTemplateEngine`: load template by brand+personality, instantiate tags with correct addresses, types, default profiles | 8 | Template instantiation | 4.1 |
| 4.3 | Implement S7 template: addresses DB1.DBX0.0 (Bool), DB1.DBW2 (Int16), DB1.DBD4 (Float32), MW20 (Word), IW0 (input), QW0 (output), DB2.DBB0 (String[32]), DB1.DBD8 (DInt), DB1.DBD12 (Real), etc. — 5 of each supported type (Bool, Byte, Int16, UInt16, Int32, UInt32, Float32, String, Time) | 6 | S7 template file | 4.1 |
| 4.4 | Implement Rockwell template: Controller scope tags `Sim_Bool_01..05`, `Sim_Dint_01..05`, `Sim_Real_01..05`, `Sim_String_01..05`, `Sim_Real_Arr[10]` | 6 | Rockwell template file | 4.1 |
| 4.5 | Implement Modbus template: Coils 1–5, DI 10001–10005, IR 30001+ (Int16/Int32/Float32), HR 40001+ (all types × 4 word orders) | 8 | Modbus template file | 4.1 |
| 4.6 | Implement MELSEC template: M0–M4 (Bool), X0–X4 (Bool), Y0–Y4 (Bool), D100+ (Int16/Int32/Float32), W0+ (Int16), R0+ (Float32) | 6 | MELSEC template file | 4.1 |
| 4.7 | Implement ADS template: `MAIN.bPump1..5` (Bool), `MAIN.nCount1..5` (Int32), `MAIN.fTemp1..5` (Real), `MAIN.sStatus1..5` (String), `GVL.stMachine.nRPM` (nested struct), `GVL.stMachine.bRunning` (nested bool) | 8 | ADS template file | 4.1, 3C.11 |
| 4.8 | Implement OPC UA template: folder-per-type layout (`/Numeric/`, `/Boolean/`, `/String/`, `/DateTime/`, `/Arrays/`) with 5 vars each | 6 | OPC UA template file | 4.1 |
| 4.9 | Implement PLC detail page in UI: virtualized tag table (TanStack Table + @tanstack/react-virtual), all columns, inline editing of simulation params | 20 | Tag table renders 1000+ rows smoothly | 1.17, 1.19 |
| 4.10 | Implement sparkline component (ECharts mini chart showing last 50 values per tag) | 6 | Sparklines in tag table | 4.9 |
| 4.11 | Implement tag filtering: by data type, profile type, access, search by name | 6 | Filter bar works | 4.9 |
| 4.12 | Implement add/remove/clone tag functionality → calls REST API | 8 | Tag CRUD | 4.9, 1.13 |
| 4.13 | Implement Tag Editor page: full profile form with live preview chart (ECharts) | 14 | Profile editor with preview | 4.9 |
| 4.14 | Implement address input with brand-specific validation and autocomplete | 8 | Address validation per brand | 4.9 |

**Phase 4 exit criteria:**
- [ ] All 6 brand templates load on PLC creation (5 tags × N types)
- [ ] Tag table renders and remains smooth at 1000+ tags
- [ ] Profile editor shows waveform preview that matches actual tag output
- [ ] Add/remove/clone tags works for all brands
- [ ] All 13 signal profiles produce correct values (verified by unit tests + visual)

---

## Phase 5: Export/Import (Weeks 9–10)

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 5.1 | Implement JSON export: serialize PLC instance + all tags to JSON file (full fidelity) | 4 | JSON export | 1.8, 1.2 |
| 5.2 | Implement JSON import: deserialize, validate schema, merge into fleet with conflict resolution | 8 | JSON import | 5.1, 1.10 |
| 5.3 | Implement CSV export (Kepware-compatible): `Name,Address,DataType,ScanRate,Protocol,Description,ClientAccess,Enabled` per specification format | 6 | CSV export matches `tags_export.csv` format | 1.2, 1.10 |
| 5.4 | Implement Kepware DataType mapping: map common types → Kepware vocabulary per brand driver (e.g., `Int16` → `Word` for Modbus, `Float32` → `Float` for S7, `Bool` → `Boolean`, `String` → `String`, etc.) | 6 | CSV uses correct Kepware types | 5.3 |
| 5.5 | Implement CSV import: parse via CsvHelper, validate rows, show preview with accept/reject | 8 | CSV import with validation | 5.3, 1.10 |
| 5.6 | Implement XLSX export: one sheet per PLC, fleet overview sheet, ClosedXML formatting | 10 | XLSX export | 1.10 |
| 5.7 | Implement import conflict resolution UI: new/skip/overwrite/rename per PLC and per tag | 8 | Import dialog with conflict handling | 5.2, 5.5 |
| 5.8 | Implement Export/Import page in UI: format pickers, scope (per-PLC / fleet), drag-drop zone, validation preview | 12 | Export/Import page | 5.2, 5.5, 5.6, 5.7 |
| 5.9 | Test CSV round-trip: export fleet → import same file → verify identical | 4 | Round-trip test | 5.3, 5.5 |

**Phase 5 exit criteria:**
- [ ] CSV export matches `tags_export.csv` format exactly (Kepware-compatible)
- [ ] CSV import validates rows, reports errors per line
- [ ] JSON export/import is lossless round-trip
- [ ] XLSX export opens correctly in Excel with formatted sheets
- [ ] Import handles conflicts (new PLC / overwrite / skip)

---

## Phase 6: UI Polish & Realism Features (Weeks 10–12)

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 6.1 | Implement Traffic Monitor UI: per-PLC connection list (IP, connect time, req count), rolling event log, protocol counters | 16 | Traffic page | 1.19, 1.15 |
| 6.2 | Implement traffic log ring buffer in worker: last 10 000 events per PLC, pushed via IPC to supervisor | 8 | Traffic data source | 1.4 |
| 6.3 | Implement per-protocol counters: requests/sec, reads/sec, writes/sec, errors/sec, active connections | 6 | Counter tracking | 6.2 |
| 6.4 | Implement Trends page: multi-series strip chart with time-range selector and tag picker | 14 | Live trend chart | 1.19, 4.9 |
| 6.5 | Implement `FaultInjector` middleware in worker: configurable delay, drop, error response injection at configured rate | 10 | Fault injection | 2A.8, 2B.11, 3A.11, 3B.10, 3C.13, 3D.1 |
| 6.6 | Implement fault rules UI: toggle per-PLC, configure delay/drop/error rates | 6 | Fault config UI | 6.5, 4.9 |
| 6.7 | Implement connection-drop behavior: simulate TCP RST on configured intervals | 4 | Connection drop | 6.5 |
| 6.8 | Implement per-instance base latency + jitter: configurable through UI, applied in protocol response path | 6 | Realistic timing | 2B.13 |
| 6.9 | Implement request rate limiting: configurable max requests/sec per connection | 4 | Rate limiting | 6.2 |
| 6.10 | Implement `config.json` global settings: NIC selection, theme (dark/light), loopback default, auto-start on restart | 6 | Global settings | 1.8 |
| 6.11 | Implement dark/light theme in React: Mantine theme provider, persist preference | 4 | Theme toggle persists | 1.17 |
| 6.12 | Implement responsive layout: sidebar collapses, table horizontal scroll, mobile-friendly (down to 1024px) | 8 | Responsive design | 1.17 |
| 6.13 | Implement confirmation dialogs for all destructive actions (delete PLC, delete tag, stop PLC, etc.) | 4 | Confirmation modals | 1.17 |
| 6.14 | Implement startup state restoration: read `state.json` → auto-start previously running PLCs | 4 | Auto-start | 1.8 |
| 6.15 | Implement UI loading states, error boundaries, empty states for all pages | 6 | UX polish | 4.9, 5.8, 6.1, 6.4 |
| 6.16 | Implement web UI local-only binding (default `127.0.0.1`) with optional LAN toggle | 4 | Security default | 1.12 |
| 6.17 | Implement protocol debug logging: per-protocol toggle in System page, separate log file per brand | 6 | Debug logging | 1.12 |
| 6.18 | Implement System page: elevation status, port conflict table, log viewer (tail last 500 lines), config folder shortcut, app version | 8 | System page | 1.14 |

**Phase 6 exit criteria:**
- [ ] Traffic Monitor shows live connections, event log, and protocol counters
- [ ] Trends page shows live strip charts for selected tags
- [ ] Fault injection (delay/drop/error) works at configured rates
- [ ] Latency + jitter settings produce measurable response time variation
- [ ] Dark/light theme works and persists
- [ ] Responsive layout works down to laptop widths (1024px)
- [ ] All destructive actions have confirmation dialogs
- [ ] Startup restores previously running PLCs
- [ ] OPC UA security (Basic256Sha256) works

---

## Phase 7: Testing & Acceptance (Weeks 12–14)

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 7.1 | Write unit tests for all protocol address parsers: 50+ test cases per brand | 12 | Address parser coverage ≥ 95 % | Phases 2, 3 |
| 7.2 | Write protocol request/response round-trip tests (in-memory byte streams) for all FCs/services | 16 | Protocol unit tests | Phases 2, 3 |
| 7.3 | Write integration tests: each protocol against its TCP listener in same-process mode | 12 | Integration tests | Phases 2, 3 |
| 7.4 | Set up 6-PLC concurrent test: all brands on 6 IPs, verify no port conflicts, no crashes | 8 | Concurrent test | Phase 3 |
| 7.5 | 8-hour soak test: 6 PLCs × all tags polled at 1s interval → monitor memory, CPU, connections | 16 | Stability verified | 7.4 |
| 7.6 | Write test for deterministic signal generation: same seed → same waveform every run | 4 | Determinism verified | 2C.6 |
| 7.7 | Write write-policy tests: override behavior, pause behavior for all protocol brands | 6 | Write policy verified | 2C.5 |
| 7.8 | Perform NexEdgeLogger validation (manual, requires actual machine): connect each of 6 drivers, browse, subscribe, read, write, 8-hour run | 24 | Acceptance criteria A1–A6 met | Phase 3 |
| 7.9 | Perform Wireshark validation for all 6 protocols: capture 5 min → verify 0 malformed | 8 | Acceptance criteria A4 | Phase 3 |
| 7.10 | Perform second reference client validation: UaExpert (OPC UA), pycomm3 (Rockwell), pymcprotocol (MELSEC), ads-client (ADS), ModbusPoll (Modbus), Snap7 (S7) concurrently | 12 | Acceptance criteria A3 | Phase 3 |
| 7.11 | Performance test: 20 PLCs × 1000 tags × 100ms update, 10 clients per PLC → verify CPU < 50%, RAM < 2 GB | 8 | Non-functional N1–N2 | 7.4 |
| 7.12 | Edge case tests: zero-length requests, fragment reassembly, multi-byte wrong offsets, concurrent 100x write storm | 8 | Robustness verified | Phase 3 |
| 7.13 | Error handling tests: invalid addresses, wrong data types, out-of-range values, connection limits exceeded | 8 | Error semantics correct | Phase 3 |
| 7.14 | Export/import round-trip tests: CSV → fleet → export CSV → diff identical, JSON → same | 4 | Export fidelity | 5.9 |
| 7.15 | Memory leak detection: run 24h with monitoring, report any handle/GDI/memory growth | 8 | No leaks | 7.5 |
| 7.16 | Bug bash: structured testing session covering all UI pages, all protocols, all profiles | 16 | Bug list triaged | All previous |
| 7.17 | Fix all P0/P1 bugs found during testing | 20 | Stable release candidate | 7.16 |
| 7.18 | Update all unit tests for bug-fix coverage | 8 | Coverage maintained | 7.17 |

**Phase 7 exit criteria:**
- [ ] All 13 acceptance criteria from SPEC §13 pass
- [ ] Wireshark: 0 malformed for all 6 protocols
- [ ] Soak test: 8 hours, no crash, no leak, no protocol error
- [ ] Performance: 20 PLCs × 1000 tags within resource budget
- [ ] All P0/P1 bugs fixed
- [ ] Unit test coverage ≥ 80 % overall, ≥ 90 % for core modules

---

## Phase 8: Packaging & Documentation (Weeks 14–15)

### Tasks

| ID | Task | Est. (h) | Deliverable | Dependencies |
|----|------|---------|-------------|--------------|
| 8.1 | Create single-folder publish: `dotnet publish` with trimmed self-contained deployment | 6 | Distributable folder | All |
| 8.2 | Create Windows installer (WiX Toolset or Inno Setup): install to `Program Files`, start menu shortcut, UAC manifest for elevated helper | 12 | Installer `.exe` or `.msi` | 8.1 |
| 8.3 | Implement auto-update check: probe a configurable URL, notify user of new version | 6 | Update mechanism | 8.1 |
| 8.4 | Write user manual: installation, configuration, usage per page, troubleshooting | 16 | User manual PDF | All |
| 8.5 | Write protocol integration guide: how each brand maps to real PLCs, known limitations | 8 | Integration guide | Phase 3 |
| 8.6 | Write developer setup guide: build from source, test harness, adding new protocols | 8 | Developer guide | 1.1 |
| 8.7 | Review all licenses: compile OSS notice file with all dependency licenses | 4 | LICENSE-3RD-PARTY.txt | All |
| 8.8 | Code signing: sign all `.exe` files with certificate | 2 | Signed binaries | 8.1 |
| 8.9 | Final regression test on clean Windows 11 VM: install → run → create 6 PLCs → validate | 8 | Installer verified | 8.2, 8.8 |

**Phase 8 exit criteria:**
- [ ] Installer installs and runs on clean Windows 11 Pro
- [ ] User manual covers all features
- [ ] Protocol integration guide documents limitations per brand
- [ ] All third-party licenses documented
- [ ] Binaries are code-signed
- [ ] Clean VM installation test passes

---

## Effort Summary

| Phase | Tasks | Est. Hours (total) | Calendar (1 dev) | Calendar (3 dev team) |
|-------|-------|-------------------|------------------|----------------------|
| P1: Foundation | 23 | 172 | 3 weeks | 1.5 weeks |
| P2A: Modbus | 14 | 81 | 2 weeks | 1 week |
| P2B: S7comm | 16 | 104 | 2.5 weeks | 1.5 weeks |
| P2C: Signals | 6 | 52 | 1 week | 1 week |
| P3A: EtherNet/IP | 15 | 98 | 2 weeks | 1.5 weeks |
| P3B: MELSEC | 13 | 67 | 1.5 weeks | 1 week |
| P3C: ADS | 16 | 112 | 2.5 weeks | 1.5 weeks |
| P3D: OPC UA | 14 | 82 | 2 weeks | 1 week |
| P4: Tags & Templates | 14 | 112 | 2.5 weeks | 1.5 weeks |
| P5: Export/Import | 9 | 66 | 1.5 weeks | 1 week |
| P6: UI Polish & Realism | 18 | 106 | 2.5 weeks | 1.5 weeks |
| P7: Test & Acceptance | 18 | 180 | 4 weeks | 2 weeks |
| P8: Package & Document | 9 | 74 | 2 weeks | 1.5 weeks |
| **Total** | **185** | **1 306** | **~29 weeks** | **~17 weeks** |

> **Note:** The total of 1 306 hours assumes ideal conditions. With buffers (20 % for
> unknowns, meetings, code review, context switching), plan for **1 800–2 400 hours**
> (11–15 person-months / 4–6 months for a team of 3).

---

## Risk Register

| Risk | Probability | Impact | Mitigation |
|------|-----------|--------|------------|
| **OPC UA SDK complexity** — learning curve, threading model, memory management | Medium | High | Start Phase 3D early with dedicated spike (2 days). Use SDK examples as template. |
| **ADS notification system** — real-time requirements, cyclic notification | Medium | High | Implement as simplified polling-based notification first, optimize later. Use timer-driven check rather than interrupt-driven. |
| **CIP fragmentation** — Read Tag Fragmented service for large arrays | Low | Medium | Limit array size in v1 to ≤ 1024 elements. Implement fragmentation in phase 2 of CIP. |
| **Windows IP alias limitations** — `netsh` may fail on certain NIC drivers (WiFi, virtual adapters) | Medium | Medium | Detect failure → fall back to loopback with clear UI message. Test on top 3 NIC types (Realtek, Intel, Broadcom). |
| **Port excluded range conflicts** — Windows reserves port ranges for NAT | High | Low | Detect via `netsh int ipv4 show excludedportrange tcp`. Provide one-click workaround (`net stop winnat` guidance). |
| **Worker process memory leak** in long-running protocol sessions | Medium | High | Implement worker watchdog with max session duration (default 24h), auto-recycle. Profiling in Phase 7. |
| **React performance at 1000+ tags** with sparklines | Medium | Medium | Use windowing (react-virtual), canvas sparklines (not SVG), limit history to 100 points per tag. |
| **Signal generator determinism divergence** after long runs | Low | Medium | Use 64-bit monotonic tick counter, not `DateTime.Now`. Verify determinism in Phase 7. |
| **Wireshark malformed packets** for obscure protocol features | Medium | High | Build test corpus from real PLC captures. Validate with `tshark -Y "s7comm"`. Unit test every PDU byte. |
| **Elevated helper UAC prompt fatigue** | High | Low | Cache elevation session, show prompt once per app launch. Document in UI that elevation is needed only for NIC ops. |

---

## Key Dependencies & Parallelization

```
Week:   1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
        │  │  │  │  │  │  │  │  │  │  │  │  │  │  │
P1      ████████████████████████
P2A     ──────────██████████████
P2B     ──────────████████████████████████
P2C     ───────────────────████████████
P3A     ───────────────────────████████████████████
P3B     ───────────────────────██████████████
P3C     ───────────────────────████████████████████████
P3D     ───────────────────────████████████████
P4      ────────────██████████████████████████
P5      ─────────────────────────────────██████████
P6      ────────────────────────────────────████████████████
P7      ─────────────────────────────────────────████████████████
P8      ───────────────────────────────────────────────────████████
        │  │  │  │  │  │  │  │  │  │  │  │  │  │  │
```

- P1 is fully sequential (foundation required by everything).
- P2A (Modbus) and P2B (S7) can be parallelized after P1.
- P2C (Signals) starts after P2A is stable (needs tag engine from P2A.9).
- P3 (four protocol tracks) run fully in parallel after P2A completes (P2A proves the protocol→tag integration pattern).
- P4 (Templates) overlaps P2–P3; starts after P2C is stable.
- P5 (Export) starts after P4 completes (templates define the full tag model).
- P6 (UI Polish) starts after P4–P5.
- P7 (Testing) starts after all implementation phases complete.
- P8 (Packaging) starts after P7 acceptance.

---

## Acceptance Criteria Verification Matrix

| Criterion (from SPEC §13) | How Verified | Phase |
|---------------------------|-------------|-------|
| A1: NexEdgeLogger connects, browses, subscribes, logs correct values for ≥ 30 min per brand | Manual test with NexEdgeLogger on separate machine + soak test | P7 |
| A2: Writes to Echo tags round-trip correctly for all data types | Automated integration test + manual check | P7 |
| A3: Second reference client validates same instance concurrently | Manual test with reference clients per brand | P7 |
| A4: Wireshark 0 malformed packets in 5-minute session | Capture + `tshark` analysis script | P7 |
| A5: Step/auto-reverse matches configured waveform exactly | Unit test + Trend chart visual check | P7 |
| A6: 6 PLCs running simultaneously 8 hours, zero crashes | Soak test harness — 6 workers, all ports, all tags | P7 |
| G1–G8: All specification goals | Traceability matrix in test plan | All |
| N1–N6: Non-functional requirements | Performance/scale test suite | P7 |

---

## Infrastructure Requirements

| Resource | Specification | Purpose |
|----------|--------------|---------|
| Dev machine | Windows 11 Pro, 16 GB RAM, 4+ cores, 256 GB SSD | Development + testing |
| Test machine 1 | Windows 11, 8 GB RAM | NexEdgeLogger client (separate machine for LAN tests) |
| Test machine 2 (optional) | Any OS, 4 GB RAM | Second reference client (UaExpert, etc.) |
| Network | 1 GbE switch, static IP range | Realistic LAN testing |
| Tools | Wireshark 4.x, NexEdgeLogger (latest), Visual Studio 2022, Node.js 20+, .NET 8 SDK | Dev + test toolchain |

---

## Definition of Done (per task)

A task is **done** when:

1. Code compiles with zero warnings (treated as errors)
2. All related unit tests pass
3. Integration test (where applicable) passes with a real TCP connection
4. Wireshark trace confirms correct protocol behavior (for protocol tasks)
5. UI task: visual inspection passes + component tests pass
6. No regression in existing tests
7. Code reviewed by at least one other developer (team context)
8. Documentation updated if behavioral change
