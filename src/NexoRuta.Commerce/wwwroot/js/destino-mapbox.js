// Keep the Mapbox GL JS version aligned with the existing Backoffice map editor.
const version = "3.33.0";
let sdkLoad;
let cssLoad;

async function loadSdk() {
    await (cssLoad ??= new Promise((resolve, reject) => {
        const href = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.css`;
        const existing = document.querySelector(`link[href="${href}"]`);
        if (existing?.sheet) { resolve(); return; }
        const css = existing ?? document.createElement("link");
        css.addEventListener("load", resolve, { once: true });
        css.addEventListener("error", () => {
            cssLoad = null; css.remove(); reject(new Error("Mapbox map stylesheet could not load."));
        }, { once: true });
        if (!existing) { css.rel = "stylesheet"; css.href = href; document.head.append(css); }
    }));
    if (globalThis.mapboxgl) return Promise.resolve();
    return sdkLoad ??= new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.js`;
        script.onload = resolve;
        script.onerror = () => { sdkLoad = null; reject(new Error("Mapbox map SDK could not load.")); };
        document.head.append(script);
    });
}

export function conectarTeclado(input, receptor) {
    let pending = Promise.resolve();
    const invoke = (method, ...args) => { pending = pending.then(() => receptor.invokeMethodAsync(method, ...args)).catch(() => {}); };
    const keydown = event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            invoke("MoverSugerencia", event.key === "ArrowDown" ? 1 : -1);
        } else if (event.key === "Enter" && input.getAttribute("aria-expanded") === "true") {
            event.preventDefault();
            invoke("ElegirSugerenciaActiva");
        } else if (event.key === "Escape" && input.getAttribute("aria-expanded") === "true") {
            event.preventDefault();
            invoke("CerrarSugerencias");
        }
    };
    input.addEventListener("keydown", keydown);
    return { dispose() { input.removeEventListener("keydown", keydown); } };
}

export function enfocarBusqueda(input) { input?.focus(); }

export async function crear(elemento, token, estilo, receptor, editable) {
    await loadSdk();
    const colors = getComputedStyle(elemento);
    const color = colors.getPropertyValue("--nr-action").trim() || colors.color;
    const map = new mapboxgl.Map({ container: elemento, accessToken: token, style: estilo,
        center: [-56, -33], zoom: 4, attributionControl: true });
    let marker, closed = false, notified = false, lastPoint;
    let canEdit = editable === true;
    let coverage = { type: "FeatureCollection", features: [] };
    const coverageSource = "destination-coverage";
    let coverageKey = "", pendingCoverageFit = false;
    let styleReady = map.isStyleLoaded?.() === true;
    let renderingCoverage = false, coverageRenderFailed = false;
    let lastCoverageState;
    const coverageState = value => {
        if (closed || lastCoverageState === value) return;
        lastCoverageState = value;
        receptor.invokeMethodAsync("EstadoCobertura", value).catch(() => {});
    };
    const renderCoverage = () => {
        if (closed || renderingCoverage) return;
        renderingCoverage = true;
        try {
            const source = map.getSource(coverageSource);
            if (source) source.setData(coverage);
            if (!styleReady) return;
            if (!source) map.addSource(coverageSource, { type: "geojson", data: coverage });
            const layers = [
                { id: coverageSource + "-fill", type: "fill", paint: { "fill-color": color, "fill-opacity": 0.22 } },
                { id: coverageSource + "-halo", type: "line", paint: { "line-color": colors.getPropertyValue("--nr-surface").trim() || color, "line-width": 6, "line-opacity": 0.9 } },
                { id: coverageSource + "-line", type: "line", paint: { "line-color": colors.getPropertyValue("--nr-action-strong").trim() || color, "line-width": 3 } }
            ];
            for (const layer of layers) {
                if (!map.getLayer(layer.id)) map.addLayer({ ...layer, source: coverageSource, slot: "top", layout: { visibility: "visible" } });
                map.moveLayer(layer.id);
            }
            if (pendingCoverageFit) { pendingCoverageFit = false; fitCoverage(); }
        } catch { coverageRenderFailed = true; coverageState("error"); }
        finally { renderingCoverage = false; }
    };
    map.on("styledataloading", event => { if (event.dataType === "style") styleReady = false; });
    map.on("style.load", () => { coverageRenderFailed = false; styleReady = true; renderCoverage(); });
    const restoreCoverage = () => {
        // Differential setStyle can emit styledata/idle without another style.load.
        if (closed || coverageRenderFailed || !map.isStyleLoaded?.()) return;
        if (pendingCoverageFit || !map.getSource(coverageSource) || ["fill", "halo", "line"].some(suffix => !map.getLayer(coverageSource + "-" + suffix))) {
            styleReady = true;
            renderCoverage();
        }
        if (!coverageRenderFailed && ["fill", "halo", "line"].every(suffix => map.getLayer(coverageSource + "-" + suffix)) && map.isSourceLoaded?.(coverageSource)) coverageState("listo");
    };
    map.on("styledata", restoreCoverage);
    map.on("idle", restoreCoverage);
    const state = name => { if (!closed) receptor.invokeMethodAsync("EstadoMapa", name).catch(() => {}); };
    const select = (lat, lon, description = null) => {
        if (closed || !canEdit) return;
        updateMarker(lat, lon, false);
        receptor.invokeMethodAsync("Seleccionar", lat, lon, description).catch(() => {});
    };
    function updateMarker(lat, lon, center) {
        if (!Number.isFinite(lat) || !Number.isFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180) {
            marker?.remove(); marker = null; lastPoint = undefined; return;
        }
        const changed = !lastPoint || lastPoint[0] !== lat || lastPoint[1] !== lon;
        if (!marker) {
            marker = new mapboxgl.Marker({ draggable: canEdit, color })
                .setLngLat([lon, lat]).addTo(map);
            marker.on("dragend", () => { const point = marker.getLngLat(); select(point.lat, point.lng); });
        } else {
            marker.setDraggable(canEdit);
            marker.setLngLat([lon, lat]);
        }
        lastPoint = [lat, lon];
        if (center && changed) map.jumpTo({ center: [lon, lat], zoom: 15 });
    }
    map.addControl(new mapboxgl.NavigationControl({ showCompass: false }));
    map.on("click", event => select(event.lngLat.lat, event.lngLat.lng));
    map.once("load", () => { styleReady = true; renderCoverage(); state("listo"); });
    map.on("error", event => {
        if (event.sourceId === coverageSource) { coverageRenderFailed = true; coverageState("error"); return; }
        if (notified || closed) return;
        notified = true;
        const status = Number(event?.error?.status ?? event?.status);
        const code = String(event?.error?.code ?? event?.error?.name ?? "").toLowerCase();
        state(status === 401 || status === 403 || code.includes("auth") || code.includes("forbidden")
            ? "token" : navigator.onLine ? "proveedor" : "sin-conexion");
    });
    const resize = new ResizeObserver(() => { if (!closed) map.resize(); });
    resize.observe(elemento);
    let removalObserver;
    const dispose = () => {
        if (closed) return;
        closed = true;
        resize.disconnect(); removalObserver?.disconnect(); map.remove();
    };
    removalObserver = new MutationObserver(() => { if (!elemento.isConnected) dispose(); });
    removalObserver.observe(document.body, { childList: true, subtree: true });
    function fitCoverage() {
        if (closed || coverage.features.length === 0) return;
        const bounds = [[Infinity, Infinity], [-Infinity, -Infinity]];
        const include = point => {
            bounds[0][0] = Math.min(bounds[0][0], point[0]); bounds[0][1] = Math.min(bounds[0][1], point[1]);
            bounds[1][0] = Math.max(bounds[1][0], point[0]); bounds[1][1] = Math.max(bounds[1][1], point[1]);
        };
        const visit = coordinates => {
            if (Number.isFinite(coordinates?.[0]) && Number.isFinite(coordinates?.[1])) include(coordinates);
            else if (Array.isArray(coordinates)) coordinates.forEach(visit);
        };
        coverage.features.forEach(feature => visit(feature.geometry.coordinates));
        if (lastPoint) include([lastPoint[1], lastPoint[0]]);
        if (!bounds.flat().every(Number.isFinite)) return;
        map.resize(); map.fitBounds(bounds, { padding: 32, maxZoom: 14, duration: 0 });
    }
    return {
        cobertura(zonas) {
            coverageRenderFailed = false;
            const key = (zonas ?? []).map(z => z.zonaId ?? z.nombre).join("|");
            if (key !== coverageKey) { coverageKey = key; pendingCoverageFit = (zonas?.length ?? 0) > 0; }
            coverage = { type: "FeatureCollection", features: (zonas ?? []).map(z => ({ type: "Feature", properties: { nombre: z.nombre }, geometry: z.geometria })) };
            renderCoverage();
        },
        verCobertura: fitCoverage,
        mostrar(lat, lon) { if (!closed) updateMarker(lat, lon, true); },
        configurarEdicion(value) { canEdit = value === true; marker?.setDraggable(canEdit); },
        dispose
    };
}
