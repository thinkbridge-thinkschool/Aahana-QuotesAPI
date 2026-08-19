-- Day 9: Reproduce and Resolve a Deadlock
-- SQL Server / SSMS
--
-- A classic two-resource deadlock was reproduced using two sessions.
-- SQL Server returned Msg 1205 and selected one transaction as
-- the deadlock victim.
--
-- The deadlock was fixed by using consistent lock ordering
-- in both transactions.


------------------------------------------------------------
-- SETUP
------------------------------------------------------------

USE ThinkSchoolSQL;
GO

DROP TABLE IF EXISTS DeadlockTest;
GO

CREATE TABLE DeadlockTest
(
    Id INT PRIMARY KEY,
    ResourceName NVARCHAR(100) NOT NULL,
    Value INT NOT NULL
);
GO

INSERT INTO DeadlockTest (Id, ResourceName, Value)
VALUES
    (1, 'Resource A', 100),
    (2, 'Resource B', 200);
GO


------------------------------------------------------------
-- DEADLOCK REPRODUCTION
------------------------------------------------------------

-- SESSION 1
-- Locks Resource A first, then requests Resource B.

BEGIN TRANSACTION;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 1;

-- Keep this transaction open.


-- SESSION 2
-- Locks Resource B first, then requests Resource A.

BEGIN TRANSACTION;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 2;

-- Keep this transaction open.


-- Now complete the circular wait:
--
-- SESSION 1:
UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 2;
GO

-- SESSION 2:
UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 1;
GO


------------------------------------------------------------
-- ACTUAL DEADLOCK RESULT
------------------------------------------------------------

-- SQL Server returned:
--
-- Msg 1205, Level 13, State 51
-- Transaction (Process ID 63) was deadlocked on lock resources
-- with another process and has been chosen as the deadlock victim.
-- Rerun the transaction.
--
-- SQL Server detected the circular wait and selected one
-- transaction as the deadlock victim.


------------------------------------------------------------
-- FIX: CONSISTENT LOCK ORDERING
------------------------------------------------------------

-- Both transactions now acquire locks in the same order:
-- Resource A (Id = 1) -> Resource B (Id = 2)


-- SESSION 1

BEGIN TRANSACTION;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 1;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 2;

COMMIT TRANSACTION;
GO


-- SESSION 2

BEGIN TRANSACTION;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 1;

UPDATE DeadlockTest
SET Value = Value + 1
WHERE Id = 2;

COMMIT TRANSACTION;
GO


------------------------------------------------------------
-- WHY THE FIX WORKS
------------------------------------------------------------

-- Both transactions acquire locks in the same order (A -> B),
-- so they cannot form a circular wait and therefore cannot
-- deadlock.


------------------------------------------------------------
-- FINAL CHECK
------------------------------------------------------------

SELECT *
FROM DeadlockTest
ORDER BY Id;
GO