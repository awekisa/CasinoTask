"use strict";
const storageKey = "betty.browser.player.v1";
const $ = id => document.getElementById(id);
const currency = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const paths = { deposit: "/api/deposits", withdraw: "/api/withdrawals", bet: "/api/bets" };
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
let profile;
let busy = false;
let ready = false;
let hasLeft = false;

function readProfile() {
    const value = localStorage.getItem(storageKey);
    if (!value) return { playerId: crypto.randomUUID(), pending: null };
    const saved = JSON.parse(value);
    if (!guid.test(saved.playerId)) throw new Error("Saved player data is invalid. No request was sent.");
    return saved;
}
function saveProfile() { localStorage.setItem(storageKey, JSON.stringify(profile)); }
function message(text, error = false) {
    $("message").textContent = text;
    document.querySelector(".result").classList.toggle("error", error);
}
function renderControls() {
    const blocked = busy || !ready || !!profile?.pending || hasLeft;
    document.querySelectorAll("#wallet-form button, #bet-form button, #wallet-form input, #bet-form input, #player-form button, #player-form input").forEach(element => element.disabled = blocked);
    $("refresh").disabled = busy || hasLeft;
    $("leave").disabled = blocked;
    $("retry").hidden = !profile?.pending;
    $("retry").disabled = busy;
    $("player-id").value = profile?.playerId ?? "";
}
function displayBalance(balance) {
    for (const name of ["total", "available", "held"]) $(name).textContent = currency.format(balance[name]);
}
async function api(path, method = "GET", body) {
    const response = await fetch(path, {
        method, cache: "no-store",
        headers: body ? { "Content-Type": "application/json" } : {},
        body: body ? JSON.stringify(body) : undefined
    });
    const result = await response.json();
    if (!response.ok) {
        const error = new Error(result.error || result.title || `Request failed (${response.status}).`);
        error.status = response.status;
        throw error;
    }
    return result;
}
async function refresh() {
    const balance = await api(`/api/wallets/${profile.playerId}`, "PUT");
    displayBalance(balance);
    ready = true;
    return balance;
}
async function exclusive(action) {
    if (busy) return;
    busy = true;
    renderControls();
    try {
        // Coordinate tabs so an unresolved request is never replaced by a new one.
        await navigator.locks.request(storageKey, async () => {
            const previousPlayer = profile?.playerId;
            profile = readProfile();
            if (previousPlayer !== profile.playerId) {
                ready = false;
                for (const name of ["total", "available", "held"]) $(name).textContent = "—";
            }
            saveProfile();
            await action();
        });
    } catch (error) {
        message(`${error.message} ${profile?.pending ? "Your request is saved. Use Retry saved request to check its outcome." : "Check that Wallet and Game are running, then refresh."}`, true);
    } finally { busy = false; renderControls(); }
}
async function submitPending() {
    const pending = profile.pending;
    if (!pending) { message("There is no saved request to retry."); return; }
    message("Processing your request…");
    let result;
    try { result = await api(paths[pending.action], "POST", pending.request); }
    catch (error) {
        // Validation errors confirm that the request was not accepted.
        // Network failures and conflicts retain the original request ID.
        if (error.status === 400) { profile.pending = null; saveProfile(); }
        throw error;
    }
    if (pending.action === "bet" && ["Pending", "Decided"].includes(result.state)) {
        message("Your round is still processing. Retry the saved request to check the result. Your stake will not be charged twice.");
    } else {
        profile.pending = null;
        saveProfile();
        if (result.state === "Rejected" || result.status === "Rejected") message(result.error || "Request rejected.", true);
        else if (pending.action === "bet") message(result.payout === 0 ? "You lost this round." : `Congrats - you won ${currency.format(result.payout)}!`);
        else message(`Your ${pending.action === "deposit" ? "deposit" : "withdrawal"} of ${currency.format(pending.request.amount)} was successful.`);
    }
    try {
        const balance = await refresh();
        message(`${$("message").textContent} Your current balance is: ${currency.format(balance.total)}`,
            result.state === "Rejected" || result.status === "Rejected");
    }
    catch { message(`${$("message").textContent} Current balance is unavailable; refresh when Wallet is running.`, true); }
}
function place(action, value) {
    if (!/^\d+(\.\d{1,2})?$/.test(value) || Number(value) <= 0 || Number(value) > 10000000000 ||
        (action === "bet" && (Number(value) < 1 || Number(value) > 10))) {
        message("Enter a positive amount with at most two decimal places. Bets must be $1–$10.", true);
        return;
    }
    exclusive(async () => {
        if (profile.pending) { message("Resolve your saved request before starting another."); return; }
        profile.pending = { action, request: { playerId: profile.playerId, requestId: crypto.randomUUID(), amount: Number(value) } };
        saveProfile(); // Save before sending so reloads and timeouts can replay the same request.
        await submitPending();
    });
}
$("wallet-form").addEventListener("submit", event => { event.preventDefault(); place(event.submitter?.value || "deposit", $("wallet-amount").value); });
$("bet-form").addEventListener("submit", event => { event.preventDefault(); place("bet", $("bet-amount").value); });
$("retry").addEventListener("click", () => exclusive(submitPending));
$("leave").addEventListener("click", () => exclusive(async () => {
    if (profile.pending) { message("Resolve your saved request before leaving."); return; }
    hasLeft = true;
    message("Thank you for playing! Hope to see you again soon.");
}));
$("refresh").addEventListener("click", () => exclusive(async () => { await refresh(); message(profile.pending ? "Balance updated. You still have a saved request to resolve." : "Balance updated. Ready when you are."); }));
$("player-form").addEventListener("submit", event => {
    event.preventDefault();
    const playerId = $("player-id").value.trim();
    if (!guid.test(playerId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(playerId)) { message("Enter a valid, non-empty player ID.", true); return; }
    exclusive(async () => {
        if (profile.pending) throw new Error("Resolve your saved request before changing player.");
        profile = { playerId, pending: null };
        saveProfile();
        ready = false;
        for (const name of ["total", "available", "held"]) $(name).textContent = "—";
        await refresh();
        message("Player loaded. Your wallet is ready.");
    });
});
window.addEventListener("storage", event => {
    if (event.key === storageKey && !busy) exclusive(async () => { await refresh(); message(profile.pending ? "A saved request is waiting. Retry to check its result." : "Player updated from another tab."); });
});
exclusive(async () => { await refresh(); message(profile.pending ? "Welcome back. Retry your saved request to check its result." : "Your wallet is ready. Add funds to get started."); });
