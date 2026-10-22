// Misma versión fijada por el editor Mapbox de Backoffice.
const version = "3.33.0";
let carga;
function cargarSdk() {
    if (globalThis.mapboxgl) return Promise.resolve();
    return carga ??= new Promise((resolve, reject) => {
        const css = document.createElement("link");
        css.rel = "stylesheet"; css.href = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.css`;
        document.head.append(css);
        const script = document.createElement("script");
        script.src = `https://api.mapbox.com/mapbox-gl-js/v${version}/mapbox-gl.js`;
        script.onload = resolve; script.onerror = () => { carga = null; reject(new Error("No se pudo cargar el mapa.")); };
        document.head.append(script);
    });
}
export async function crear(elemento, token, receptor) {
    await cargarSdk();
    const mapa = new mapboxgl.Map({ container: elemento, accessToken: token, style: "mapbox://styles/mapbox/streets-v12",
        center: [0, 0], zoom: 1, attributionControl: true });
    let marcador, cerrado = false;
    const informar = (lat, lon) => { if (!cerrado) receptor.invokeMethodAsync("Seleccionar", lat, lon).catch(() => {}); };
    mapa.addControl(new mapboxgl.NavigationControl({ showCompass: false }));
    mapa.on("click", e => informar(e.lngLat.lat, e.lngLat.lng));
    const dispose = () => { if (cerrado) return; cerrado = true; observador.disconnect(); mapa.remove(); };
    // El circuito puede desaparecer antes de que .NET alcance a liberar el mapa.
    const observador = new MutationObserver(() => { if (!elemento.isConnected) dispose(); });
    observador.observe(document.body, { childList: true, subtree: true });
    return {
        mostrar(lat, lon) {
            if (cerrado || !Number.isFinite(lat) || !Number.isFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180) return;
            if (!marcador) {
                marcador = new mapboxgl.Marker({ draggable: true, color: "#0D6EFD" }).setLngLat([lon, lat]).addTo(mapa);
                marcador.on("dragend", () => { const punto = marcador.getLngLat(); informar(punto.lat, punto.lng); });
            } else marcador.setLngLat([lon, lat]);
            mapa.jumpTo({ center: [lon, lat], zoom: 15 });
        }, dispose
    };
}
