# Vmic wire protocol (v1)

All multi-byte integers are **little-endian**. Three ports are used:

| Port | Proto | Purpose |
|------|-------|---------|
| `5800` | UDP | Discovery (client broadcasts, host replies unicast) |
| `5801` | TCP | Control (handshake, keepalive, disconnect) |
| `5802` | UDP | Audio (client → host) |

## Common header (16 bytes)

Every message — TCP and UDP alike — starts with this header:

```
offset  size  field
0       2     magic       = 0x564D  ("VM")
2       1     version     = 0x01
3       1     msg_type    (see table)
4       4     session_id  (host-assigned; 0 before assignment)
8       4     sequence    (per-session; wrap-safe comparisons)
12      4     payload_len (bytes following the header)
```

## Message types

| type | name | direction | payload |
|------|------|-----------|---------|
| `0x01` | `DISCOVER_REQ` | client → broadcast | `utf8(client_name)` |
| `0x02` | `DISCOVER_RESP` | host → client | `utf8(host_name) + u16(control_port)` |
| `0x10` | `CONNECT_REQ` | client → host (TCP) | `utf8(client_name)` |
| `0x11` | `CONNECT_ACK` | host → client (TCP) | *(empty; `session_id` in header)* |
| `0x12` | `CONNECT_REJ` | host → client (TCP) | `utf8(reason)` |
| `0x20` | `KEEPALIVE` | both (TCP) | *(empty)* |
| `0x30` | `AUDIO_DATA` | client → host (UDP) | `u64(send_ts_us) + pcm16[960]` |
| `0xFF` | `DISCONNECT` | both | *(empty)* |

Strings are length-prefixed: `u16(byte_len) + utf8 bytes`.

## Audio framing

- One frame = **10 ms** of **48 kHz mono 16-bit PCM** = **480 samples = 960 bytes**.
- `AUDIO_DATA` payload = `send_ts_us` (8 bytes, diagnostics) + the 960-byte frame.
- Total datagram ≈ 16 (header) + 8 + 960 = **984 bytes** — below the Ethernet MTU,
  so no fragmentation.
- `sequence` increments per frame. The host's jitter buffer reorders, drops
  duplicates/late frames, and conceals losses (see ARCHITECTURE).

## Flows

### Discovery
```
client ──DISCOVER_REQ──► 255.255.255.255:5800      (every 2 s while scanning)
host   ──DISCOVER_RESP─► client:5800 (unicast)     {host_name, control_port=5801}
```

### Connect / stream / disconnect
```
client ──TCP connect──► host:5801
client ──CONNECT_REQ──► host
host   ──CONNECT_ACK──► client        (header.session_id = N)
client ──AUDIO_DATA───► host:5802     (header.session_id = N, sequence++)
       ... KEEPALIVE every 5 s both ways ...
client ──DISCONNECT───► host          (or TCP close)
```

## Timeouts & failure handling

- **Keepalive** every 5 s; no TCP traffic for 15 s ⇒ host drops the client.
- No `AUDIO_DATA` for 3 s ⇒ host marks the client's audio as lost (jitter buffer
  emits concealment/silence); control still alive until the 15 s timeout.
- Client: no `CONNECT_ACK` within 5 s ⇒ connection fails.
- v1 has **no FEC and no retransmission** — losses are concealed at the receiver.

## Reserved for later

`LEVEL_UPDATE`, per-client `GAIN_CMD` / `MUTE_CMD`, and a `LEVEL` telemetry type
are reserved but not implemented in v1.
