// Keep the Mapbox GL JS version aligned with the existing Backoffice map editor.
const version = "3.33.0";
let sdkLoad;
const searchControllers = new Map();

function loadSdk() {
    if (globalThis.mapboxgl) return Promise.resolve();
    return sdkLoad ??= new Promise((resolve, reject) => {
        const css = document.createElement("link");
        css.rel = "stylesheet";
        css.href = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.css`;
        document.head.append(css);
        const script = document.createElement("script");
        script.src = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.js`;
        script.onload = resolve;
        script.onerror = () => { sdkLoad = null; reject(new Error("Mapbox map SDK could not load.")); };
        document.head.append(script);
    });
}

export async function buscarDirecciones(token, query, key) {
    searchControllers.get(key)?.abort();
    const controller = new AbortController();
    searchControllers.set(key, controller);
    try {
        const url = new URL("https://api.mapbox.com/search/geocode/v6/forward");
        url.searchParams.set("q", query);
        url.searchParams.set("access_token", token);
        url.searchParams.set("permanent", "true");
        url.searchParams.set("autocomplete", "true");
        url.searchParams.set("limit", "5");
        url.searchParams.set("language", "es");
        url.searchParams.set("types", "address,street,place,locality,neighborhood");
        const response = await fetch(url, { signal: controller.signal, headers: { Accept: "application/json" } });
        if (!response.ok) return { estado: response.status, resultados: [] };
        const body = await response.json();
        const resultados = (body.features ?? []).map((feature, index) => {
            const coordinates = feature.geometry?.coordinates;
            const properties = feature.properties ?? {};
            if (!Array.isArray(coordinates) || coordinates.length < 2) return null;
            const longitude = Number(coordinates[0]);
            const latitude = Number(coordinates[1]);
            const description = String(properties.full_address ?? properties.name ?? "").trim();
            if (!description || !Number.isFinite(latitude) || !Number.isFinite(longitude)
                || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) return null;
            return {
                id: String(properties.mapbox_id ?? feature.id ?? `${index}:${longitude},${latitude}`),
                descripcion: description,
                latitud: latitude,
                longitud: longitude
            };
        }).filter(Boolean);
        return { estado: response.status, resultados };
    } catch (error) {
        if (error?.name === "AbortError") return { estado: 0, resultados: [] };
        throw error;
    } finally {
        if (searchControllers.get(key) === controller) searchControllers.delete(key);
    }
}

export function cancelarBusqueda(key) {
    searchControllers.get(key)?.abort();
    searchControllers.delete(key);
}

export function conectarTeclado(input, receptor) {
    const keydown = event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            receptor.invokeMethodAsync("MoverSugerencia", event.key === "ArrowDown" ? 1 : -1).catch(() => {});
        } else if (event.key === "Enter" && input.getAttribute("aria-expanded") === "true") {
            event.preventDefault();
            receptor.invokeMethodAsync("ElegirSugerenciaActiva").catch(() => {});
        } else if (event.key === "Escape" && input.getAttribute("aria-expanded") === "true") {
            event.preventDefault();
            receptor.invokeMethodAsync("CerrarSugerencias").catch(() => {});
        }
    };
    input.addEventListener("keydown", keydown);
    return { dispose() { input.removeEventListener("keydown", keydown); } };
}

export function enfocarBusqueda(input) { input?.focus(); }

export async function crear(elemento, token, estilo, receptor, editable) {
    await loadSdk();
    const map = new mapboxgl.Map({ container: elemento, accessToken: token, style: estilo,
        center: [-56, -33], zoom: 4, attributionControl: true });
    let marker, closed = false, notified = false, lastPoint;
    let canEdit = editable === true;
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
            marker = new mapboxgl.Marker({ draggable: canEdit, color: "#0D6EFD" })
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
    map.once("load", () => state("listo"));
    map.on("error", event => {
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
    return {
        mostrar(lat, lon) { if (!closed) updateMarker(lat, lon, true); },
        configurarEdicion(value) { canEdit = value === true; marker?.setDraggable(canEdit); },
        dispose
    };
}
