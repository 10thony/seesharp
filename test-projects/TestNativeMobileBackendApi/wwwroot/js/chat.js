"use strict";

let accessToken = null;
let currentUser = null;

async function ensureAuthenticated() {
    if (accessToken && currentUser) {
        return { accessToken, currentUser };
    }

    const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ userName: "demo", password: "Password1!" })
    });

    if (!response.ok) {
        throw new Error("Login failed. Register a user or reset the database with infra/reset-database.ps1");
    }

    const data = await response.json();
    accessToken = data.accessToken;
    currentUser = data.user;
    document.getElementById("signedInAs").textContent = `Signed in as ${currentUser.displayName || currentUser.userName}`;
    return { accessToken, currentUser };
}

function appendMessage(user, message, sentAt) {
    const li = document.createElement("li");
    const timestamp = sentAt ? new Date(sentAt).toLocaleTimeString() : "";
    li.textContent = timestamp ? `[${timestamp}] ${user}: ${message}` : `${user}: ${message}`;
    document.getElementById("messagesList").appendChild(li);
}

async function loadHistory(token) {
    const response = await fetch("/api/chat/messages?limit=50", {
        headers: { Authorization: `Bearer ${token}` }
    });

    if (!response.ok) {
        return;
    }

    const messages = await response.json();
    for (const item of messages) {
        appendMessage(item.userName, item.message, item.sentAt);
    }
}

async function startChat() {
    const { accessToken: token } = await ensureAuthenticated();
    await loadHistory(token);

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/chatHub", { accessTokenFactory: () => token })
        .build();

    document.getElementById("sendButton").disabled = true;

    connection.on("ReceiveMessage", function (user, message, sentAt) {
        appendMessage(user, message, sentAt);
    });

    await connection.start();
    document.getElementById("sendButton").disabled = false;

    document.getElementById("sendButton").addEventListener("click", function (event) {
        const message = document.getElementById("messageInput").value;
        connection.invoke("SendMessage", message).catch(function (err) {
            return console.error(err.toString());
        });
        event.preventDefault();
    });
}

startChat().catch(function (err) {
    console.error(err.toString());
});
