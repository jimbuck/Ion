# Stage 6b: networking module benchmarks (September 2026)

`Ion.Benchmarks`, `--job short` (3 iterations), .NET 10.0.12 JIT, Intel Xeon 2.1 GHz VM (4 cores, noisy: treat the
numbers as orders of magnitude). Every row allocates 0 bytes.

```sh
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*Network*' --job short
```

## Snapshot capture (`NetworkSnapshotCaptureBenchmarks`)

One server tick with nothing moving: the tick scope's begin and end, i.e. the capture of two replicated components
(`NetBenchPosition`: a `Vector2` and a float; `NetBenchHealth`: two ints) of every networked entity into the ring.

| Entities | Mean |
|---:|---:|
| 1,000 | 10.7 us |
| 10,000 | 156 us |

## Delta encoding (`NetworkDeltaBenchmarks`)

| Method | Moving | Mean |
|---|---:|---:|
| `EncodeDelta10k` (generated `WriteDelta`, 10,000 values, one member changed each) | | 42.7 us |
| `DecodeDelta10k` (generated `ReadDelta` of the same) | | 42.4 us |
| `SnapshotRoundTrip10k`: one frame of a server and a client on the loopback transport with 10,000 networked entities (capture, delta against the acknowledged tick, packets, decode, apply to the client world) | 1 % | 300 us |
| same | 10 % | 424 us |

The round trip was 0.70 ms and 0.81 ms before the encoder and the client learned to find the changed slots with a
vectorized pass per column (presence word and bytes per 64-slot block, then per slot in the blocks that differ). Before
that, at 10,000 entities the full snapshot needed more than the 256 parts the first protocol allowed and was truncated (it
now allows 4,096 parts and flags a snapshot that still does not fit, which the client drops).

## Messages (`NetworkMessageBenchmarks`)

| Method | Mean |
|---|---:|
| `Dispatch100`: 100 client-to-server messages per frame, serialized, packed, sent through the loopback, decoded with the authority and rate checks, emitted on the event bus and read with a `NetworkReader<T>` (includes both sessions' frame) | 4.9 us |
