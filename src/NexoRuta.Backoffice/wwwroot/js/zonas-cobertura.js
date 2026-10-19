(() => {
    let map;
    let draw;
    let drawFeatureId;
    let pointMarker;
    let territory;
    let mode;
    let drawListenersRegistered = false;
    let zoneGeometryLayer = false;
    let departmentSelect;

    const drawingSourceName = "Dibujo";
    const sourceRadios = document.querySelectorAll('input[name="Fuente"]');
    const departmentSource = document.getElementById("department-source");
    const drawingSource = document.getElementById("drawing-source");
    const geometryInput = document.getElementById("GeometriaJson");
    const root = document.getElementById("coverage-map");
    const status = document.getElementById("map-status");
    const setStatus = message => { if (status) status.textContent = message; };

    const parseTimeMicros = time => {
        const match = /^(?:([01]\d)|2([0-3])):([0-5]\d):([0-5]\d)(?:\.(\d{1,6}))?$/.exec(time ?? "");
        if (!match) return null;
        const hour = Number(match[1] ?? ("2" + match[2]));
        const minute = Number(match[3]);
        const second = Number(match[4]);
        const micros = Number((match[5] ?? "").padEnd(6, "0"));
        return ((hour * 60 + minute) * 60 + second) * 1_000_000 + micros;
    };
    const selectedTimeMicros = (form, prefix) => {
        const hour = form.querySelector('[data-time-part="' + prefix + '-hour"]');
        const minute = form.querySelector('[data-time-part="' + prefix + '-minute"]');
        if (!hour?.value || !minute?.value) return null;
        const original = hour.dataset.originalTime;
        const value = original && original.slice(0, 2) === hour.value && original.slice(3, 5) === minute.value
            ? original
            : hour.value + ":" + minute.value + ":00.000000";
        return parseTimeMicros(value);
    };
    document.querySelectorAll("[data-slot-time-form]").forEach(form => {
        const guidance = form.querySelector("[data-slot-time-guidance]");
        const updateGuidance = () => {
            if (!guidance) return;
            const start = selectedTimeMicros(form, "start");
            const end = selectedTimeMicros(form, "end");
            guidance.textContent = start === null || end === null ? ""
                : start === end ? "Los horarios de inicio y fin deben ser distintos."
                    : end < start ? "Finaliza al día siguiente." : "";
        };
        form.addEventListener("change", updateGuidance);
        updateGuidance();
    });

    const removeDepartmentHighlight = () => {
        const source = map?.getSource("department-reference");
        if (source) source.setData({ type: "FeatureCollection", features: [] });
    };
    const clearZoneGeometry = () => {
        const source = map?.getSource("zone-coverage");
        if (source) source.setData({ type: "FeatureCollection", features: [] });
        if (geometryInput) geometryInput.value = "";
    };
    const sourceIsDrawing = () => document.querySelector('input[name="Fuente"]:checked')?.value === drawingSourceName;

    const updateGeometrySource = () => {
        if (!departmentSource || !drawingSource) return;
        const isDrawing = sourceIsDrawing();
        departmentSource.hidden = isDrawing;
        drawingSource.hidden = !isDrawing;
        departmentSelect = departmentSource.querySelector("select");
        if (departmentSelect) departmentSelect.disabled = isDrawing || departmentSelect.options.length < 2;
        if (isDrawing) {
            if (departmentSelect) departmentSelect.value = "";
            removeDepartmentHighlight();
            if (mode === "create" && draw && territory) enableDraw(true);
        } else {
            if (draw) draw.deleteAll();
            clearZoneGeometry();
            removeDepartmentHighlight();
            if (mode === "create") enableDraw(false);
        }
    };

    const readJson = async url => {
        const response = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } });
        if (response.redirected) throw new Error("La sesión venció; ingresá nuevamente y recargá la página.");
        let body;
        try { body = await response.json(); }
        catch { throw new Error("La referencia territorial devolvió una respuesta inválida."); }
        if (!response.ok) throw new Error(body?.message || "No se pudo consultar la referencia territorial.");
        return body;
    };
    const safelyEditable = geometry => {
        if (!geometry?.coordinates || !Array.isArray(geometry.coordinates)) return false;
        if (geometry.type === "Polygon") return geometry.coordinates.length === 1;
        return geometry.type === "MultiPolygon" && geometry.coordinates.length > 0
            && geometry.coordinates.every(polygon => Array.isArray(polygon) && polygon.length === 1);
    };
    const updateGeoJsonField = () => {
        if (!geometryInput || !draw) return;
        const features = draw.getAll().features;
        if (features.length === 1) geometryInput.value = JSON.stringify(features[0].geometry);
        else if (features.length > 1 && features.every(feature => feature.geometry.type === "Polygon"))
            geometryInput.value = JSON.stringify({ type: "MultiPolygon", coordinates: features.map(feature => feature.geometry.coordinates) });
        else geometryInput.value = "";
    };

    const addExistingZoneLayer = geometry => {
        if (!geometry || zoneGeometryLayer || !map) return;
        map.addSource("zone-coverage", { type: "geojson", data: { type: "Feature", properties: {}, geometry } });
        map.addLayer({
            id: "zone-coverage-fill", type: "fill", source: "zone-coverage",
            paint: { "fill-color": "#1475b8", "fill-opacity": 0.28 }
        });
        map.addLayer({
            id: "zone-coverage-outline", type: "line", source: "zone-coverage",
            paint: { "line-color": "#075985", "line-width": 3 }
        });
        zoneGeometryLayer = true;
    };
    const clearExistingZoneLayer = () => {
        const source = map?.getSource("zone-coverage");
        if (source) source.setData({ type: "FeatureCollection", features: [] });
    };
    const addExistingToDraw = geometry => {
        if (!draw || !geometry) return;
        const components = geometry.type === "MultiPolygon"
            ? geometry.coordinates.map(coordinates => ({ type: "Polygon", coordinates }))
            : [geometry];
        drawFeatureId = draw.add({
            type: "FeatureCollection",
            features: components.map(shape => ({ type: "Feature", properties: {}, geometry: shape }))
        })[0];
    };
    const enableDraw = enabled => {
        if (!map || !window.MapboxDraw) return;
        if (!enabled && draw) {
            map.removeControl(draw);
            draw = null;
            return;
        }
        if (!enabled || draw) return;
        draw = new window.MapboxDraw({
            displayControlsDefault: false,
            controls: { polygon: mode === "create", trash: mode === "create" },
            touchEnabled: true
        });
        map.addControl(draw, "top-left");
        if (!drawListenersRegistered) {
            map.on("draw.create", () => {
                clearExistingZoneLayer();
                updateGeoJsonField();
                setStatus("Geometría dibujada. La API valida la forma completa y no recorta ni mueve sus vértices.");
            });
            map.on("draw.update", () => {
                updateGeoJsonField();
                setStatus("Geometría modificada. El servidor comprobará que quede completamente dentro de Uruguay al guardar.");
            });
            map.on("draw.delete", updateGeoJsonField);
            drawListenersRegistered = true;
        }
    };

    const showDepartment = async code => {
        if (!code || !map) { removeDepartmentHighlight(); return; }
        try {
            const url = new URL(root.dataset.departmentUrl, window.location.origin);
            url.searchParams.set("codigo", code);
            const department = await readJson(url);
            const bounds = department.limites;
            if (!department.geometria || ![bounds?.oeste, bounds?.sur, bounds?.este, bounds?.norte].every(Number.isFinite))
                throw new Error("El departamento no contiene una geometría válida.");
            map.getSource("department-reference").setData({
                type: "Feature", properties: { code: department.codigo }, geometry: department.geometria
            });
            map.fitBounds([[bounds.oeste, bounds.sur], [bounds.este, bounds.norte]], { padding: 40, duration: 700 });
            setStatus("Referencia de " + department.nombre + " seleccionada. La zona se creará con una copia independiente de este contorno.");
        } catch (error) {
            removeDepartmentHighlight();
            setStatus(error.message || "No se pudo consultar el límite departamental local.");
        }
    };

    const setupCoveragePoint = () => {
        if (!root.dataset.longitudeInput || !root.dataset.latitudeInput) return;
        const longitude = document.getElementById(root.dataset.longitudeInput);
        const latitude = document.getElementById(root.dataset.latitudeInput);
        const selectedPoint = document.getElementById("selected-point");
        const queryButton = document.getElementById("query-coverage");
        const placeMarker = (lng, lat) => {
            if (pointMarker) pointMarker.remove();
            pointMarker = new window.mapboxgl.Marker({ color: "#b91c1c" }).setLngLat([lng, lat]).addTo(map);
        };
        map.on("click", event => {
            if (!longitude || !latitude) return;
            longitude.value = event.lngLat.lng.toString();
            latitude.value = event.lngLat.lat.toString();
            placeMarker(event.lngLat.lng, event.lngLat.lat);
            if (queryButton) queryButton.disabled = false;
            if (selectedPoint) selectedPoint.textContent = "Punto pendiente de validación: longitud " + longitude.value + ", latitud " + latitude.value + ".";
            setStatus("El punto todavía no está confirmado. La API comprobará que esté dentro de la referencia de Uruguay.");
        });
        const initialLongitude = Number(root.dataset.coverageLongitude);
        const initialLatitude = Number(root.dataset.coverageLatitude);
        if (longitude?.value && latitude?.value && Number.isFinite(initialLongitude) && Number.isFinite(initialLatitude)) {
            placeMarker(initialLongitude, initialLatitude);
            if (queryButton) queryButton.disabled = false;
            if (selectedPoint) selectedPoint.textContent = "Punto pendiente de validación: longitud " + longitude.value + ", latitud " + latitude.value + ".";
        }
    };

    const startMap = data => {
        const bounds = data?.limites;
        const geometry = data?.geometria;
        if (!geometry || ![bounds?.oeste, bounds?.sur, bounds?.este, bounds?.norte].every(Number.isFinite)
            || bounds.oeste >= bounds.este || bounds.sur >= bounds.norte)
            throw new Error("La referencia territorial de Uruguay no contiene geometría o límites válidos.");
        territory = data;
        const rectangle = [[bounds.oeste, bounds.sur], [bounds.este, bounds.norte]];
        map = new window.mapboxgl.Map({
            container: root,
            style: root.dataset.style,
            center: [(bounds.oeste + bounds.este) / 2, (bounds.sur + bounds.norte) / 2],
            zoom: 0,
            maxBounds: rectangle,
            renderWorldCopies: false,
            maxZoom: 19
        });
        map.addControl(new window.mapboxgl.NavigationControl(), "top-right");
        map.on("error", () => setStatus("Mapbox no pudo cargar el mapa. No se modificó ni guardó ninguna geometría."));
        map.on("load", () => {
            map.addSource("uruguay-reference", { type: "geojson", data: { type: "Feature", properties: {}, geometry } });
            map.addLayer({
                id: "uruguay-reference-outline", type: "line", source: "uruguay-reference",
                paint: { "line-color": "#165f46", "line-width": 2.5, "line-opacity": 0.9 }
            });
            map.addSource("department-reference", { type: "geojson", data: { type: "FeatureCollection", features: [] } });
            map.addLayer({
                id: "department-reference-fill", type: "fill", source: "department-reference",
                paint: { "fill-color": "#e2a800", "fill-opacity": 0.16 }
            });
            map.addLayer({
                id: "department-reference-outline", type: "line", source: "department-reference",
                paint: { "line-color": "#a16207", "line-width": 2.5 }
            });

            const camera = map.cameraForBounds(rectangle, { padding: 36 });
            if (!camera || !Number.isFinite(camera.zoom)) {
                setStatus("No se pudo calcular la vista territorial a partir del catálogo.");
                root.style.visibility = "visible";
                return;
            }
            map.setMinZoom(Math.max(0, camera.zoom - 1));
            map.fitBounds(rectangle, { padding: 36, duration: 0 });
            root.style.visibility = "visible";

            let geometryValue = null;
            try { geometryValue = root.dataset.geometry ? JSON.parse(root.dataset.geometry) : null; }
            catch { setStatus("No se pudo leer la geometría guardada; no se reemplazó su valor."); }
            const canEdit = root.dataset.editable === "true" && safelyEditable(geometryValue) && Boolean(window.MapboxDraw);
            const canCreate = mode === "create" && Boolean(window.MapboxDraw);
            if (geometryValue && !canEdit && !canCreate) addExistingZoneLayer(geometryValue);
            else if (geometryValue && mode === "create" && !safelyEditable(geometryValue)) addExistingZoneLayer(geometryValue);
            if (canEdit || canCreate) {
                enableDraw(true);
                if (geometryValue && safelyEditable(geometryValue)) addExistingToDraw(geometryValue);
            }
            if (mode === "create") updateGeometrySource();
            else if (root.dataset.editable === "true" && !canEdit)
                setStatus("Esta geometría incluye componentes o huecos que el editor no conserva; se muestra en modo lectura.");
            else setStatus("El mapa muestra la referencia territorial de Uruguay. La API valida las operaciones geográficas.");
            if (departmentSelect?.value) void showDepartment(departmentSelect.value);
            setupCoveragePoint();
        });
    };

    const onMap = Boolean(root);
    mode = root?.dataset.mode;
    if (departmentSource) {
        departmentSelect = departmentSource.querySelector("select");
        departmentSelect?.addEventListener("change", () => void showDepartment(departmentSelect.value));
    }
    sourceRadios.forEach(radio => radio.addEventListener("change", updateGeometrySource));
    updateGeometrySource();

    if (!onMap || !root.dataset.token) return;
    if (!window.mapboxgl) {
        setStatus("Mapbox GL JS no se pudo cargar. No se modificó ni guardó la geometría.");
        return;
    }
    window.mapboxgl.accessToken = root.dataset.token;
    root.style.visibility = "hidden";
    if (!root.dataset.territoryUrl) {
        setStatus("La consulta de la referencia territorial no está disponible; no se mostró un mapa alternativo.");
        return;
    }
    void readJson(root.dataset.territoryUrl)
        .then(startMap)
        .catch(error => {
            root.style.visibility = "visible";
            setStatus(error.message || "No se pudo cargar el catálogo territorial de Uruguay.");
        });

    const editVertices = document.getElementById("edit-vertices");
    if (editVertices) editVertices.addEventListener("click", () => {
        if (draw && drawFeatureId) {
            draw.changeMode("direct_select", { featureId: drawFeatureId });
            setStatus("Arrastrá los vértices para ajustar el contorno. El servidor valida la geometría completa al guardar.");
        }
    });
})();
