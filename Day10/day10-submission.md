\# Day 10 — EF Core Change Tracker + AsNoTracking



\## Change Tracker / Identity Resolution



The tracked query returned the same entity instance when the same entity was queried again:



\- Same instance with tracking: `True`

\- Tracked entities: `1`

\- AsNoTracking entity tracked: `False`



This demonstrates that EF Core's change tracker keeps track of queried entities and provides identity resolution for tracked queries. `AsNoTracking()` skips change tracking for read-only queries.



\## 10,000-Row Benchmark



| Query | Rows | Time | Allocated |

|---|---:|---:|---:|

| With Tracking | 10,000 | 118 ms | 9,637,432 bytes |

| AsNoTracking | 10,000 | 34 ms | 3,759,544 bytes |



\### Query variants



Tracked:



```csharp

await db.Quotes.ToListAsync();

