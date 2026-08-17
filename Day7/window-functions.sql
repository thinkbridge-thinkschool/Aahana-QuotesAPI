WITH QuoteWindows AS
(
    SELECT
        Id,
        Author,
        Text,

        ROW_NUMBER() OVER (
            PARTITION BY Author
            ORDER BY Id
        ) AS QuoteNumber,

        COUNT(*) OVER (
            PARTITION BY Author
            ORDER BY Id
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS RunningCount,

        LAG(Id) OVER (
            PARTITION BY Author
            ORDER BY Id
        ) AS PreviousQuoteId

    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Author,
    QuoteNumber,
    Text,
    RunningCount,
    PreviousQuoteId
FROM QuoteWindows
ORDER BY Author, QuoteNumber;
