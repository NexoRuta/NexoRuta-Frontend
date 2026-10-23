(() => {
    let map;
    let draw;
    let drawFeatureId;
    let pointMarker;
    let territory;
    let referenceData;
    let departmentLayerAvailable = false;
    let mapReady = false;
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
    const zoneForm = document.getElementById("zone-form");
    const departmentReferenceStatus = document.getElementById("department-reference-status");
    const retryDepartmentReference = document.getElementById("retry-department-reference");
    const retryMap = document.getElementById("retry-map");
    const createButton = document.getElementById("create-zone-submit");
    const setStatus = message => { if (status) status.textContent = message; };
    const setRetryVisible = visible => { if (retryMap) retryMap.hidden = !visible; };
    const setDepartmentRetryVisible = visible => { if (retryDepartmentReference) retryDepartmentReference.hidden = !visible; };
    const setDepartmentReferenceStatus = message => {
        if (departmentReferenceStatus) departmentReferenceStatus.textContent = message;
    };

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
            if (mode === "create" && draw && map) enableDraw(true);
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
        if (!response.ok) {
            const error = new Error(body?.message || "No se pudo consultar la referencia territorial.");
            error.code = body?.code;
            error.status = response.status;
            throw error;
        }
        return body;
    };
    const updateDepartmentOptions = departments => {
        const select = document.getElementById("CodigoDepartamento");
        const previousCode = select?.value || zoneForm?.dataset.selectedDepartment;
        if (select) {
            const options = departments.map(item => new Option(item.nombre + " (" + item.codigo + ")", item.codigo));
            select.replaceChildren(new Option("Seleccioná un departamento", ""), ...options);
            if (departments.some(item => item.codigo === previousCode)) select.value = previousCode;
        }
        const departmentRadio = document.getElementById("source-department");
        if (departmentRadio) departmentRadio.disabled = departments.length === 0;
        if (departmentRadio && document.getElementById("source-drawing")?.disabled && departments.length > 0)
            departmentRadio.checked = true;
        if (select) select.disabled = departments.length === 0 || sourceIsDrawing();
        updateGeometrySource();
        updateCreateAvailability();
        return departments.length > 0;
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
        updateCreateAvailability();
    };

    const updateCreateAvailability = () => {
        if (mode !== "create" || !createButton) return;
        const name = document.getElementById("Nombre")?.value?.trim();
        const sourceReady = sourceIsDrawing()
            ? Boolean(root?.dataset.token && geometryInput?.value)
            : Boolean(referenceData && departmentSelect?.value);
        createButton.disabled = !name || !sourceReady;
    };

    const officialDepartmentCodes = new Map([
        ["ARTIGAS", "UY-AR"], ["CANELONES", "UY-CA"], ["CERRO LARGO", "UY-CL"],
        ["COLONIA", "UY-CO"], ["DURAZNO", "UY-DU"], ["FLORES", "UY-FS"],
        ["FLORIDA", "UY-FD"], ["LAVALLEJA", "UY-LA"], ["MALDONADO", "UY-MA"],
        ["MONTEVIDEO", "UY-MO"], ["PAYSANDÚ", "UY-PA"], ["RÍO NEGRO", "UY-RN"],
        ["RIVERA", "UY-RV"], ["ROCHA", "UY-RO"], ["SALTO", "UY-SA"],
        ["SAN JOSÉ", "UY-SJ"], ["SORIANO", "UY-SO"], ["TACUAREMBÓ", "UY-TA"],
        ["TREINTA Y TRES", "UY-TT"]
    ]);

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
        if (!code) {
            removeDepartmentHighlight();
            updateCreateAvailability();
            return;
        }
        if (mode === "create") {
            const department = referenceData?.byCode.get(code);
            if (!department) {
                removeDepartmentHighlight();
                setStatus("No se encontró ese departamento en los límites oficiales cargados.");
                updateCreateAvailability();
                return;
            }
            if (!map) {
                setStatus("Referencia oficial de " + department.nombre + " seleccionada para copiarse como zona independiente.");
                updateCreateAvailability();
                return;
            }
            const source = map.getSource("department-reference");
            if (source) source.setData({
                type: "Feature", properties: { code: department.codigo }, geometry: department.geometria
            });
            map.fitBounds([[department.limites.oeste, department.limites.sur],
                [department.limites.este, department.limites.norte]], { padding: 40, duration: 400 });
            setStatus("Referencia oficial de " + department.nombre + " seleccionada. Se guardará una copia independiente.");
            updateCreateAvailability();
            return;
        }
        if (!map) { removeDepartmentHighlight(); return; }
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
        map.on("error", () => {
            root.style.visibility = "visible";
            setStatus("No se pudo cargar el mapa. Revisá la conexión con el proveedor de mapas e intentá de nuevo.");
            setRetryVisible(true);
        });
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
            setRetryVisible(false);

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
            if (mode === "create" && !window.MapboxDraw) {
                setStatus("El mapa se cargó, pero no se pudieron cargar los controles de dibujo.");
                setRetryVisible(true);
            } else if (mode === "create") {
                updateGeometrySource();
                setStatus("El mapa muestra la referencia territorial de Uruguay. La API valida las operaciones geográficas.");
            }
            else if (root.dataset.editable === "true" && !canEdit)
                setStatus("Esta geometría incluye componentes o huecos que el editor no conserva; se muestra en modo lectura.");
            else setStatus("El mapa muestra la referencia territorial de Uruguay. La API valida las operaciones geográficas.");
            if (departmentSelect?.value) void showDepartment(departmentSelect.value);
            setupCoveragePoint();
        });
    };

    const readOfficialDepartmentAsset = async () => {
        const url = zoneForm?.dataset.departmentAssetUrl || root?.dataset.departmentAssetUrl;
        if (!url) throw new Error("No se encontró el asset departamental oficial.");
        const response = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/geo+json, application/json" } });
        if (!response.ok) throw new Error("El asset departamental no está disponible.");
        const data = await response.json();
        if (data?.type !== "FeatureCollection" || !Array.isArray(data.features))
            throw new Error("El asset departamental no contiene un GeoJSON válido.");

        const groups = new Map();
        const bounds = { oeste: Infinity, sur: Infinity, este: -Infinity, norte: -Infinity };
        const visit = (coordinates, groupBounds) => {
            if (!Array.isArray(coordinates) || coordinates.length === 0)
                throw new Error("El asset contiene coordenadas vacías.");
            if (coordinates.length === 2 && coordinates.every(Number.isFinite)) {
                const [longitude, latitude] = coordinates;
                if (longitude < -180 || longitude > 180 || latitude < -90 || latitude > 90)
                    throw new Error("El asset contiene coordenadas fuera de WGS 84.");
                for (const target of [bounds, groupBounds]) {
                    target.oeste = Math.min(target.oeste, longitude);
                    target.sur = Math.min(target.sur, latitude);
                    target.este = Math.max(target.este, longitude);
                    target.norte = Math.max(target.norte, latitude);
                }
                return;
            }
            for (const child of coordinates) visit(child, groupBounds);
        };

        for (const feature of data.features) {
            const name = feature?.properties?.DEPTO?.trim();
            const code = officialDepartmentCodes.get(name);
            if (!code || feature?.type !== "Feature" || !feature.geometry)
                throw new Error("El asset contiene un departamento sin código o geometría oficial.");
            const geometry = feature.geometry;
            const polygons = geometry.type === "Polygon" ? [geometry.coordinates]
                : geometry.type === "MultiPolygon" ? geometry.coordinates : null;
            if (!Array.isArray(polygons) || polygons.length === 0)
                throw new Error("El asset contiene una geometría departamental no compatible.");
            let group = groups.get(code);
            if (!group) {
                group = { codigo: code, nombre: name, coordinates: [], limites: { oeste: Infinity, sur: Infinity, este: -Infinity, norte: -Infinity } };
                groups.set(code, group);
            }
            for (const polygon of polygons) {
                visit(polygon, group.limites);
                group.coordinates.push(polygon);
            }
        }
        if (groups.size !== officialDepartmentCodes.size
            || !Number.isFinite(bounds.oeste) || !Number.isFinite(bounds.sur)
            || !Number.isFinite(bounds.este) || !Number.isFinite(bounds.norte))
            throw new Error("El asset no contiene los 19 departamentos oficiales completos.");

        const departments = Array.from(groups.values()).sort((left, right) => left.codigo.localeCompare(right.codigo));
        const byCode = new Map(departments.map(department => [department.codigo, {
            codigo: department.codigo,
            nombre: department.nombre,
            limites: department.limites,
            geometria: { type: "MultiPolygon", coordinates: department.coordinates }
        }]));
        return { geoJson: data, bounds, departments, byCode };
    };

    const addCreateReferenceLayers = () => {
        if (!map || !mapReady || mode !== "create") return false;
        try {
            if (!map.getSource("department-reference"))
                map.addSource("department-reference", { type: "geojson", data: { type: "FeatureCollection", features: [] } });
            if (!map.getLayer("department-reference-fill"))
                map.addLayer({
                    id: "department-reference-fill", type: "fill", source: "department-reference",
                    paint: { "fill-color": "#e2a800", "fill-opacity": 0.2 }
                });
            if (!map.getLayer("department-reference-outline"))
                map.addLayer({
                    id: "department-reference-outline", type: "line", source: "department-reference",
                    paint: { "line-color": "#a16207", "line-width": 2.5 }
                });
            if (!referenceData) return false;
            const existing = map.getSource("uruguay-reference");
            if (existing) existing.setData(referenceData.geoJson);
            else
                map.addSource("uruguay-reference", { type: "geojson", data: referenceData.geoJson });
            if (!map.getLayer("uruguay-reference-fill"))
                map.addLayer({
                    id: "uruguay-reference-fill", type: "fill", source: "uruguay-reference",
                    paint: { "fill-color": "#dcefe6", "fill-opacity": 0.12 }
                });
            if (!map.getLayer("uruguay-reference-outline"))
                map.addLayer({
                    id: "uruguay-reference-outline", type: "line", source: "uruguay-reference",
                    paint: { "line-color": "#165f46", "line-width": 2, "line-opacity": 0.9 }
                });
            departmentLayerAvailable = true;
            const bounds = referenceData.bounds;
            const rectangle = [[bounds.oeste, bounds.sur], [bounds.este, bounds.norte]];
            map.setMaxBounds(rectangle);
            if (!geometryInput?.value && !departmentSelect?.value) {
                const camera = map.cameraForBounds(rectangle, { padding: 36 });
                if (camera && Number.isFinite(camera.zoom)) {
                    map.setMinZoom(Math.max(0, camera.zoom - 1));
                    map.fitBounds(rectangle, { padding: 36, duration: 0 });
                }
            }
            if (departmentSelect?.value) void showDepartment(departmentSelect.value);
            return true;
        } catch {
            departmentLayerAvailable = false;
            setDepartmentReferenceStatus("No se pudo mostrar la capa departamental oficial. La copia desde el archivo sigue disponible; podés reintentar la capa.");
            setDepartmentRetryVisible(true);
            return false;
        }
    };

    const startCreateMap = () => {
        if (!root || !window.mapboxgl) {
            if (root) root.style.visibility = "visible";
            setStatus("No se pudo cargar Mapbox. El dibujo necesita que el proveedor de mapas esté disponible; podés reintentar.");
            setRetryVisible(true);
            return false;
        }
        mapReady = false;
        window.mapboxgl.accessToken = root.dataset.token;
        const bounds = referenceData?.bounds;
        const rectangle = bounds ? [[bounds.oeste, bounds.sur], [bounds.este, bounds.norte]] : null;
        map = new window.mapboxgl.Map({
            container: root,
            style: root.dataset.style,
            center: bounds ? [(bounds.oeste + bounds.este) / 2, (bounds.sur + bounds.norte) / 2] : [-56, -33],
            zoom: bounds ? 4 : 4.5,
            ...(rectangle ? { maxBounds: rectangle } : {}),
            renderWorldCopies: false,
            maxZoom: 19
        });
        map.addControl(new window.mapboxgl.NavigationControl(), "top-right");
        map.on("error", () => {
            root.style.visibility = "visible";
            setStatus("No se pudo cargar el mapa. Revisá la conexión con el proveedor de mapas e intentá de nuevo.");
            setRetryVisible(true);
        });
        map.on("load", () => {
            mapReady = true;
            root.style.visibility = "visible";
            addCreateReferenceLayers();
            let geometryValue = null;
            try { geometryValue = geometryInput?.value ? JSON.parse(geometryInput.value) : null; }
            catch { setStatus("No se pudo leer la geometría dibujada; se conservó su valor para corregirlo."); }
            if (mode === "create" || (geometryValue && safelyEditable(geometryValue))) {
                enableDraw(true);
                if (geometryValue && safelyEditable(geometryValue)) addExistingToDraw(geometryValue);
            }
            updateGeometrySource();
            if (!window.MapboxDraw) {
                setStatus("Mapbox cargó el mapa, pero faltan los controles de dibujo. Podés reintentar.");
                setRetryVisible(true);
            } else if (referenceData && departmentLayerAvailable) {
                setStatus("Mapa listo con los límites departamentales oficiales. El servidor valida cada geometría al guardar.");
                setRetryVisible(false);
            } else if (referenceData) {
                setStatus("Mapa listo. No se pudo mostrar la capa departamental; el dibujo propio sigue disponible.");
            } else {
                setStatus("Mapa listo. La capa departamental no está disponible; el dibujo propio sigue habilitado.");
            }
            if (departmentSelect?.value) void showDepartment(departmentSelect.value);
            updateCreateAvailability();
        });
        return true;
    };

    const loadDepartmentReference = async () => {
        if (!zoneForm || mode !== "create") return false;
        setDepartmentReferenceStatus("Cargando límites departamentales oficiales…");
        setDepartmentRetryVisible(false);
        try {
            referenceData = await readOfficialDepartmentAsset();
            updateDepartmentOptions(referenceData.departments);
            const layerLoaded = !mapReady || addCreateReferenceLayers();
            setDepartmentReferenceStatus(layerLoaded
                ? "19 departamentos oficiales disponibles para seleccionar y copiar."
                : "No se pudo mostrar la capa departamental oficial. La copia desde el archivo sigue disponible; podés reintentar la capa.");
            setDepartmentRetryVisible(!layerLoaded);
            if (mapReady && layerLoaded) setStatus("Mapa listo con los límites departamentales oficiales. El servidor valida cada geometría al guardar.");
            return true;
        } catch {
            if (!referenceData) updateDepartmentOptions([]);
            setDepartmentReferenceStatus("No se pudo cargar la capa departamental oficial. El dibujo propio sigue disponible; podés reintentar.");
            setDepartmentRetryVisible(true);
            if (mapReady) setStatus("La capa departamental no se pudo cargar. El mapa y el dibujo propio siguen disponibles.");
            return false;
        }
    };

    const loadScript = (url, globalName) => {
        if (window[globalName]) return Promise.resolve();
        return new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = url;
            script.onload = () => window[globalName] ? resolve() : reject(new Error("No se registró la biblioteca esperada."));
            script.onerror = () => reject(new Error("No se pudo cargar la biblioteca de mapas."));
            document.head.append(script);
        });
    };

    const loadMapboxLibraries = async () => {
        if (!root) return false;
        for (const url of [root.dataset.mapboxStyleUrl, root.dataset.drawStyleUrl]) {
            if (!url) continue;
            const absoluteUrl = new URL(url, document.baseURI).href;
            if ([...document.querySelectorAll('link[rel="stylesheet"]')].some(link => link.href === absoluteUrl)) continue;
            const link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = absoluteUrl;
            document.head.append(link);
        }
        if (!window.mapboxgl) await loadScript(root.dataset.mapboxScriptUrl, "mapboxgl");
        if (!window.MapboxDraw) await loadScript(root.dataset.drawScriptUrl, "MapboxDraw");
        return true;
    };

    mode = root?.dataset.mode;
    if (departmentSource) {
        departmentSelect = departmentSource.querySelector("select");
        departmentSelect?.addEventListener("change", () => void showDepartment(departmentSelect.value));
    }
    sourceRadios.forEach(radio => radio.addEventListener("change", updateGeometrySource));
    document.getElementById("Nombre")?.addEventListener("input", updateCreateAvailability);
    updateGeometrySource();

    const territoryFailureMessage = error => error?.code === "territory-query-failed"
        ? "No se pudo consultar la referencia territorial para este mapa. Revisá la disponibilidad de la API e intentá de nuevo."
        : "La referencia territorial no está disponible para este mapa.";
    const resetMap = () => {
        if (map) map.remove();
        map = null;
        mapReady = false;
        draw = null;
        drawFeatureId = null;
        zoneGeometryLayer = false;
        drawListenersRegistered = false;
    };
    const loadTerritory = async () => {
        if (!root || !root.dataset.territoryUrl || !root.dataset.token) return false;
        if (!window.mapboxgl) {
            root.style.visibility = "visible";
            setStatus("No se pudo cargar el mapa. Revisá la conexión e intentá de nuevo.");
            setRetryVisible(true);
            return false;
        }
        let data;
        try {
            data = await readJson(root.dataset.territoryUrl);
        } catch (error) {
            root.style.visibility = "visible";
            setStatus(territoryFailureMessage(error));
            setRetryVisible(true);
            return false;
        }
        try {
            resetMap();
            root.style.visibility = "hidden";
            window.mapboxgl.accessToken = root.dataset.token;
            startMap(data);
            return true;
        } catch {
            root.style.visibility = "visible";
            setStatus("No se pudo cargar el mapa con la referencia territorial recibida.");
            setRetryVisible(true);
            return false;
        }
    };
    retryDepartmentReference?.addEventListener("click", async () => {
        if (retryDepartmentReference.disabled) return;
        retryDepartmentReference.disabled = true;
        retryDepartmentReference.setAttribute("aria-busy", "true");
        retryDepartmentReference.textContent = "Cargando límites…";
        try {
            await loadDepartmentReference();
        } finally {
            retryDepartmentReference.disabled = false;
            retryDepartmentReference.removeAttribute("aria-busy");
            retryDepartmentReference.textContent = "Reintentar límites departamentales";
        }
    });

    retryMap?.addEventListener("click", async () => {
        if (retryMap.disabled) return;
        retryMap.disabled = true;
        retryMap.setAttribute("aria-busy", "true");
        retryMap.textContent = "Cargando mapa…";
        setStatus("Cargando Mapbox…");
        try {
            if (mode === "create") {
                await loadMapboxLibraries();
                resetMap();
                startCreateMap();
            } else {
                await loadMapboxLibraries();
                await loadTerritory();
            }
        } catch {
            setStatus("No se pudo cargar Mapbox. Revisá la conexión con el proveedor de mapas e intentá de nuevo.");
            setRetryVisible(true);
        } finally {
            retryMap.disabled = false;
            retryMap.removeAttribute("aria-busy");
            retryMap.textContent = "Reintentar mapa";
        }
    });

    if (mode === "create") {
        void loadDepartmentReference();
        if (root?.dataset.token) {
            if (window.mapboxgl) startCreateMap();
            else {
                root.style.visibility = "visible";
                setStatus("No se pudo cargar Mapbox. El dibujo necesita que el proveedor de mapas esté disponible; podés reintentar.");
                setRetryVisible(true);
            }
        }
        updateCreateAvailability();
    } else if (root?.dataset.token) {
        if (!window.mapboxgl) {
            root.style.visibility = "visible";
            setStatus("No se pudo cargar el mapa. Revisá la conexión e intentá de nuevo.");
            setRetryVisible(true);
        } else {
            root.style.visibility = "hidden";
            window.mapboxgl.accessToken = root.dataset.token;
            void loadTerritory();
        }
    }

    const editVertices = document.getElementById("edit-vertices");
    if (editVertices) editVertices.addEventListener("click", () => {
        if (draw && drawFeatureId) {
            draw.changeMode("direct_select", { featureId: drawFeatureId });
            setStatus("Arrastrá los vértices para ajustar el contorno. El servidor valida la geometría completa al guardar.");
        }
    });
})();
