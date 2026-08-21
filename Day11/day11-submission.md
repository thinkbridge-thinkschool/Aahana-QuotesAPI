\# Day 11 — Profile a Slow Endpoint



\## Baseline p50/p99



The `/slow` endpoint was load-tested using k6 with 10 virtual users for 10 seconds.



\- Requests: 593

\- p50: 97.51 ms

\- p99: 709.76 ms

\- p95: 501.46 ms

\- Average: 169.45 ms

\- Maximum: 1.06 s

\- Error rate: 0%



\## Offending SQL



The endpoint first loads the distinct authors:



```sql

SELECT DISTINCT Author

FROM Quotes

ORDER BY Author;

