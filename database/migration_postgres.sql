-- 1. Create the Users table (Postgres Syntax)
CREATE TABLE IF NOT EXISTS Users (
    Id SERIAL PRIMARY KEY,
    Username VARCHAR(100) UNIQUE NOT NULL,
    Email VARCHAR(150) UNIQUE NOT NULL,
    PasswordHash TEXT NOT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 2. Create the ChatMessages table
CREATE TABLE IF NOT EXISTS ChatMessages (
    Id SERIAL PRIMARY KEY,
    Username VARCHAR(100) NOT NULL,
    Message VARCHAR(255) NOT NULL,
    Color VARCHAR(50) NOT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 3. Initial Greeting data
INSERT INTO ChatMessages (Username, Message, Color)
SELECT 'MasterFarmer', 'Welcome to the Postgres Cloud of FarmHub!', '#fde047'
WHERE NOT EXISTS (SELECT 1 FROM ChatMessages WHERE Username = 'MasterFarmer');
