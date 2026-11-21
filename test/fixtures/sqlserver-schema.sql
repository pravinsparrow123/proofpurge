USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'LifecycleDemo')
BEGIN
    CREATE DATABASE LifecycleDemo;
END
GO

USE LifecycleDemo;
GO

-- Cleanup existing tables if any
IF OBJECT_ID('dbo.Shipments', 'U') IS NOT NULL DROP TABLE dbo.Shipments;
IF OBJECT_ID('dbo.OrderItems', 'U') IS NOT NULL DROP TABLE dbo.OrderItems;
IF OBJECT_ID('dbo.Orders', 'U') IS NOT NULL DROP TABLE dbo.Orders;
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NOT NULL DROP TABLE dbo.AuditLogs;
IF OBJECT_ID('dbo.Accounts', 'U') IS NOT NULL DROP TABLE dbo.Accounts;
GO

-- 1. Root Parent: Accounts
CREATE TABLE dbo.Accounts (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    AccountName NVARCHAR(100) NOT NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE', -- ACTIVE, CLOSED, SUSPENDED
    LastActivityDate DATETIME2 NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- 2. Child Level 1: Orders
CREATE TABLE dbo.Orders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    AccountId INT NOT NULL,
    OrderNumber NVARCHAR(50) NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Orders_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(Id)
);
GO

-- Index on Orders.AccountId for fast join/deletion
CREATE NONCLUSTERED INDEX IX_Orders_AccountId ON dbo.Orders(AccountId);
GO

-- 3. Child Level 1: AuditLogs
CREATE TABLE dbo.AuditLogs (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    AccountId INT NOT NULL,
    EventAction NVARCHAR(100) NOT NULL,
    EventTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AuditLogs_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(Id)
);
GO

-- Deliberately leave AuditLogs.AccountId unindexed to verify Missing Index Warning in scanner!

-- 4. Child Level 2: OrderItems
CREATE TABLE dbo.OrderItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT NOT NULL,
    ProductName NVARCHAR(100) NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id)
);
GO
CREATE NONCLUSTERED INDEX IX_OrderItems_OrderId ON dbo.OrderItems(OrderId);
GO

-- 5. Child Level 2: Shipments
CREATE TABLE dbo.Shipments (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT NOT NULL,
    TrackingCode NVARCHAR(100) NOT NULL,
    ShippedDate DATETIME2 NULL,
    CONSTRAINT FK_Shipments_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id)
);
GO
CREATE NONCLUSTERED INDEX IX_Shipments_OrderId ON dbo.Shipments(OrderId);
GO

-- High-Throughput Set-Based Data Seeder (Generates 40,000+ total rows, 20,000+ purge candidates)
SET NOCOUNT ON;

-- Clear tables in reverse topological order
DELETE FROM dbo.OrderItems;
DELETE FROM dbo.Shipments;
DELETE FROM dbo.Orders;
DELETE FROM dbo.AuditLogs;
DELETE FROM dbo.Accounts;

DBCC CHECKIDENT ('dbo.Accounts', RESEED, 0);
DBCC CHECKIDENT ('dbo.Orders', RESEED, 0);
DBCC CHECKIDENT ('dbo.OrderItems', RESEED, 0);
DBCC CHECKIDENT ('dbo.Shipments', RESEED, 0);
DBCC CHECKIDENT ('dbo.AuditLogs', RESEED, 0);

-- Generate 5,000 Accounts (2,500 CLOSED older than 3 years, 2,500 ACTIVE)
;WITH Digits AS (
    SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS v(n)
),
Numbers AS (
    SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Num
    FROM Digits d1 CROSS JOIN Digits d2 CROSS JOIN Digits d3 CROSS JOIN Digits d4
)
INSERT INTO dbo.Accounts (AccountName, Status, LastActivityDate)
SELECT 
    CONCAT('Customer_', Num),
    CASE WHEN Num % 2 = 0 THEN 'CLOSED' ELSE 'ACTIVE' END,
    CASE WHEN Num % 2 = 0 THEN DATEADD(year, -4, SYSUTCDATETIME()) ELSE SYSUTCDATETIME() END
FROM Numbers
WHERE Num <= 5000;

-- 5,000 AuditLogs (2,500 purge candidates)
INSERT INTO dbo.AuditLogs (AccountId, EventAction, EventTimestamp)
SELECT 
    Id,
    'ACCOUNT_CREATED',
    LastActivityDate
FROM dbo.Accounts;

-- 10,000 Orders (2 per account = 5,000 purge candidates)
INSERT INTO dbo.Orders (AccountId, OrderNumber, TotalAmount, CreatedAt)
SELECT 
    a.Id,
    CONCAT('ORD-', a.Id, '-', o.OrderSeq),
    150.00 * o.OrderSeq,
    a.LastActivityDate
FROM dbo.Accounts a
CROSS JOIN (VALUES (1), (2)) AS o(OrderSeq);

-- 10,000 OrderItems (1 per order = 5,000 purge candidates)
INSERT INTO dbo.OrderItems (OrderId, ProductName, Quantity, UnitPrice)
SELECT 
    Id,
    'Enterprise Cloud Platform License',
    2,
    75.00
FROM dbo.Orders;

-- 10,000 Shipments (1 per order = 5,000 purge candidates)
INSERT INTO dbo.Shipments (OrderId, TrackingCode, ShippedDate)
SELECT 
    Id,
    CONCAT('TRK-', Id),
    CreatedAt
FROM dbo.Orders;
GO

