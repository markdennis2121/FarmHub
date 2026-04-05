USE ModernLoginDB;
GO

-- 1. Add the Username column
ALTER TABLE Users 
ADD Username NVARCHAR(100) NULL;
GO

-- 2. Update existing accounts (like demo@example.com) with a default username
UPDATE Users 
SET Username = 'MasterFarmer' 
WHERE Email = 'demo@example.com' AND Username IS NULL;

-- 3. In the future, we would make this NOT NULL, 
-- but for now, we'll keep it as NULL and handle it in the Registration logic.
GO
