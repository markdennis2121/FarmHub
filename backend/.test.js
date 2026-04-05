import sql from 'mssql/msnodesqlv8.js';

const dbConfig1 = {
    server: 'localhost',
    database: 'ModernLoginDB',
    driver: 'msnodesqlv8',
    options: {
        instanceName: 'SQLEXPRESS',
        trustedConnection: true,
        trustServerCertificate: true
    }
};

const dbConfig2 = {
    server: 'localhost',
    database: 'ModernLoginDB',
    driver: 'msnodesqlv8',
    options: {
        instanceName: 'SQLEXPRESS01',
        trustedConnection: true,
        trustServerCertificate: true
    }
};

const dbConfig3 = {
    connectionString: 'Driver={SQL Server Native Client 11.0};Server=MSI\\SQLEXPRESS;Database=ModernLoginDB;Trusted_Connection=yes;',
};

async function test(name, config) {
    console.log(`Testing ${name}...`)
    try {
        let pool = await sql.connect(config);
        let res = await pool.query`SELECT 1 as val`;
        console.log(`${name} SUCCESS! val:`, res.recordset[0].val);
        process.exit(0);
    } catch(err) {
        console.log(`${name} FAILED:`);
        console.dir(err, {depth: null});
    }
}

async function runAll() {
    await test('dbConfig1', dbConfig1);
    await test('dbConfig2', dbConfig2);
    // await test('dbConfig3', dbConfig3);
    console.log("All failed.");
    process.exit(1);
}

runAll();
