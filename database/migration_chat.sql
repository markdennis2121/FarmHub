USE ModernLoginDB;
GO

-- 1. Create the ChatMessages table to hold global persistent history
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ChatMessages' AND xtype='U')
BEGIN
    CREATE TABLE ChatMessages (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        Message NVARCHAR(255) NOT NULL,
        Color NVARCHAR(50) NOT NULL,
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END
GO

-- 2. Add some initial greeting history if empty
IF NOT EXISTS (SELECT TOP 1 * FROM ChatMessages)
BEGIN
    INSERT INTO ChatMessages (Username, Message, Color)
    VALUES ('MasterFarmer', 'Welcome to the persistent history of FarmHub!', '#fde047');
END
GO
