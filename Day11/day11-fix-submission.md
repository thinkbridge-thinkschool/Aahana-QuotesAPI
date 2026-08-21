\# Day 11 — Drop p99 by 10×



\## Before vs After p99



The `/slow` endpoint was measured with the same k6 load configuration: 10 virtual users for 10 seconds.



| Metric | Before | After |

|---|---:|---:|

| p50 | 97.51 ms | 3.11 ms |

| p99 | 709.76 ms | 38.76 ms |

| p95 | 501.46 ms | 17.93 ms |

| Requests | 593 | 14,481 |

| Error rate | 0% | 0% |



The p99 improved from 709.76 ms to 38.76 ms, which is approximately an 18.3× improvement and exceeds the required 10× target.



\## Changes Made



\### 1. Eliminated the N+1 query



The original endpoint loaded the list of authors and then executed one separate query for every author.



This resulted in:



\- 100 authors

\- 1 query to load authors

\- 100 quote queries

\- 101 total SQL queries



The fixed endpoint uses one query to fetch all required quotes instead of querying once per author.



The query count was reduced from 101 to 2.



\### 2. Added an index



Added an index on the `Author` column:



```sql

CREATE INDEX IF NOT EXISTS IX\_Quotes\_Author

ON Quotes(Author);

