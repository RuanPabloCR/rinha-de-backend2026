# Bucketed Exact k-NN with Early-Abort

A high-performance fraud detection API for [Rinha de Backend 2026](https://github.com/zanfranceschi/rinha-de-backend-2026), built with **.NET 10 Native AOT** and an optimized exact k-nearest-neighbor search over 3 million 14-dimensional quantized vectors.

**Score:** Latency-optimized k-NN via bucket pruning, Q15 quantization, memory-mapped I/O, and scalar early-abort distance computation.

---

## Architecture

```
references.json.gz (official dataset, 3M vectors)
        │
        ▼
  DataPProcessor ───► vectors.bin  (3M × 28 bytes = 84 MB)
                      labels.bin   (3M × 1 byte   = 3 MB)
                      buckets.bin  (160 × 16 bytes = 2.5 KB)
        │
        ▼
   Nginx (round-robin, 0.3 CPU · 36 MB)
     ├── api1:8080  (.NET AOT, 0.35 CPU · 157 MB)
     └── api2:8080  (.NET AOT, 0.35 CPU · 157 MB)
```

**Total resources:** 1.0 CPU · 350 MB

---

## Search Algorithm

### Overview

The search converts each transaction into a 14-dimensional normalized vector, determines its bucket, and performs an **exact k-NN** search (Euclidean distance) within the most promising subset of the dataset.

### Bucketization (160 buckets)

Vectors are grouped by 4 boolean traits combined with merchant category risk:

| Bits | Trait | Source |
|------|-------|--------|
| 0 | `hasLastTransaction` | `last_transaction != null` |
| 1 | `isOnline` | `terminal.is_online` |
| 2 | `cardPresent` | `terminal.card_present` |
| 3 | `unknownMerchant` | `merchant.id ∉ customer.known_merchants` |

10 risk bins: `[0.15, 0.20, 0.25, 0.30, 0.35, 0.45, 0.50, 0.75, 0.80, 0.85]`

**Total: 16 × 10 = 160 buckets.** Each vector falls into exactly one bucket based on its traits + nearest risk value.

Within each bucket, vectors are sorted by `amount_vs_avg` to enable positional estimation at query time.

### Query-time Search Flow

```
1. Vectorize request → 14D Q15 vector + baseBucket + riskIndex + amountVsAvg
2. Get bucket range [offset, count)
3. If count ≤ ChunkSize (16384):
     → Full scan of the entire bucket
   Else:
     → Estimate starting chunk from amountVsAvg
     → Radial chunk expansion until TargetMinCandidates (15000)
4. If scanned < TargetMinCandidates:
     → Expand to adjacent risk buckets (closest risk values first)
     → Up to TargetMaxCandidates (30000)
5. For each candidate:
     → Euclidean distance with early-abort
6. Keep top-5 nearest neighbors
7. fraud_score = fraud_count / 5
8. approved = fraud_score < 0.6
```

### Early-Abort Distance

The 14 dimensions are reordered at preprocessing time to place high-variance dimensions first. During search, the squared distance accumulates dimension by dimension and aborts as soon as the running sum exceeds the current 5th-best distance:

```
Order: [5, 6, 2, 7, 8, 9, 10, 11, 12, 0, 1, 3, 4, 13]
       (minutes_since_last_tx, km_from_last_tx, amount_vs_avg, ...)
```

Average dimensions evaluated per candidate: **~2.4 out of 14**.

### Radial Chunk Expansion

For buckets larger than `ChunkSize`, the estimated position is calculated as:

```
position = clamp(amountVsAvg, 0, 1) × (count - 1)
chunkIndex = position / ChunkSize
```

The search expands outward from this chunk index (±1, ±2, ...) until `TargetMinCandidates` is reached or the bucket is exhausted. This exploits the amount_vs_avg sort order, as similar transactions tend to have similar normalized amounts.

### Risk Bucket Fallback

If the primary bucket doesn't yield enough candidates, the search expands to adjacent risk bins (sorted by distance from the query's risk value). This ensures robustness for edge cases without scanning the entire dataset.

---

## Quantization

All vectors are quantized from `float [-1, 1]` to Q15 (`short [-32767, 32767]`):

```csharp
QuantizeQ15(value) = (short)Round(value × 32767f)
```

The sentinel value `-1` (used for `last_transaction: null` in dimensions 5 and 6) maps to `-32767`, preserving the semantic meaning of "no prior transaction."

---

## Data Pipeline

### Preprocessing (`Tools/DataPProcessor`)

Runs during Docker build. Steps:

1. Read `references.json.gz` (3M records, ~16 MB gzipped)
2. Assign each vector to one of 160 buckets
3. Quantize all 14 dimensions to Q15
4. Reorder dimensions for early-abort optimization
5. Sort each bucket by `amount_vs_avg`
6. Write three binary files:
   - `vectors.bin` — 3M × 28 bytes ~ 84 MB
   - `labels.bin` — 3M × 1 byte = 3 MB
   - `buckets.bin` — 160 × 16 bytes ~ 2.5 KB

### Runtime (`SuperDotnet`)

- **Memory-Mapped Files:** Vectors and labels are accessed via MMF for zero-copy reads
- **Warmup:** On startup, every cache line (64B stride) of the vector and label regions is touched, and synthetic queries are run for every bucket to pre-fault bucket metadata and warm the branch predictor

---

## Performance Characteristics

- **Concurrency:** `SemaphoreSlim(2)` — 2 concurrent requests per instance
- **Dataset access:** Memory-mapped files (no heap allocation for vector reads)
- **Distance computation:** Scalar early-abort, ~2.4/14 dimensions evaluated on average
- **Scan strategy:** Positional estimation within sorted buckets → radial expansion → risk bin fallback
- **Memory footprint:** ~90 MB for binary files (vectors + labels + buckets)
- **Warmup cost:** Paid once at startup, ~200ms on target hardware

---

## Build & Run

```bash
# Prerequisites: .NET 10 SDK, Docker

# Preprocess dataset (run once before Docker build)
dotnet run --project Tools/DataPProcessor -c Release

# Run stack
docker compose up
```

The stack exposes port `9999` via Nginx, round-robining between 2 API instances.

---

## Project Structure

```
├── SuperDotnet/              # Main API (.NET 10 AOT)
│   ├── Program.cs            # Entry point, middleware, endpoints
│   ├── Models/               # DTOs
│   │   ├── FraudScoreDtos.cs # Request/response contracts
│   │   └── Normalization.cs  # Normalization constants record
│   └── Services/
│       ├── BucketTable.cs         # Bucket logic, risk bins, quantization
│       ├── DataAcessService.cs    # Memory-mapped file I/O
│       ├── MccRisk.cs             # MCC risk lookup
│       ├── SearchService.cs       # k-NN search engine
│       ├── TransactionVectorizer.cs # Request → quantized vector
│       └── VectorLayout.cs        # Dimension ordering
├── Tools/DataPProcessor/     # Offline dataset preprocessor
├── docker-compose.yml        # Nginx + 2 API instances
├── Dockerfile                # Multi-stage AOT build
└── nginx.conf                # Reverse proxy config
```

---
