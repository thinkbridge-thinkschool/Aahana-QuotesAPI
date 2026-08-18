-- Day 8: Covering Indexes + Included Columns

-- BEFORE:
-- The query needs Author, QuoteText, and Category.
-- An index on Author alone may require a Key Lookup
-- to retrieve the other columns.

SET STATISTICS IO ON;

SELECT
    Author,
    QuoteText,
    Category
FROM QuotePerformance
WHERE Author = 'Author 500';

SET STATISTICS IO OFF;


-- COVERING INDEX:
-- Author is the index key.
-- QuoteText and Category are included columns.
-- This allows the query to obtain all requested
-- columns directly from the index.

CREATE NONCLUSTERED INDEX IX_QuotePerformance_Author_Covering
ON QuotePerformance(Author)
INCLUDE (QuoteText, Category);


-- AFTER:
-- Run the same query again.
-- In SQL Server, the Key Lookup should disappear
-- if the optimizer chooses the covering index.

SET STATISTICS IO ON;

SELECT
    Author,
    QuoteText,
    Category
FROM QuotePerformance
WHERE Author = 'Author 500';

SET STATISTICS IO OFF;


-- Expected plan concept:
-- BEFORE: Index Seek + Key Lookup
-- AFTER:  Index Seek without Key Lookup
--
-- Actual execution plans and logical-read delta
-- must be captured in SQL Server.
-- They are not fabricated in this submission.
