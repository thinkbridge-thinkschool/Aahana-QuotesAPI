SELECT DISTINCT Author
FROM Quotes
WHERE IsDeleted = 0

EXCEPT

SELECT DISTINCT q.Author
FROM Quotes q
INNER JOIN CollectionItems ci
    ON q.Id = ci.QuoteId
WHERE q.IsDeleted = 0;


-- Result:
-- No rows returned.
-- Every current non-deleted quote author has at least one quote in a collection.


-- 2. Authors in both the "classic" and "modern" sets
-- Operator: INTERSECT
-- Why: returns values common to both sets.
--
-- Cannot be executed against the current database because
-- there are no classic/modern category or tag sets in the schema.


-- 3. Combined distinct tag list across two categories
-- Operator: UNION
-- Why: combines two result sets and removes duplicates.
--
-- Cannot be executed against the current database because
-- there is no Tags table or category/tag column in the schema.
