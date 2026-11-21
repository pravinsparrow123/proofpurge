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

-- Seed Sample Data:
-- Insert 500 Accounts (250 active, 250 closed and older than 3 years)
SET NOCOUNT ON;

DECLARE @i INT = 1;
WHILE @i <= 500
BEGIN
    DECLARE @Status NVARCHAR(20) = CASE WHEN @i % 2 = 0 THEN 'CLOSED' ELSE 'ACTIVE' END;
    DECLARE @ActivityDate DATETIME2 = CASE WHEN @i % 2 = 0 THEN DATEADD(year, -4, SYSUTCDATETIME()) ELSE SYSUTCDATETIME() END;
    
    INSERT INTO dbo.Accounts (AccountName, Status, LastActivityDate)
    VALUES (CONCAT('Customer_', @i), @Status, @ActivityDate);

    DECLARE @AccId INT = SCOPE_IDENTITY();

    -- Add Audit Logs
    INSERT INTO dbo.AuditLogs (AccountId, EventAction, EventTimestamp)
    VALUES (@AccId, 'ACCOUNT_CREATED', @ActivityDate);

    -- Add 2 Orders per account
    DECLARE @o INT = 1;
    WHILE @o <= 2
    BEGIN
        INSERT INTO dbo.Orders (AccountId, OrderNumber, TotalAmount, CreatedAt)
        VALUES (@AccId, CONCAT('ORD-', @AccId, '-', @o), 150.00 * @o, @ActivityDate);

        DECLARE @OrdId INT = SCOPE_IDENTITY();

        -- Add Order Items
        INSERT INTO dbo.OrderItems (OrderId, ProductName, Quantity, UnitPrice)
        VALUES (@OrdId, 'Enterprise Widget', 2, 75.00);

        -- Add Shipment
        INSERT INTO dbo.Shipments (OrderId, TrackingCode, ShippedDate)
        VALUES (@OrdId, CONCAT('TRK-', @OrdId), @ActivityDate);

        SET @o = @o + 1;
    END

    SET @i = @i + 1;
END
GO

