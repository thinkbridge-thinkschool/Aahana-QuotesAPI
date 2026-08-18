-- Day 8: Clustered vs Non-Clustered Indexes

DROP TABLE IF EXISTS QuotePerformance;

CREATE TABLE QuotePerformance
(
    Id INT IDENTITY(1,1) NOT NULL,
    Author NVARCHAR(200) NOT NULL,
    QuoteText NVARCHAR(1000) NOT NULL,
    Category NVARCHAR(50) NOT NULL
);

-- Generate approximately 100,000 rows
;WITH Numbers AS
(
    SELECT TOP (100000)
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a
    CROSS JOIN sys.all_objects b
)
INSERT INTO QuotePerformance (Author, QuoteText, Category)
SELECT
    CONCAT('Author ', n % 1000),
    CONCAT('Quote ', n),
    CASE
        WHEN n % 2 = 0 THEN 'classic'
        ELSE 'modern'
    END
FROM Numbers;

-- BASELINE: no indexes
SET STATISTICS IO ON;

SELECT *
FROM QuotePerformance
WHERE Author = 'Author 500';

SET STATISTICS IO OFF;

-- CLUSTERED INDEX
CREATE CLUSTERED INDEX IX_QuotePerformance_Id
ON QuotePerformance(Id);

SET STATISTICS IO ON;

SELECT *
FROM QuotePerformance
WHERE Id = 50000;

SET STATISTICS IO OFF;

-- NON-CLUSTERED INDEX #1
CREATE NONCLUSTERED INDEX IX_QuotePerformance_Author
ON QuotePerformance(Author);

SET STATISTICS IO ON;

SELECT *
FROM QuotePerformance
WHERE Author = 'Author 500';

SET STATISTICS IO OFF;

-- NON-CLUSTERED INDEX #2
CREATE NONCLUSTERED INDEX IX_QuotePerformance_Category
ON QuotePerformance(Category);

SET STATISTICS IO ON;

SELECT *
FROM QuotePerformance
WHERE Category = 'classic';

SET STATISTICS IO OFF;

-- WRITE-SIDE TEST
INSERT INTO QuotePerformance
(
    Author,
    QuoteText,
    Category
)
VALUES
(
    'Write Test Author',
    'Write-side index maintenance test',
    'modern'
);

-- Cleanup when running in SQL Server:
-- DROP TABLE QuotePerformance;
