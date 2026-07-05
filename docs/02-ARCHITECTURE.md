# Multi-Brand PLC Simulator — Architecture Document

**Project:** Virtual Operational Factory — PLC Simulator
**Document status:** Draft for review · v0.1
**Supersedes:** 01-SPECIFICATION.md

---

## 1. System Context

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Windows 11 Host                               │
│                                                                      │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────────────┐  │
│  │  Client   │   │  Client  │   │  Client  │   │  NexEdgeLogger   │  │
│  │ (SCADA)   │   │ (UaExp.) │   │ (Kepware)│   │  (primary test)  │  │
│  └────┬─────┘   └────┬─────┘   └────┬─────┘   └────────┬─────────┘  │
│       │              │              │                   │            │
│       └──────────────┼──────────────┼───────────────────┘            │
│                      │              │                               │
│              ┌───────┴──────┐  ┌────┴────────┐                     │
│              │  Physical    │  │  Loopback   │                     │
│              │  NIC (eth0)  │  │  127.0.0.x  │                     │
│              │  192.168.x.x │  │  (fallback) │                     │
│              └───────┬──────┘  └────┬────────┘                     │
│                      │              │                               │
│              ┌───────┴──────────────┴───────────────────────┐      │
│              │        PLC Simulator Application              │      │
│              │  (Supervisor + per-PLC worker processes)      │      │
│              └──────────────────────────────────────────────┘      │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

The simulator runs entirely on a single Windows 11 machine. Each simulated PLC binds to a unique IP address on either the physical NIC (via secondary/alias IP) or the loopback interface. Client applications connect to these IPs using standard protocol ports; they cannot distinguish the simulator from real hardware at the wire level.

---

## 2. Process Architecture

### 2.1 Process Model

```
┌──────────────────────────────────────────────────────────────────┐
│                     Supervisor Process (PLC.Supervisor)          │
│  .NET 8 Console App ──── runs as user (no elevation needed)     │
│                                                                  │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │
│  │ REST API    │  │   WebSocket  │  │  Export/Import Engine  │  │
│  │ (ASP.NET    │  │   Hub (SigR) │  │  (CSV, JSON, XLSX)    │  │
│  │  Core)      │  │              │  │                        │  │
│  └─────────────┘  └──────────────┘  └────────────────────────┘  │
│                                                                  │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │
│  │ PLC Lifecycle│  │  Config Store│  │  Fleet Signal          │  │
│  │ Manager      │  │  (JSON on   │  │  Orchestrator          │  │
│  │              │  │   disk)     │  │                        │  │
│  └─────────────┘  └──────────────┘  └────────────────────────┘  │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  IPC Channel (Named Pipes / localhost TCP)               │    │
│  │  → Start/Stop PLC Worker                                 │    │
│  │  → Get tag values, connection stats, traffic logs        │    │
│  │  → Push config changes to worker                         │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
           │                   │                   │
     ┌─────┘         ┌─────────┘         ┌─────────┘
     ▼               ▼                   ▼
┌────────────┐ ┌────────────┐     ┌────────────┐
│ PLC Worker │ │ PLC Worker │ ... │ PLC Worker │
│ (Instance  │ │ (Instance  │     │ (Instance  │
│  1, Brand  │ │  2, Brand  │     │  n, Brand  │
│  S7)       │ │  Modbus    │     │  OPC UA    │
└────────────┘ └────────────┘     └────────────┘
```

### 2.2 Process Isolation Rationale

| Aspect | Detail |
|--------|--------|
| **Why separate processes** | G9 (Reliability): one crashing PLC must not affect others. Separate processes via `Process.Start()` with guaranteed resource cleanup on exit. |
| **IPC mechanism** | Named Pipes (Windows Named Pipe, `\\.\pipe\PLCWorker_{instanceId}`) — lowest latency inter-process communication on Windows, no port conflicts. Supervisor sends config snapshots; worker pushes tag updates at configured intervals. |
| **Worker lifecycle** | Supervisor spawns worker on PLC start, connects IPC. Worker runs until PLC stopped or process crash. Supervisor detects crash (process exit) and auto-restarts with configurable max-retry. |
| **Resource limits** | Each worker process gets a dedicated job object for CPU/memory limits (Windows Job Object API). Default: 100 MB RAM, 1 CPU core per worker. |

### 2.3 Elevated Helper

```
┌────────────────────────────────────────────────────────────┐
│              Elevated Helper (PLC.Elevator)                 │
│  .NET 8 Console App ──── runs as Administrator             │
│  Communication: Named Pipe `\\.\pipe\PLC_Elevator`         │
│                                                             │
│  Commands:                                                  │
│  • Add-NetIPAddress (alias IP on NIC)                      │
│  • Remove-NetIPAddress                                     │
│  • New-NetFirewallRule (allow port on protocol)            │
│  • Remove-NetFirewallRule                                  │
│  • Test-NetIPAddress (check if IP already in use)          │
│  • Get-NetAdapter (list physical NICs)                     │
│                                                             │
│  Auto-started by supervisor when elevation needed.          │
│  Supervisor shows clear error if elevation unavailable.     │
└────────────────────────────────────────────────────────────┘
```

- The helper is a separate binary, compiled with a manifest requesting `requireAdministrator`.
- Supervisor launches it on first network operation, keeps it alive for the session.
- All commands are authenticated via a shared secret passed over the pipe.
- If helper cannot be started (user denies UAC), supervisor runs in loopback-only mode.

---

## 3. Technology Stack

### 3.1 Backend (.NET 8)

| Component | Technology | Justification |
|-----------|-----------|---------------|
| Runtime | .NET 8.0 | LTS, cross-platform (future), excellent async I/O, Native AOT for workers |
| HTTP/WS server | ASP.NET Core 8 + SignalR | Built-in WebSocket/real-time, minimal overhead |
| Serialization | System.Text.Json + MessagePack | JSON for config files, MessagePack for IPC (performance) |
| OPC UA Server | OPC Foundation .NET UA Reference Stack (NuGet: `OPCFoundation.NetStandard.Opc.Ua`) | De facto standard, ASN.1 binary encoding, correct wire format |
| Export (XLSX) | ClosedXML (NuGet) | OpenXML without Office Interop, permissive license |
| CSV parsing | CsvHelper (NuGet) | Robust, handles edge cases, MIT license |
| Logging | Serilog (structured, file + Seq optional) | Industry standard, per-protocol log levels |
| DI / Config | Built-in Microsoft.Extensions | Standard .NET patterns |
| Elevated commands | System.Management.Automation (PowerShell via runspace) | Direct WinRM/NETSH access without shelling out |

### 3.2 Protocol Libraries

| Protocol | Library / Approach | Notes |
|----------|-------------------|-------|
| **S7comm** | Custom implementation (based on Snap7 source analysis) | No maintained open-source S7 **server** library exists. Implement ISO-on-TCP (RFC 1006) framing + COTP + S7comm PDU parsing/serialization. Reference: Snap7 source, Wireshark `packet-s7comm.c` |
| **EtherNet/IP + CIP** | Custom implementation (based on `libplctag` / Wireshark dissectors) | CIP explicit messaging only. Implement Encapsulation layer + CIP Read/Write Tag service, ListIdentity (UDP). Reference: ODVA CIP spec vol 2, Wireshark `packet-cip.c` |
| **Modbus TCP** | NModbus (NuGet: `NModbus`) or custom | Well-established. NModbus supports server mode. If library limitations found, custom implementation using the Modbus spec is straightforward |
| **MELSEC MC 3E** | Custom implementation | SLMP/MC 3E binary protocol is simple: fixed header + data. Reference: Mitsubishi manuals, Wireshark `packet-mcslmp.c`, `pymcprotocol` source |
| **Beckhoff ADS** | Custom implementation | Requires AMS NetID routing, symbol info (browse), handle-by-name, notifications. Reference: TwinCAT AdsDll source headers, `ads-client` TypeScript library, Wireshark `packet-ams.c` |
| **OPC UA** | OPC Foundation UA SDK (NuGet) | Full server stack with all encodings. Implement `INodeManager` for tag browsing, subscriptions, method calls |

### 3.3 Frontend

| Component | Technology | Justification |
|-----------|-----------|---------------|
| Framework | React 18 + TypeScript | Strong ecosystem, component model |
| Build tool | Vite | Fast HMR, small bundles |
| UI library | Mantine v7 (or Ant Design) | Modern, dark/light, accessible, tree-shakeable |
| Charts | Apache ECharts + echarts-for-react | Rich chart types, sparklines, strip charts |
| State | Zustand + TanStack Query | Lightweight, server-state caching |
| WebSocket | SignalR JS client | Bidirectional RPC, fallback transports |
| Virtualized grid | TanStack Table (React Table) + @tanstack/react-virtual | Handles 1000+ rows smoothly |
| Forms | react-hook-form + zod | Validation, schemas |
| Routing | React Router 6 | SPA routing |

---

## 4. Component Architecture

### 4.1 Supervisor Internal Modules

```
PLC.Supervisor/
├── Api/                          # ASP.NET Core controllers + SignalR hubs
│   ├── Controllers/
│   │   ├── FleetController.cs    # GET/POST/PUT/DELETE /api/plcs
│   │   ├── TagsController.cs     # GET/PUT /api/plcs/{id}/tags
│   │   ├── NetworkController.cs  # GET /api/network/* (NICs, IPs, status)
│   │   ├── ExportController.cs   # GET/POST /api/export/*
│   │   └── SystemController.cs   # GET /api/system/* (elevation, logs, etc.)
│   ├── Hubs/
│   │   ├── FleetHub.cs           # Real-time tag values, connection stats
│   │   └── TrafficHub.cs         # Real-time protocol event stream
│   └── Middleware/
│       └── ErrorHandling.cs      # Structured error responses
├── Core/                         # Domain model — no external dependencies
│   ├── Models/
│   │   ├── PlcInstance.cs        # PLC aggregate root
│   │   ├── TagDefinition.cs      # Single tag with all metadata
│   │   ├── SimulationProfile.cs  # Base + 11 concrete profile types
│   │   ├── Brand.cs              # Brand enum + metadata
│   │   ├── Personality.cs        # Model-specific defaults
│   │   └── NetworkConfig.cs      # IP, port overrides, NIC binding
│   ├── Enums/
│   │   ├── PlcState.cs           # Created | Running | Stopped | Error
│   │   ├── TagAccess.cs          # ReadOnly | ReadWrite
│   │   └── ProfileType.cs        # Static | Step | Ramp | Sine ... Echo
│   └── ValueObjects/
│       ├── DataPoint.cs          # Timestamp + value (for sparklines)
│       └── ProtocolError.cs      # Structured error info
├── Services/
│   ├── FleetService.cs           # CRUD, lifecycle orchestration
│   ├── PlcProcessManager.cs      # Spawn/manage worker processes
│   ├── ConfigPersistence.cs      # JSON read/write with atomic save
│   ├── NetworkService.cs         # Interface to elevated helper
│   ├── ElevatorClient.cs         # Named pipe client to PLC.Elevator
│   ├── PortConflictChecker.cs    # Scan for port conflicts
│   ├── TagTemplateEngine.cs      # Load default templates per brand
│   └── ExportService.cs          # CSV/JSON/XLSX export/import
├── SignalGeneration/             # Runs in supervisor, pushed to workers
│   ├── SignalScheduler.cs        # Per-PLC monotonic scheduler (timer wheel)
│   ├── Profiles/
│   │   ├── StaticProfile.cs
│   │   ├── StepProfile.cs
│   │   ├── RampProfile.cs
│   │   ├── SineProfile.cs
│   │   ├── SquareProfile.cs
│   │   ├── TriangleProfile.cs
│   │   ├── RandomProfile.cs
│   │   ├── ToggleProfile.cs
│   │   ├── PulseProfile.cs
│   │   ├── CounterProfile.cs
│   │   ├── ClockProfile.cs
│   │   ├── TextCycleProfile.cs
│   │   └── EchoProfile.cs
│   └── IProfileGenerator.cs      # Interface for all profiles
├── Ipam/                         # IP Address Management
│   ├── IpPool.cs                 # Manage assigned IPs, detect conflicts
│   └── SubnetCalculator.cs       # Validate IP/subnet, suggest next free
└── Infrastructure/
    ├── Serialization/
    │   ├── TagConverter.cs       # JSON converter for polymorphic profiles
    │   └── ConfigSerializer.cs   # JSON round-trip with versioning
    ├── Logging/
    │   ├── StructuredLogger.cs   # Serilog wrapper with per-protocol filtering
    │   └── TrafficLogger.cs      # Ring buffer of recent protocol events
    └── Platform/
        ├── WindowsFirewall.cs    # Firewall rule management
        ├── NetAdapterInfo.cs     # Get NIC info via WMI
        └── JobObject.cs          # Process resource limits via job objects
```

### 4.2 Worker Internal Modules

```
PLC.Worker/
├── Program.cs                    # Entry: parse args (instanceId, supervisorPipe),
                                  #   connect IPC, start protocol listener
├── PlcContext.cs                 # In-memory state: tags, connections, config
├── Ipc/
│   └── WorkerPipeServer.cs       # Named pipe server for supervisor IPC
├── Protocol/
│   ├── IProtocolListener.cs      # Interface: Start(), Stop(), Stats
│   ├── Common/
│   │   ├── TcpListenerBase.cs    # Shared TCP accept + session management
│   │   ├── SessionBase.cs        # Shared connection tracking
│   │   └── ByteBuffer.cs         # Efficient read/write buffer
│   ├── S7/
│   │   ├── S7Listener.cs         # TCP :102 → ISO-on-TCP → COTP → S7comm
│   │   ├── S7Session.cs          # Per-connection state
│   │   ├── CotpLayer.cs          # COTP connection request/data transfer
│   │   ├── TsapMapper.cs         # TSAP → rack/slot mapping
│   │   ├── S7RequestParser.cs    # Parse S7comm PDU (header + params + data)
│   │   ├── S7ResponseBuilder.cs  # Build S7comm response PDU
│   │   ├── SzlProvider.cs        # SZL (System State List) responses for identity
│   │   ├── DataView.cs           # DB/M/flags/I/O → tag resolution
│   │   └── S7AddressParser.cs    # Parse "DB10.DBW100", "M5.3", etc.
│   ├── Rockwell/
│   │   ├── EnipListener.cs       # TCP :44818 + UDP :44818 (ListIdentity)
│   │   ├── EnipSession.cs        # Encapsulation layer connection
│   │   ├── CipLayer.cs           # CIP common packet format
│   │   ├── CipTagService.cs      # Read/Write Tag service (0x4C/0x4D)
│   │   ├── CipBrowseService.cs   # Tag list browse (0x55)
│   │   ├── IdentityObject.cs     # CIP Identity object (class 0x01)
│   │   └── EnipAddressParser.cs  # Parse "N7:0", "MyTag", "Array[5]"
│   ├── Modbus/
│   │   ├── ModbusListener.cs     # TCP :502
│   │   ├── ModbusSession.cs      # MBAP header + PDU
│   │   ├── ModbusRequestHandler.cs # FC 1-6, 15, 16, 22, 23, 43/14
│   │   └── ModbusAddressMap.cs   # Coil/discrete/input/holding → tag lookup
│   ├── Melsec/
│   │   ├── MelsecListener.cs     # TCP :5007 (UDP optional)
│   │   ├── MelsecSession.cs      # MC 3E binary frame
│   │   ├── McFrameParser.cs      # Subheader + network no. + station no. + data
│   │   ├── McCommandHandler.cs   # Device read/write (batch, random, etc.)
│   │   ├── McDeviceMapper.cs     # M, X, Y, B, D, W, R, ZR, etc. → memory area
│   │   └── MelsecAddressParser.cs# Parse "D100", "M0", "X10", "W0F"
│   ├── Ads/
│   │   ├── AdsListener.cs        # TCP :48898 (AMS router)
│   │   ├── AdsSession.cs         # AMS/TCP framing
│   │   ├── AmsPacket.cs          # AMS header + ADSPayload
│   │   ├── AdsCommandHandler.cs  # ReadDeviceInfo, Read/Write, ReadWrite
│   │   ├── AdsSymbolService.cs   # Symbol table browse, handle-by-name
│   │   ├── AdsNotificationService.cs # Device notification subscriptions
│   │   └── AdsAddressParser.cs   # Parse "MAIN.fTemp", "GVL.stMachine.nCount"
│   ├── OpcUa/
│   │   ├── UaServerHost.cs       # OPC UA Server instance (OPC Foundation SDK)
│   │   ├── UaNodeManager.cs      # INodeManager → maps tags to UA nodes
│   │   ├── UaSubscriptionManager.cs # Subscribe → notification pump
│   │   └── UaNameplate.cs        # ApplicationDescription, ServerStatus
│   └── FaultInjection/
│       ├── FaultInjector.cs      # Middleware: delay, drop, error responses
│       └── FaultRule.cs          # Configurable rules (rate, type, pattern)
├── TagEngine/
│   ├── TagTable.cs               # Thread-safe tag resolution by name/address
│   ├── TagValueStore.cs          # Current values, history ring buffer
│   └── WritePolicy.cs            # Accept-write → override/pause generator
└── Configuration/
    └── WorkerConfig.cs           # Deserialized from IPC startup message
```

### 4.3 Elevated Helper Modules

```
PLC.Elevator/
├── Program.cs                    # Named pipe server, dispatch commands
├── Handlers/
│   ├── AddIpHandler.cs           # netsh interface ip add address
│   ├── RemoveIpHandler.cs        # netsh interface ip delete address
│   ├── AddFirewallRuleHandler.cs # netsh advfirewall firewall add rule
│   ├── RemoveFirewallRuleHandler.cs
│   ├── ListNicsHandler.cs        # Get-NetAdapter via WMI
│   └── CheckPortHandler.cs       # Test if port is already bound
└── Security/
    ├── PipeAuth.cs               # Shared secret verification
    └── AuditLogger.cs            # Log all elevated operations
```

---

## 5. Protocol Stack Architecture

Each protocol follows a layered architecture within the worker:

```
TCP Listener (Socket)
     │
     ▼
Transport Layer  ─── ISO-on-TCP (S7), Encapsulation (ENIP),
                    MBAP (Modbus), AMS/TCP (ADS), MC Frame (MELSEC),
                    OPC UA Binary (UA)
     │
     ▼
Session Layer    ─── Connection tracking, keepalive, concurrent
                     connection limit enforcement
     │
     ▼
Protocol Layer   ─── PDU parsing, validation, error code generation,
                     request routing to tag engine
     │
     ▼
Tag Engine       ─── Tag lookup → read/write → format brand-native response
```

### 5.1 Concurrency Model per Worker

- **Main thread**: IPC with supervisor, graceful shutdown handling
- **I/O threads**: async TCP accept loop (`SocketAsyncEventArgs` / `Task.WhenAll`)
- **Session pool**: one `Task` per connected client, bounded by `maxConnections`
- **Signal timer**: single `PeriodicTimer` tick, drives tag value updates
- **IPC thread**: dedicated named-pipe listener for supervisor commands

No synchronization between sessions for reads (tag store is `ConcurrentDictionary`).
Writes are lock-free via `Interlocked.CompareExchange` for scalars.

### 5.2 Protocol Realism Details

#### S7comm (ISO-on-TCP + COTP + S7 PDU)

| Layer | Implementation |
|-------|---------------|
| **ISO 8073** (TPKT) | 4-byte length header. RFC 1006 compliant. |
| **COTP** | Connection Request (CR) → Connection Confirm (CC) handshake with TSAPs. TSAP → rack/slot: 0x01/0x02 for S7-300 (R0/S2), 0x01/0x01 for S7-1200 (R0/S1). |
| **S7 PDU** | Protocol ID 0x32. PDU header (8 bytes) + Parameter block + Data block. Supported functions: |
| | - **0x04** Read Var (multi-parameter): parse address, return tag value |
| | - **0x05** Write Var (multi-parameter): parse address, write tag |
| | - **0x1C** Request Die: close connection gracefully |
| | - **0x1D** Read SZL: return identity data (order code, serial, firmware) per personality |
| | - **0x1E** En/Disable Subscription: acknowledge (no actual impl) |
| | - **0x1F** Read/Write Var (list): same as 0x04/0x05 but in list form |
| **Address parsing** | DB10.DBW100 → area 0x84 (DB), DB#10, offset 100, size WORD. Also support: M (flags, 0x83), I (inputs, 0x81), Q (outputs, 0x82), PE/PA (peripheral). |
| **SZL IDs responded** | 0x0011 (Module ID / order code), 0x001C (CPU characteristics), 0x0001 (BOOT data), 0x011C (component identification). Each returns data matching personality. |
| **Timing** | Default response latency per personality: S7-300 5ms, S7-400 2ms, S7-1200 8ms, S7-1500 3ms. All ± configurable jitter. |

#### EtherNet/IP + CIP (Explicit Messaging)

| Layer | Implementation |
|-------|---------------|
| **TCP Encapsulation** | Standard ENIP encapsulation header: 24 bytes (Command 0x0065 RegisterSession → 0x0064 → forwardClose, etc.). RegisterSession response carries same session handle. |
| **UDP** | ListIdentity (0x0063) on UDP :44818 responds with Identity object data (vendor 1, product name matching personality, serial). |
| **CIP** | Common Industrial Protocol packet: Response path + Service + General Status + Data. |
| **Services** | - **0x4C** Read Tag (Fragmented): for tag values > 65535 bytes |
| | - **0x4D** Read Tag: standard tag read by symbol name |
| | - **0x4E** Write Tag: write by symbol name |
| | - **0x55** Get Instance Attributes List (browse): returns tag list |
| | - **0x01** Get Attributes All (Identity object) |
| | - **0x03** Set Attribute Single |
| **Tag type mapping** | Logix tag types → CIP type codes: BOOL(0xC1), SINT(0xC2), INT(0xC3), DINT(0xC4), REAL(0xCA), etc. Array tags include array dimension header. |
| **Personality** | ControlLogix 1756-L7x: Vendor=1, DeviceType=14 (Controller), ProductCode=168, Revision=major.minor. CompactLogix 1769-L33ER: ProductCode=85. |

#### Modbus TCP

| Layer | Implementation |
|-------|---------------|
| **MBAP** | 7-byte header: Transaction ID (2) + Protocol ID (0x0000, 2) + Length (2) + Unit ID (1). Unit ID mapped per personality. |
| **Function Codes** | FC 01 (Read Coils), 02 (Read Discrete Inputs), 03 (Read Holding Registers), 04 (Read Input Registers), 05 (Write Single Coil), 06 (Write Single Register), 15 (Write Multiple Coils), 16 (Write Multiple Registers), 22 (Mask Write Register), 23 (Read/Write Multiple Registers), 43/14 (Read Device Identification). |
| **Exception codes** | 01 (ILLEGAL FUNCTION), 02 (ILLEGAL DATA ADDRESS), 03 (ILLEGAL DATA VALUE), 04 (SERVER DEVICE FAILURE), 06 (BUSY). |
| **Device ID (FC 43/14)** | Returns vendor name, product code, revision matching the personality (Generic / Schneider M340). |
| **Word order** | Configurable per address range: ABCD (big-endian), CDAB, BADC, DCBA — stored in tag metadata, applied transparently in response. |
| **Address ranges** | Coils: 00001–09999, Discrete Inputs: 10001–19999, Input Registers: 30001–39999, Holding Registers: 40001–49999. Extended ranges beyond 99999 supported per spec. |

#### MELSEC MC Protocol 3E (Binary)

| Layer | Implementation |
|-------|---------------|
| **Frame** | 3E binary: Subheader (2 bytes: 0x5000 for batch read binary) + Network No (1) + PLC No (1) + Request Destination Module I/O No (2) + Request Destination Module Station No (3) + Request Data Length (2) + Monitoring Timer (2) + Command (2) + Subcommand (2) + Data. |
| **Commands** | 0x0401 (Batch Read in Units of Bits), 0x0403 (Batch Read in Units of Words), 0x1401 (Batch Write in Units of Bits), 0x1403 (Batch Write in Units of Words), 0x0604 (Random Read), 0x1604 (Random Write). |
| **Device codes** | M (0x4D), X (0x58), Y (0x59), B (0x42), D (0x44), W (0x57), R (0x52), ZR (0xEA), etc. |
| **Error codes** | Return proper MC error codes for invalid device/address (e.g., 0xC05C for device designation error). |
| **Model name** | FX5U responds to model name read with "FX5U-32MT/ES". Q03UDE responds with "Q03UDE". |

#### Beckhoff ADS / AMS

| Layer | Implementation |
|-------|---------------|
| **AMS/TCP** | Port 48898: AMS header via TCP stream with 4-byte length prefix. AMS header: 32 bytes (target/source NetID + port, command ID, state flags, length). |
| **Commands** | 0x0001 (ReadDeviceInfo), 0x0002 (Read), 0x0003 (Write), 0x0005 (ReadWrite), 0x0006 (AddNotification), 0x0007 (DeleteNotification), 0x0008 (Notify), 0x000F (ReadState). |
| **Symbol handling** | - **Symbol table**: stored as flat list in worker. Browse by iterating all symbols. |
| | - **Handle-by-name**: 0x0009 (GetHandle) → returns 4-byte handle. |
| | - **Read/Write by handle**: 0x0006 notification subscriptions with cyclic rate. |
| | - **Symbol info**: data type, size, comment accessible via 0x000A (ReleaseHandle), 0x0012 (GetSymbolInfo). |
| **AMS NetID** | Derived from the assigned IP address: `192.168.1.100.1.1` format (IP plus port identifier). |
| **Error codes** | 0x0 (ERR_SUCCESS), 0x700 (ERR_CLIENT_PORT), 0x710 (ERR_SYMBOL_NOT_FOUND), 0x712 (ERR_SYMBOL_VERSION_INVALID), 0x740 (ERR_SERVICE_NOT_SUPPORTED). |
| **Personality** | TwinCAT 3: Device name "TwinCAT 3 PLC", version 3.1.4024.x. TwinCAT 2: "TwinCAT 2 PLC", version 2.11.0.x. |

#### OPC UA

| Layer | Implementation |
|-------|---------------|
| **SDK** | OPC Foundation .NET UA Reference Stack (NuGet `OPCFoundation.NetStandard.Opc.Ua.Server`). |
| **Transport** | opc.tcp binary protocol on port 4840. |
| **Server** | - `ApplicationDescription`: ManufacturerName "Siemens" / "Rockwell Automation" depending on flavor. |
| | - `ProductUri`: matches personality. |
| | - `ApplicationUri`: `urn:{hostname}:{instanceName}:{brand}`. |
| **Address space** | Root → Objects → Simulator → {PLC Name} → {Numeric Types, Boolean Types, String Types, DateTime Types, Array Types, ...} — each containing 5 variables plus optional arrays. |
| **NodeManager** | Custom `INodeManager` exposing tag definitions as `VariableNode` with `DataType` mapped to UA built-in types. |
| **Services** | Browse (no root→objects filtering), Read, Write, Subscribe (publish on data change / cyclic), TranslateBrowsePathsToNodeIds. |
| **EUInformation** | Engineering units per tag (from tag definition `engUnit`) exposed as `AnalogUnit` via `EUInformation`. |
| **Security** | None / Basic128Rsa15 / Basic256 / Basic256Sha256. No user authentication in v1 (anonymous login). |

---

## 6. Signal Generation Architecture

### 6.1 Scheduler

```
┌──────────────────────────────────────────────────────────┐
│               SignalScheduler (per PLC)                   │
│                                                           │
│  Uses a hierarchical timing wheel:                        │
│  - Level 0: 10 ms ticks (256 slots) → 2.56 s cycle       │
│  - Level 1: 100 ms ticks (256 slots) → 25.6 s cycle      │
│  - Level 2: 1 s ticks (256 slots) → 4.27 min cycle       │
│  - Level 3: 10 s ticks (256 slots) → 42.7 min cycle      │
│  - Level 4: 1 min ticks (256 slots) → 4.27 h cycle       │
│                                                           │
│  Each tag registered at its updateMs slot.                │
│  Overhead: O(1) tick, O(1) insert/remove.                │
│  Deterministic: same seed + same config = same sequence. │
└──────────────────────────────────────────────────────────┘
```

### 6.2 Profile Execution

Each profile implements `IProfileGenerator`:

```csharp
interface IProfileGenerator
{
    /// <summary>Compute the next value given elapsed time since epoch.</summary>
    object ComputeValue(long elapsedMs, object? previousValue);
}
```

| Profile | Computation |
|---------|------------|
| **Static** | Returns configured `value` regardless of time. |
| **Step** | `value += direction × step` every `updateMs`. On limit: reverse, wrap, or clamp per `atLimit`. |
| **Ramp** | `value = lowLimit + ((elapsedMs % periodMs) / periodMs) × (highLimit - lowLimit)`. Sawtooth. |
| **Sine** | `amp × sin(2π × elapsedMs / periodMs + phase) + offset`. Noise added via Box-Muller transform if `noise% > 0`. |
| **Square** | `value = (elapsedMs % periodMs < periodMs × duty%) ? highLimit : lowLimit`. |
| **Triangle** | `value = lowLimit + (2 × abs((elapsedMs / periodMs - floor(elapsedMs / periodMs + 0.5)))) × (highLimit - lowLimit)`. |
| **Random** | New value from uniform/normal distribution every `updateMs`. PRNG with optional seed. |
| **Toggle** | `boolValue = (elapsedMs / intervalMs) % 2 == 0`. |
| **Pulse** | `boolValue = (elapsedMs % periodMs) < periodMs × duty%`. |
| **Counter** | `value += step` each tick; `value %= rolloverAt` if set. |
| **Clock** | Formats current system time per `format` (e.g., "HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss"). |
| **TextCycle** | `value = values[(elapsedMs / intervalMs) % values.Length]`. |
| **Echo** | Returns last written value (initial `value` at startup). |

### 6.3 Write Policy

When a client writes to a tag:

1. Protocol handler receives write request → validates data type → applies value.
2. Tag value is immediately updated in `TagValueStore`.
3. If profile is `Echo` or `Static`: value persists until next write.
4. If profile is any generator: configurable policy:
   - **Override** (default): value is overwritten on the next generator tick.
   - **Pause**: generator stops for this tag; value held until unpaused from UI.
5. Write is logged in traffic monitor with timestamp, client IP, old value, new value.

---

## 7. Networking Architecture

### 7.1 IP Address Management (IPAM)

```
┌─────────────────────────────────────────────┐
│  IpPool                                      │
│                                              │
│  Maintains an in-memory set of:              │
│  - All IPs currently assigned to PLCs        │
│  - All IPs detected on the network (ARP)     │
│  - All IPs reserved by other applications    │
│                                              │
│  Auto-assignment strategy:                   │
│  1. User selects NIC (or loopback)           │
│  2. User defines IP range (e.g., 10.0.0.2    │
│     - 10.0.0.254) or uses suggested subnet   │
│  3. IpPool.SuggestNext(NicSubnet) returns    │
│     the smallest unused address              │
│  4. Before assignment, sends ICMP ping to    │
│     verify address not in use                │
└─────────────────────────────────────────────┘
```

### 7.2 Network Operations Flow

```
UI "Start PLC"
  │
  ▼
FleetService.StartPlc(instanceId)
  │
  ├─► Validate IP not already in use
  │
  ├─► ElevatorClient.AddIpAddress(nicName, ip, subnetMask)
  │     │
  │     ▼
  │   PLC.Elevator: netsh interface ip add address "eth0" 10.0.0.2 255.255.255.0
  │     │
  │     ▼
  │   Wait for IP to be reachable (ARP probe, max 3 retries)
  │
  ├─► ElevatorClient.AddFirewallRule(protocol, port, ip)
  │     │
  │     ▼
  │   PLC.Elevator: netsh advfirewall firewall add rule ...
  │
  ├─► PlcProcessManager.StartWorker(instanceId, config)
  │     │
  │     ▼
  │   Process.Start("PLC.Worker.exe", "--instance", id, "--pipe", pipeName)
  │     │
  │     ▼
  │   Worker connects to supervisor via named pipe
  │     │
  │     ▼
  │   Supervisor sends full config snapshot over pipe
  │     │
  │     ▼
  │   Worker starts TCP listener on configured IP:port
  │
  └─► Update PlcState → Running
      Broadcast via SignalR FleetHub
```

### 7.3 Stop/Delete Flow

```
UI "Stop PLC"
  │
  ▼
FleetService.StopPlc(instanceId)
  │
  ├─► Send IPC Stop command to worker (graceful: 5s timeout)
  │
  ├─► Worker: close all sessions → stop TCP listener → exit
  │
  ├─► If worker does not exit: Process.Kill(true)
  │
  ├─► ElevatorClient.RemoveIpAddress(nicName, ip) [if configured to remove on stop]
  │
  └─► Update PlcState → Stopped
```

### 7.4 Port Conflict Detection

On supervisor startup and before starting any PLC:
1. Enumerate all TCP listeners via `IPGlobalProperties.GetActiveTcpListeners()`.
2. Compare against configured PLC ports (including default per brand).
3. If conflict detected: log warning, display in UI with "conflict" badge.
4. Check Windows excluded port range: `netsh int ipv4 show excludedportrange tcp`.
5. If a port falls in an excluded range: display warning with guidance (restart winnat service or pick another port).

---

## 8. Data Persistence

### 8.1 File Layout

```
%APPDATA%\PLC Simulator\
├── config.json                          # Global app settings (NIC, theme, window)
├── fleet\
│   ├── MyS7Plc.json                     # One JSON file per PLC instance
│   ├── MyModbusPlc.json
│   └── ...
├── templates\
│   ├── s7.json                          # Default templates (editable)
│   ├── rockwell.json
│   ├── modbus.json
│   ├── melsec.json
│   ├── ads.json
│   └── opcua.json
├── export\                              # Export output directory
├── logs\
│   ├── supervisor-.log                  # Serilog rolling file
│   └── worker-{instanceId}-.log         # Per-worker logs
└── state.json                           # Which PLCs were running → restore on startup
```

### 8.2 Atomic Write Pattern

```csharp
void SavePlcConfig(PlcInstance plc)
{
    var tmp = Path.Combine(tempDir, $"{plc.Id}.json.tmp");
    var final = Path.Combine(configDir, "fleet", $"{plc.Id}.json");
    File.WriteAllText(tmp, Serialize(plc));
    File.Move(tmp, final, overwrite: true); // Atomic on NTFS
}
```

### 8.3 Config Versioning

Each JSON file carries a `"$schemaVersion": "1.0"` field. On startup, if a file has an older version, a migration function runs. This allows forward compatibility.

---

## 9. Web UI Architecture

### 9.1 Page Structure

```
/                              → Fleet Dashboard (default)
/plcs/:id                      → PLC Detail (tag grid, sparklines)
/plcs/:id/traffic              → Traffic Monitor
/plcs/:id/trends               → Trends (strip chart)
/plcs/:id/tags/:tagId/edit     → Tag Editor (profile config + preview)
/plcs/new                      → New PLC Wizard (brand → personality → IP → template)
/network                       → Network Settings
/export                        → Export/Import
/system                        → System Settings
```

### 9.2 Real-Time Data Flow

```
┌──────────┐   SignalR ── FleetHub ──►  ┌──────────┐
│  Worker  │   ◄──── Tag updates ────    │   UI     │
│  Process │   ──── Connection stats ──► │ (React)  │
│  (x N)   │   ◄──── PLC control ─────   │          │
└──────────┘                             └──────────┘
       │
       │ Named Pipe
       ▼
┌──────────┐   SignalR
│Supervisor├──────────────► FleetHub ──► All clients
└──────────┘               TrafficHub ──► Per-PLC event stream
```

Tag updates are batched at the supervisor: per-PLC tag values are collected every 100ms (default) from the worker and pushed via SignalR. The UI uses React.memo + virtualization to handle 1000+ tags.

### 9.3 UI Components (Directory)

```
src/
├── components/
│   ├── layout/
│   │   ├── AppShell.tsx          # Sidebar + header + main content
│   │   ├── Sidebar.tsx           # PLC list with status dots
│   │   └── ThemeToggle.tsx       # Dark/light switch
│   ├── fleet/
│   │   ├── FleetGrid.tsx         # Card grid of all PLCs
│   │   ├── PlcCard.tsx           # Single PLC card with badge
│   │   ├── FleetToolbar.tsx      # Fleet-wide start/stop, filter
│   │   └── PlcStateBadge.tsx     # Running/stopped/error indicator
│   ├── plc-detail/
│   │   ├── TagTable.tsx          # Virtualized tag grid
│   │   ├── TagRow.tsx            # Single row: name, address, type, value, sparkline
│   │   ├── Sparkline.tsx         # Mini chart
│   │   ├── TagFilter.tsx         # Filter by type, profile, access
│   │   └── AddTagModal.tsx       # Modal to add tag
│   ├── tag-editor/
│   │   ├── ProfileForm.tsx       # Dynamic form based on profile type
│   │   ├── ProfilePreview.tsx    # Live waveform preview (ECharts)
│   │   ├── DataTypeSelect.tsx    # Brand-filtered type picker
│   │   └── AddressInput.tsx      # Brand-validated address with autocomplete
│   ├── wizard/
│   │   ├── StepBrand.tsx         # Select brand
│   │   ├── StepPersonality.tsx   # Select model
│   │   ├── StepNetwork.tsx       # IP assignment (auto/manual)
│   │   ├── StepTemplate.tsx      # Template selection/preview
│   │   ├── StepReview.tsx        # Summary + create
│   │   └── WizardStepper.tsx     # Progress indicator
│   ├── traffic/
│   │   ├── ConnectionTable.tsx   # Active connections
│   │   ├── TrafficLog.tsx        # Rolling event log (virtualized)
│   │   └── ProtocolCounters.tsx  # Per-protocol request/response counters
│   ├── trends/
│   │   ├── TrendChart.tsx        # Multi-series strip chart
│   │   └── TrendToolbar.tsx      # Tag selector, time range
│   ├── network/
│   │   ├── NicSelector.tsx       # NIC list with status
│   │   ├── IpPoolTable.tsx       # Assigned IPs
│   │   └── HealthCheckPanel.tsx  # Alias + firewall status
│   ├── export/
│   │   ├── ExportForm.tsx        # Format, scope, options
│   │   ├── ImportDropZone.tsx    # Drag-drop file validation
│   │   └── ImportPreview.tsx     # Preview before importing
│   └── system/
│       ├── ElevationStatus.tsx   # Admin privilege indicator
│       ├── PortConflictTable.tsx # Conflicts found
│       └── LogViewer.tsx         # App log viewer
```

---

## 10. Export/Import Engine

### 10.1 CSV Format (Kepware-Compatible)

```
Name,Address,DataType,ScanRate,Protocol,Description,ClientAccess,Enabled
Temp,DB10.DBW100,Word,1000,s7,Oven Temp,ReadOnly,1
```

| CSV Field | Mapping |
|-----------|---------|
| Name | `tag.name` |
| Address | `tag.address` (brand-native syntax) |
| DataType | Kepware vocabulary (Word, DWord, Float, Boolean, String, etc.) per brand driver |
| ScanRate | `tag.simulation.updateMs` (ms) |
| Protocol | `instance.brand` → driver ID (s7, rockwell, modbus-tcp, melsec, ads, opcua) |
| Description | `tag.description` |
| ClientAccess | `tag.access` (ReadOnly / ReadWrite) |
| Enabled | `tag.enabled` (1/0) |

### 10.2 JSON Format (Round-Trip)

```json
{
  "$schemaVersion": "1.0",
  "instance": {
    "name": "S7-1200-PLC",
    "brand": "siemens",
    "personality": "s7-1200",
    "description": "Main conveyor line PLC"
  },
  "network": {
    "nic": "Ethernet0",
    "ipAddress": "10.0.0.2",
    "subnetMask": "255.255.255.0",
    "port": 102,
    "maxConnections": 8
  },
  "behavior": {
    "baseLatencyMs": 8,
    "jitterMs": 2,
    "scanCycleMs": 10,
    "faultInjection": null
  },
  "tags": [
    {
      "name": "ConveyorSpeed",
      "address": "DB1.DBD0",
      "dataType": "Float32",
      "access": "ReadOnly",
      "description": "Conveyor belt speed",
      "engUnit": "m/s",
      "enabled": true,
      "simulation": {
        "profile": "sine",
        "lowLimit": 0.5,
        "highLimit": 2.5,
        "periodMs": 10000,
        "updateMs": 200,
        "noisePercent": 2
      }
    }
  ]
}
```

### 10.3 XLSX Export

Generated via ClosedXML. Columns: same as CSV but with one sheet per PLC. Additional sheet "Overview" with fleet summary (PLC name, brand, IP, tag count, running state). Formatted with auto-sized columns, header styling.

### 10.4 Import Validation Pipeline

```
Upload file
  │
  ▼
Detect format (by extension / content sniffing)
  │
  ├─► CSV → Parse with CsvHelper → Validate each row
  │         │
  │         ├─► Row valid? → Add to import list
  │         └─► Row invalid? → Collect error with line number
  │
  ├─► JSON → Deserialize → Validate against schema
  │         │
  │         ├─► Valid → Show preview (PLCs + tags)
  │         └─► Invalid → Show schema errors
  │
  └─► XLSX → ClosedXML read → Convert to internal model → Validate
         │
         └─► (Import from XLSX is not supported; export only)
  │
  ▼
Show Import Preview (React table with accept/reject per row)
  │
  ▼
User confirms → ImportService merges into fleet
  │ Conflict resolution:
  │   - New PLC → create
  │   - Existing PLC name → skip / overwrite / rename (user choice)
  │   - Same tag name within PLC → merge / skip / overwrite
```

---

## 11. Logging & Observability

### 11.1 Log Hierarchy

| Logger | Output | Retention |
|--------|--------|-----------|
| `PLC.Supervisor` | `logs/supervisor-.log` (Serilog rolling file) | 30 days |
| `PLC.Worker.{instanceId}` | `logs/worker-{id}-.log` | 7 days (small) |
| `PLC.Elevator` | Windows Event Log (Application) | OS-managed |
| Traffic log (ring buffer) | In-memory, last 10 000 events per PLC | Lost on restart (deemed non-critical) |
| Protocol debug log | File per protocol type, toggleable at runtime | Manual |

### 11.2 Structured Logging Fields

All log events include:
- `@timestamp`, `@level`, `@message`, `@source` (supervisor/worker/elevator)
- `PlcInstanceId`, `PlcName`, `Brand`
- `Protocol`, `ClientIp` (where applicable)
- Exception details with stack trace

---

## 12. Security Considerations

| Area | Approach |
|------|---------|
| **Elevated helper** | Runs only when needed; pipe auth via GUID token; all commands audited |
| **Named pipes** | Accessible only to processes running as same Windows user (default pipe security) |
| **Web UI** | Bound to `127.0.0.1` by default (localhost-only). Optional LAN access via config toggle, with warning in UI |
| **OPC UA** | No user auth in v1; anonymous login only |
| **Configuration files** | No secrets stored (no passwords) |
| **CSV injection** | All values escaped; no formula execution on import |
| **DoS prevention** | Per-worker connection limits, configurable request rate throttle, read/write size limits |
| **Input validation** | All user input validated with zod schemas (frontend) + FluentValidation (backend) |

---

## 13. Testing Architecture

### 13.1 Test Levels

| Level | Scope | Tool |
|-------|-------|------|
| **Unit** | Individual profile generators, address parsers, protocol frame builders, tag store | xUnit + Moq |
| **Protocol unit** | Protocol request/response round-trip without TCP | xUnit (in-memory byte streams) |
| **Integration** | Each protocol against a real TCP listener in same process | xUnit + TCP loopback |
| **System** | Full supervisor + worker + elevated helper (mocked elevation) | xUnit + Process |
| **Acceptance** | Against NexEdgeLogger driver from another machine (manual / CI) | Manual + Wireshark |
| **Performance** | 20 PLCs × 1000 tags, sustained 100ms update interval, 10 clients per PLC | BenchmarkDotNet + k6 |

### 13.2 Protocol Test Vectors

Each protocol has a corpus of test vectors:
- Wireshark `.pcapng` captures of real PLC traffic (where available)
- Known-good request → response pairs
- Error scenarios: invalid address, wrong type, out-of-range, too many connections
- Edge cases: zero-length requests, fragmentation, concurrent requests

---

## 14. Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Process isolation per PLC** | Superior to in-process isolation because a crash in any protocol stack cannot take down the supervisor or other PLCs. Enables per-worker resource limits via Windows Job Objects. |
| **Named Pipes for IPC** | Lower latency than TCP loopback; no port conflicts; built-in Windows security; supports bidirectional streaming. |
| **Signal generation in supervisor** vs workers | Profiles run in the supervisor and computed values are pushed to workers. This keeps workers simple (they only serve protocol requests) and makes signal generation independent of protocol processing. Trade-off: network traffic for tag values. If latency becomes an issue, signal generation can be moved into workers. |
| **Custom protocol implementations** for S7, CIP, MC, ADS | No maintained open-source **server** libraries exist for these protocols. All client libraries implement the client side only. Writing against the wire spec (Wireshark dissectors, open-source client code as reference) is the only option. |
| **OPC UA via OPC Foundation SDK** | This is the one protocol with a solid, free server SDK. Using it avoids reimplementing UA Binary encoding, security, session management, and subscriptions — which are enormously complex. |
| **Elevated helper pattern** | Keeps 99% of the app running without admin rights. Elevation is scoped to a small, auditable binary. If elevation not available, the app degrades gracefully (loopback mode). |
| **Web UI** over native GUI | Cross-technology access (LAN from another machine), easier development (React ecosystem), consistent look across Windows/Mac/Linux (future), modern component libraries. |
| **Hierarchical timing wheel** for signal scheduling | O(1) per tick regardless of tag count. Scales to 1000+ tags with diverse update intervals. Deterministic when seeded. |
| **Kepware-style CSV** | Direct import into KEPServerEX and NexEdgeLogger without editing. This was the explicit requirement. The CSV row format maps directly to their OPC/DDE server import. |
| **Config versioning via `$schemaVersion`** | Allows forward migration as the data model evolves. Critical for a long-lived simulation tool. |

---

## 15. File/Process Layout (Build Output)

```
PLC Simulator/
├── PLC.Supervisor.exe              # Main application (ASP.NET Core)
├── PLC.Worker.exe                  # Worker binary (Native AOT)
├── PLC.Elevator.exe                # Elevated helper (Native AOT, manifest: requireAdmin)
├── wwwroot/                        # Built React SPA
│   ├── index.html
│   ├── assets/
│   │   ├── main-abc123.js
│   │   └── main-abc123.css
│   └── favicon.ico
├── templates/                      # Default brand templates (shipped)
│   ├── s7.json
│   ├── rockwell.json
│   ├── modbus.json
│   ├── melsec.json
│   ├── ads.json
│   └── opcua.json
├── config.json                     # Default config (created on first run)
└── data/                           # Config storage (created on first run)
    ├── fleet/
    └── logs/
```

---

## 16. Solution Structure (Source)

```
PLC-Simulator.sln
├── src/
│   ├── PLC.Supervisor/             # ASP.NET Core project
│   ├── PLC.Worker/                 # Worker (Native AOT publish)
│   ├── PLC.Elevator/               # Elevated helper (Native AOT, Windows-specific)
│   ├── PLC.Shared/                 # Shared models, interfaces, DTOs (class library)
│   │   ├── Models/
│   │   ├── Protocols/              # Protocol enums, address types, data types
│   │   ├── Ipc/                    # Named pipe message DTOs
│   │   └── SignalProfiles/         # Profile parameter classes
│   ├── PLC.Protocols.S7/           # S7 protocol implementation (library)
│   ├── PLC.Protocols.Rockwell/     # CIP/ENIP protocol implementation
│   ├── PLC.Protocols.Modbus/       # Modbus protocol implementation
│   ├── PLC.Protocols.Melsec/       # MC 3E protocol implementation
│   ├── PLC.Protocols.Ads/          # ADS protocol implementation
│   ├── PLC.Protocols.OpcUa/        # OPC UA wrapper library
│   └── PLC.Testing/                # Test project
├── frontend/
│   ├── src/
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig.json
└── docs/
    ├── 01-SPECIFICATION.md
    ├── 02-ARCHITECTURE.md
    ├── 03-TECHNICAL-SPEC.md
    ├── 04-IMPLEMENTATION-PLAN.md
    └── protocol-vectors/           # Wireshark pcaps, test data
```

---

## 17. Acronyms and Glossary

| Term | Definition |
|------|-----------|
| ADS | Automation Device Specification (Beckhoff TwinCAT protocol) |
| AMS | Automation Message Specification (Beckhoff routing layer) |
| CIP | Common Industrial Protocol (Rockwell/ODVA) |
| COTP | Connection-Oriented Transport Protocol (ISO 8073) |
| DB | Data Block (S7 memory area) |
| ENIP | EtherNet/IP encapsulation layer |
| FC | Function Code (Modbus) |
| IPC | Inter-Process Communication |
| MBAP | Modbus Application Protocol header |
| MC 3E | Mitsubishi Communication protocol (3E frame = binary) |
| NIC | Network Interface Card |
| PDU | Protocol Data Unit |
| SLMP | Seamless Message Protocol (Mitsubishi, same as MC 3E) |
| SZL | System State List (S7 diagnostic data) |
| TPKT | ISO Transport Service on top of TCP (RFC 1006) |
| TSAP | Transport Service Access Point (S7 COTP) |
