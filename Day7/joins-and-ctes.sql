WITH RankedQuotes AS
(
    SELECT
        Id,
        Author,
        Text,
        ROW_NUMBER() OVER
        (
            PARTITION BY Author
            ORDER BY Id DESC
        ) AS rn
    FROM Quotes
    WHERE IsDeleted = 0
),
AuthorStats AS
(
    SELECT
        Author,
        COUNT(*) AS QuoteCount
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT
    s.Author,
    s.QuoteCount,
    r.Text AS MostRecentQuote
FROM AuthorStats s
INNER JOIN RankedQuotes r
    ON s.Author = r.Author
   AND r.rn = 1
ORDER BY s.QuoteCount DESC
LIMIT 10;
