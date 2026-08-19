-- Day 9: Isolation Levels + Read Anomalies
-- SQL Server
--
-- Actual two-session experiments were performed in SSMS.
--
-- RESULTS OBSERVED:
-- Dirty read:
--   READ UNCOMMITTED allowed Session 2 to read uncommitted Balance = 500.
--
-- Non-repeatable read:
--   READ COMMITTED returned 1000 on the first read and 750
--   on the second read after Session 2 updated the row.
--
-- Repeatable read prevention:
--   REPEATABLE READ kept the value at 1000 while Session 2's
--   UPDATE was blocked by the lock.
--
-- Phantom read:
--   Under REPEATABLE READ, Charlie (Balance 1500) appeared
--   in the second range query.
--
-- Serializable prevention:
--   Under SERIALIZABLE, David's qualifying INSERT was blocked.
--   With SET LOCK_TIMEOUT 5000, SQL Server returned:
--   Msg 1222 - Lock request time out period exceeded.
--
-- This demonstrates:
--   Dirty read          -> prevented by READ COMMITTED
--   Non-repeatable read -> prevented by REPEATABLE READ
--   Phantom read        -> prevented by SERIALIZABLE


------------------------------------------------------------
-- SETUP
------------------------------------------------------------

USE ThinkSchoolSQL;
GO

DROP TABLE IF EXISTS IsolationTest;
GO

CREATE TABLE IsolationTest
(
    Id INT PRIMARY KEY,
    AccountName NVARCHAR(100) NOT NULL,
    Balance INT NOT NULL
);
GO

INSERT INTO IsolationTest (Id, AccountName, Balance)
VALUES
    (1, 'Alice', 1000),
    (2, 'Bob', 2000);
GO


------------------------------------------------------------
-- 1. DIRTY READ
------------------------------------------------------------

-- SESSION 1

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
GO

BEGIN TRANSACTION;

UPDATE IsolationTest
SET Balance = 500
WHERE Id = 1;

-- Leave transaction open.


-- SESSION 2

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

SELECT Id, AccountName, Balance
FROM IsolationTest
WHERE Id = 1;
GO

-- ACTUAL RESULT:
-- Session 2 read Alice's Balance = 500 even though
-- Session 1 had not committed.
--
-- DIRTY READ reproduced.


-- SESSION 1 CLEANUP

ROLLBACK TRANSACTION;
GO


------------------------------------------------------------
-- 2. NON-REPEATABLE READ
------------------------------------------------------------

-- SESSION 1

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
GO

BEGIN TRANSACTION;

SELECT Balance
FROM IsolationTest
WHERE Id = 1;
GO

-- FIRST READ:
-- 1000


-- SESSION 2

UPDATE IsolationTest
SET Balance = 750
WHERE Id = 1;
GO


-- SESSION 1

SELECT Balance
FROM IsolationTest
WHERE Id = 1;
GO

-- SECOND READ:
-- 750
--
-- ACTUAL OBSERVATION:
-- 1000 -> 750
--
-- NON-REPEATABLE READ reproduced.


------------------------------------------------------------
-- 3. REPEATABLE READ PREVENTION
------------------------------------------------------------

-- SESSION 1

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
GO

BEGIN TRANSACTION;

SELECT Balance
FROM IsolationTest
WHERE Id = 1;
GO

-- RESULT:
-- 1000


-- SESSION 2

UPDATE IsolationTest
SET Balance = 900
WHERE Id = 1;
GO

-- ACTUAL OBSERVATION:
-- Session 2 remained blocked while Session 1 held
-- the required lock.


-- SESSION 1

SELECT Balance
FROM IsolationTest
WHERE Id = 1;
GO

-- RESULT:
-- 1000
--
-- The value remained unchanged while Session 2 was blocked.


------------------------------------------------------------
-- 4. PHANTOM READ
------------------------------------------------------------

-- Reset the test data before the experiment.

DELETE FROM IsolationTest
WHERE Id IN (3, 4);
GO

UPDATE IsolationTest
SET Balance = 1000
WHERE Id = 1;
GO


-- SESSION 1

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
GO

BEGIN TRANSACTION;

SELECT Id, AccountName, Balance
FROM IsolationTest
WHERE Balance >= 1000;
GO

-- FIRST RESULT:
-- Alice
-- Bob


-- SESSION 2

INSERT INTO IsolationTest (Id, AccountName, Balance)
VALUES (3, 'Charlie', 1500);
GO

-- INSERT COMPLETED.


-- SESSION 1

SELECT Id, AccountName, Balance
FROM IsolationTest
WHERE Balance >= 1000;
GO

-- SECOND RESULT:
-- Alice
-- Bob
-- Charlie
--
-- ACTUAL OBSERVATION:
-- Charlie appeared as a new qualifying row.
--
-- PHANTOM READ reproduced.


------------------------------------------------------------
-- 5. SERIALIZABLE PREVENTS PHANTOM READ
------------------------------------------------------------

-- Reset the test data.

DELETE FROM IsolationTest
WHERE Id IN (3, 4);
GO


-- SESSION 1

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
GO

BEGIN TRANSACTION;

SELECT Id, AccountName, Balance
FROM IsolationTest
WHERE Balance >= 1000;
GO

-- RESULT:
-- Alice
-- Bob


-- SESSION 2

SET LOCK_TIMEOUT 5000;
GO

INSERT INTO IsolationTest (Id, AccountName, Balance)
VALUES (4, 'David', 1800);
GO

-- ACTUAL RESULT:
-- Msg 1222
-- Lock request time out period exceeded.
-- The statement was terminated.
--
-- David's INSERT was blocked because SERIALIZABLE
-- protected the qualifying range.


-- SESSION 1

SELECT Id, AccountName, Balance
FROM IsolationTest
WHERE Balance >= 1000;
GO

-- RESULT:
-- Alice
-- Bob
--
-- David did not appear because his INSERT was blocked.


------------------------------------------------------------
-- ISOLATION LEVEL SUMMARY
------------------------------------------------------------

-- Anomaly             Lowest isolation level preventing it
--
-- Dirty read          READ COMMITTED
-- Non-repeatable read REPEATABLE READ
-- Phantom read        SERIALIZABLE

