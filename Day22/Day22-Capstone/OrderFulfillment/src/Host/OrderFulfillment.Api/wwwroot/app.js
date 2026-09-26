const STAGES = ["Pending", "Confirmed", "PaymentReceived", "Shipped"];
const POLL_INTERVAL_MS = 1500;
const POLL_TIMEOUT_MS = 60_000;

const form = document.getElementById("order-form");
const submitBtn = document.getElementById("submit-btn");
const formError = document.getElementById("form-error");
const sagaPanel = document.getElementById("saga-panel");
const orderIdEl = document.getElementById("order-id");
const sagaSteps = document.getElementById("saga-steps");
const sagaCancelled = document.getElementById("saga-cancelled");
const sagaNote = document.getElementById("saga-note");
const historyList = document.getElementById("history");
const btnLabel = submitBtn.querySelector(".btn-label");
const spinner = submitBtn.querySelector(".spinner");

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  formError.hidden = true;
  submitBtn.disabled = true;
  btnLabel.textContent = "Placing…";
  spinner.hidden = false;

  const sku = document.getElementById("sku").value;
  const quantity = Number(document.getElementById("quantity").value);
  const unitPrice = Number(document.getElementById("unitPrice").value);

  try {
    const response = await fetch("/api/v1/orders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        customerId: crypto.randomUUID(),
        lines: [{ sku, quantity, unitPrice, currency: "USD" }],
      }),
    });

    if (response.status === 429) {
      throw new Error("Rate limited (20 orders / 10s) — wait a moment and try again.");
    }

    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      const detail = problem?.errors
        ? Object.values(problem.errors).flat().join(" ")
        : problem?.error ?? `HTTP ${response.status}`;
      throw new Error(detail);
    }

    const { orderId } = await response.json();
    trackOrder(orderId, `${quantity}× ${sku}`);
  } catch (err) {
    formError.textContent = err.message;
    formError.hidden = false;
  } finally {
    submitBtn.disabled = false;
    btnLabel.textContent = "Place order";
    spinner.hidden = true;
  }
});

function trackOrder(orderId, summary) {
  sagaPanel.hidden = false;
  sagaCancelled.hidden = true;
  sagaNote.textContent = "Waiting for the saga to advance…";
  orderIdEl.textContent = orderId;
  for (const li of sagaSteps.children) {
    li.classList.remove("done", "current");
  }

  historyList.querySelector("li.empty")?.remove();
  const historyItem = document.createElement("li");
  historyItem.innerHTML = `<span>${summary}</span><span class="status">…</span>`;
  historyList.prepend(historyItem);

  const startedAt = Date.now();

  const poll = async () => {
    if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
      sagaNote.textContent =
        "Still not done after 60s — if this is pointed at real Azure infra, check the Container App logs.";
      return;
    }

    let order;
    try {
      const response = await fetch(`/api/v1/orders/${orderId}`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      order = await response.json();
    } catch {
      sagaNote.textContent = "Couldn't reach the API — retrying…";
      setTimeout(poll, POLL_INTERVAL_MS);
      return;
    }

    renderStatus(order.status);
    historyItem.querySelector(".status").textContent = order.status;
    historyItem.querySelector(".status").className = `status ${order.status}`;

    if (order.status === "Shipped" || order.status === "Cancelled") {
      sagaNote.textContent = `Settled after ${((Date.now() - startedAt) / 1000).toFixed(1)}s.`;
      return;
    }

    setTimeout(poll, POLL_INTERVAL_MS);
  };

  poll();
}

function renderStatus(status) {
  if (status === "Cancelled") {
    sagaCancelled.hidden = false;
    return;
  }

  const currentIndex = STAGES.indexOf(status);
  const isTerminal = status === "Shipped"; // nothing comes after this — settled, not "still working"

  for (const li of sagaSteps.children) {
    const stepIndex = STAGES.indexOf(li.dataset.status);
    const isCurrentStep = stepIndex === currentIndex;
    li.classList.toggle("done", stepIndex < currentIndex || (isCurrentStep && isTerminal));
    li.classList.toggle("current", isCurrentStep && !isTerminal);
  }
}
