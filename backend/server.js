import express from 'express';
import cors from 'cors';
import sql from 'mssql';
import bcrypt from 'bcrypt';
import jwt from 'jsonwebtoken';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
app.use(cors());
app.use(express.json());

// Serve the frontend automatically so http://localhost:8080 works immediately
app.use(express.static(path.join(__dirname, '../frontend')));

const JWT_SECRET = "super-secret-key-change-me";

// ==========================================
// SQL Server Configuration (SQL Authentication)
// ==========================================
const dbConfig = {
    database: 'ModernLoginDB',
    server: 'localhost',
    options: {
        instanceName: 'SQLEXPRESS',
        encrypt: false,
        trustServerCertificate: true
    }
};

app.post('/api/login', async (req, res) => {
    try {
        const { email, password } = req.body;

        if (!email || !password) {
            return res.status(400).json({ message: 'Email and password are required' });
        }

        // Connect to DB
        await sql.connect(dbConfig);

        // Fetch User and prevent SQL injection using template literals
        const result = await sql.query`SELECT Id, Email, PasswordHash FROM Users WHERE Email = ${email}`;

        if (result.recordset.length === 0) {
            // Delay added to prevent user enumeration attacks via timing
            await new Promise(r => setTimeout(r, Math.random() * 200 + 100));
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        const user = result.recordset[0];

        // Verify Hash
        const passwordMatch = await bcrypt.compare(password, user.PasswordHash);

        if (!passwordMatch) {
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        // Generate Secure JWT Token
        const token = jwt.sign({ id: user.Id, email: user.Email }, JWT_SECRET, { expiresIn: '2h' });

        return res.json({ token, message: 'Logged in successfully' });

    } catch (err) {
        console.error("Database connection or query error: ", err.message || err);
        return res.status(500).json({ message: 'Internal server error - check backend console.' });
    }
});

const PORT = 8080;
app.listen(PORT, () => {
    console.log(`🚀 Modern Backend API running rapidly on http://localhost:${PORT}`);
    console.log(`================================================================`);
    console.log(`Don't forget to update the database connection strings in server.js`);
    console.log(`and run the init.sql script in SSMS!`);
});
