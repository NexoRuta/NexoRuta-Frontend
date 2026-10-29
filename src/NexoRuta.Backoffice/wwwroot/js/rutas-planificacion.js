(() => {
    const formatWeight = grams => `${(grams / 1000).toLocaleString("es-UY", { maximumFractionDigits: 2 })} kg`;
    const formatSummaryDate = value => {
        const parts = new Intl.DateTimeFormat("es-UY", { weekday: "long", day: "numeric", month: "long" })
            .formatToParts(new Date(`${value}T12:00:00`));
        const part = type => parts.find(item => item.type === type)?.value || "";
        const weekday = part("weekday");
        return `${weekday.charAt(0).toLocaleUpperCase("es-UY")}${weekday.slice(1)} ${part("day")} de ${part("month")}`;
    };

    function initialize() {
        const form = document.querySelector("#route-planning-form");
        if (!form) return;

        const rows = [...form.querySelectorAll("[data-plan-row]")];
        const search = form.querySelector("#shipment-search");
        const eligibility = form.querySelector("#eligibility-filter");
        const emptySearch = form.querySelector("[data-no-search-results]");
        const courier = form.querySelector("#RepartidorAccesoId");
        const vehicle = form.querySelector("[data-vehicle-select]");
        const date = form.querySelector("#Fecha");
        const count = document.querySelector("[data-summary-count]");
        const packages = document.querySelector("[data-summary-packages]");
        const weight = document.querySelector("[data-summary-weight]");
        const firstTime = document.querySelector("[data-summary-time]");
        const courierSummary = document.querySelector("[data-summary-courier]");
        const vehicleSummary = document.querySelector("[data-summary-vehicle]");
        const destinationCondition = document.querySelector("[data-summary-destination]");
        const custodyCondition = document.querySelector("[data-summary-custody]");
        const capacityLabel = document.querySelector("[data-capacity-label]");
        const dateSummary = document.querySelector("[data-summary-date]");
        const live = document.querySelector("[data-plan-summary]");

        function selectedRows() {
            return rows.filter(row => row.querySelector("[data-plan-check]")?.checked);
        }

        function refreshSummary() {
            const selected = selectedRows();
            const totalWeight = selected.reduce((sum, row) => sum + Number(row.dataset.weight || 0), 0);
            const totalPackages = selected.reduce((sum, row) => sum + Number(row.dataset.packageCount || 0), 0);
            const firstRow = [...selected].sort((a, b) => {
                const orderA = Number(a.querySelector("[data-stop-order]")?.value || Number.MAX_SAFE_INTEGER);
                const orderB = Number(b.querySelector("[data-stop-order]")?.value || Number.MAX_SAFE_INTEGER);
                return orderA - orderB;
            })[0];

            count.textContent = String(selected.length);
            packages.textContent = String(totalPackages);
            weight.textContent = formatWeight(totalWeight);
            firstTime.textContent = firstRow?.querySelector("[data-stop-time]")?.value || "Hora pendiente";
            courierSummary.textContent = courier.selectedOptions[0]?.textContent?.trim() || "Sin seleccionar";
            vehicleSummary.textContent = vehicle.selectedOptions[0]?.textContent?.trim() || "Sin seleccionar";
            dateSummary.textContent = date.value ? formatSummaryDate(date.value) : "Sin definir";

            const option = vehicle.selectedOptions[0];
            const maxWeight = Number(option?.dataset.weightCapacity || 0);
            const maxVolume = Number(option?.dataset.volumeCapacity || 0);
            capacityLabel.textContent = maxWeight
                ? `${formatWeight(maxWeight)} de capacidad · ${(maxVolume / 1_000_000).toLocaleString("es-UY", { maximumFractionDigits: 3 })} m³`
                : "Seleccioná un vehículo para consultar su capacidad.";

            const setCondition = (element, valid, completeText, pendingText) => {
                const confirmed = selected.length > 0 && valid;
                const text = element.querySelector("span:last-child");
                const icon = element.querySelector("span:first-child");
                element.classList.toggle("is-confirmed", confirmed);
                element.classList.toggle("is-pending", !confirmed);
                element.dataset.confirmed = String(confirmed);
                icon.textContent = selected.length === 0 ? "—" : confirmed ? "✓" : "!";
                text.textContent = selected.length === 0 ? "Seleccioná un envío para verificar" : confirmed ? completeText : pendingText;
            };
            setCondition(destinationCondition,
                selected.every(row => row.dataset.destination === "true"),
                "Destino confirmado", "Destino pendiente de confirmación");
            setCondition(custodyCondition,
                selected.every(row => row.dataset.custody === "true"),
                "Bulto recibido en depósito", "Recepción física pendiente");

            const over = maxWeight > 0 && totalWeight > maxWeight;
            live.textContent = over
                ? `${selected.length} envíos seleccionados · ${formatWeight(totalWeight)}. El peso excede la capacidad configurada; el servidor volverá a validar el plan.`
                : `${selected.length} envíos seleccionados · ${formatWeight(totalWeight)}. El servidor validará el plan y la disponibilidad al guardar.`;
        }

        for (const row of rows) {
            const checkbox = row.querySelector("[data-plan-check]");
            const order = row.querySelector("[data-stop-order]");
            const time = row.querySelector("[data-stop-time]");
            const eligible = row.dataset.destination === "true" && row.dataset.custody === "true";
            const refresh = () => {
                const selected = checkbox.checked && eligible;
                checkbox.checked = selected;
                row.classList.toggle("is-selected", selected);
                order.disabled = !selected;
                time.disabled = !selected;
                order.required = selected;
                time.required = selected;
            };
            checkbox.addEventListener("change", refresh);
            order.addEventListener("input", refreshSummary);
            time.addEventListener("input", refreshSummary);
            refresh();
        }

        function filterRows() {
            const query = search.value.trim().toLocaleLowerCase("es-UY");
            const onlyEligible = eligibility.value === "eligible";
            let visible = 0;
            for (const row of rows) {
                const matchesSearch = (row.dataset.search || "").toLocaleLowerCase("es-UY").includes(query);
                const matchesEligibility = !onlyEligible || row.dataset.eligible === "true";
                const matches = matchesSearch && matchesEligibility;
                row.hidden = !matches;
                if (matches) visible++;
            }
            emptySearch.hidden = visible !== 0;
        }

        search?.addEventListener("input", filterRows);
        eligibility?.addEventListener("change", filterRows);

        courier.addEventListener("change", refreshSummary);
        vehicle.addEventListener("change", refreshSummary);
        date.addEventListener("change", refreshSummary);
        filterRows();
        refreshSummary();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
    else initialize();
})();
