-- 1. Create the database
CREATE DATABASE ModernLoginDB;
GO

USE ModernLoginDB;
GO

-- 2. Create the Users table
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Email NVARCHAR(255) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME DEFAULT GETDATE()
);
GO

-- 3. Insert a demo user to test the login with
-- The password for this hash is: password123
INSERT INTO Users (Email, PasswordHash) 
VALUES ('demo@example.com', '$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy');
GO
