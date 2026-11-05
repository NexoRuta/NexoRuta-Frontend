(() => {
    const formatWeight = grams => `${(grams / 1000).toLocaleString("es-UY", { maximumFractionDigits: 5 })} kg`;
    const formatVolume = cm3 => Number.isFinite(cm3)
        ? `${(cm3 / 1_000_000).toLocaleString("es-UY", { maximumFractionDigits: 12 })} m³`
        : "Excede rango representable";
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
        const volume = document.querySelector("[data-summary-volume]");
        const weightCapacity = document.querySelector("[data-summary-weight-capacity]");
        const volumeCapacity = document.querySelector("[data-summary-volume-capacity]");
        const weightExcess = document.querySelector("[data-summary-weight-excess]");
        const volumeExcess = document.querySelector("[data-summary-volume-excess]");
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
            let weightNow = selected.filter(row => row.dataset.pickup !== "true").reduce((sum, row) => sum + Number(row.dataset.weight || 0), 0);
            let volumeNow = selected.filter(row => row.dataset.pickup !== "true").reduce((sum, row) => sum + Number(row.dataset.volume || 0), 0);
            let totalWeight = weightNow, totalVolume = volumeNow;
            const itinerary = [...selected].sort((a,b) => Number(a.querySelector("[data-stop-order]")?.value || 0) - Number(b.querySelector("[data-stop-order]")?.value || 0));
            for (const row of itinerary) {
                const direction = row.dataset.pickup === "true" ? 1 : -1;
                weightNow += direction * Number(row.dataset.weight || 0);
                volumeNow += direction * Number(row.dataset.volume || 0);
                totalWeight = Math.max(totalWeight, weightNow); totalVolume = Math.max(totalVolume, volumeNow);
            }

            const totalPackages = selected.reduce((sum, row) => sum + Number(row.dataset.packageCount || 0), 0);
            const firstRow = [...selected].sort((a, b) => {
                const orderA = Number(a.querySelector("[data-stop-order]")?.value || Number.MAX_SAFE_INTEGER);
                const orderB = Number(b.querySelector("[data-stop-order]")?.value || Number.MAX_SAFE_INTEGER);
                return orderA - orderB;
            })[0];

            count.textContent = String(selected.length);
            packages.textContent = String(totalPackages);
            weight.textContent = formatWeight(totalWeight);
            volume.textContent = formatVolume(totalVolume);
            firstTime.textContent = `${firstRow?.querySelector("[data-stop-date]")?.value || ""} ${firstRow?.querySelector("[data-stop-time]")?.value || "Hora pendiente"}`.trim();
            courierSummary.textContent = courier.selectedOptions[0]?.textContent?.trim() || "Sin seleccionar";
            vehicleSummary.textContent = vehicle.selectedOptions[0]?.textContent?.trim() || "Sin seleccionar";
            dateSummary.textContent = date.value ? formatSummaryDate(date.value) : "Sin definir";

            const option = vehicle.selectedOptions[0];
            const maxWeight = Number(option?.dataset.weightCapacity || 0);
            const maxVolume = Number(option?.dataset.volumeCapacity || 0);
            weightCapacity.textContent = maxWeight ? formatWeight(maxWeight) : "Sin vehículo";
            volumeCapacity.textContent = maxVolume ? formatVolume(maxVolume) : "Sin vehículo";
            weightExcess.textContent = !maxWeight ? "Seleccioná un vehículo para verificar el peso."
                : totalWeight > maxWeight ? "El peso excede la capacidad del vehículo." : "Peso dentro de la capacidad.";
            volumeExcess.textContent = !maxVolume ? "Seleccioná un vehículo para verificar el volumen."
                : totalVolume > maxVolume ? "El volumen excede la capacidad del vehículo." : "Volumen dentro de la capacidad.";
            capacityLabel.textContent = maxWeight
                ? `${formatWeight(maxWeight)} de capacidad · ${formatVolume(maxVolume)}`
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

            const over = maxWeight > 0 && totalWeight > maxWeight || maxVolume > 0 && totalVolume > maxVolume;
            live.textContent = over
                ? `${selected.length} envíos seleccionados · ${formatWeight(totalWeight)} · ${formatVolume(totalVolume)}. ${weightExcess.textContent} ${volumeExcess.textContent} El servidor volverá a validar el plan.`
                : `${selected.length} envíos seleccionados · ${formatWeight(totalWeight)} · ${formatVolume(totalVolume)}. El servidor validará el plan y la disponibilidad al guardar.`;
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
                for (const selector of ["[data-stop-date]", "[data-stop-service]"]) { const extra = row.querySelector(selector); if (extra) extra.disabled = !selected; }
                order.required = selected;
                time.required = selected;
            };
            checkbox.addEventListener("change", refresh);
            order.addEventListener("input", refreshSummary);
            time.addEventListener("input", refreshSummary);
            row.querySelector("[data-stop-date]")?.addEventListener("input", refreshSummary);
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
        date.addEventListener("input", refreshSummary);
        date.addEventListener("change", refreshSummary);
        filterRows();
        refreshSummary();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
    else initialize();
})();
