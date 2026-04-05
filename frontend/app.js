const BACKEND_URL = "https://farmhub-api.onrender.com"; // <-- PASTE YOUR RENDER URL HERE!
const API_URL = `${BACKEND_URL}/api`;

document.addEventListener('DOMContentLoaded', () => {
    const loginForm = document.getElementById('loginForm');
    const registerForm = document.getElementById('registerForm');
    const loginBtn = document.getElementById('submitBtn');
    const registerBtn = document.getElementById('regSubmitBtn');
    const errorMessage = document.getElementById('errorMessage');
    const successMessage = document.getElementById('successMessage');

    // Toggle Logic
    document.getElementById('showRegister').addEventListener('click', (e) => {
        e.preventDefault();
        loginForm.classList.add('hidden');
        registerForm.classList.remove('hidden');
        errorMessage.classList.remove('show');
    });

    document.getElementById('showLogin').addEventListener('click', (e) => {
        e.preventDefault();
        registerForm.classList.add('hidden');
        loginForm.classList.remove('hidden');
        errorMessage.classList.remove('show');
    });

    // --- REGISTRATION ---
    registerForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const username = document.getElementById('regUsername').value;
        const email = document.getElementById('regEmail').value;
        const password = document.getElementById('regPassword').value;

        try {
            const response = await fetch(`${API_URL}/register`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username, email, password })
            });
            const data = await response.json();
            if (!response.ok) throw new Error(data.message || 'Registration failed');

            successMessage.classList.add('show');
            registerForm.classList.add('hidden');
            loginForm.classList.remove('hidden');
        } catch (err) {
            errorMessage.textContent = err.message;
            errorMessage.classList.add('show');
        }
    });

    // --- LOGIN ---
    loginForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const email = document.getElementById('email').value;
        const password = document.getElementById('password').value;

        try {
            const response = await fetch(`${API_URL}/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.message || 'Invalid credentials');

            // CRITICAL: Save identity to local storage
            localStorage.setItem('farmhub_token', data.token);
            localStorage.setItem('farmhub_user', data.username);

            window.location.href = 'game.html';
        } catch (err) {
            errorMessage.textContent = err.message;
            errorMessage.classList.add('show');
        }
    });
});
