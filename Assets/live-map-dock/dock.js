/*
 * RE-080 live map: the behaviour of the bottom command dock, after God's Eye View.
 *
 *   - Location and Visual Presets are trays: hover 850 ms opens, leaving for 1 s closes unless the
 *     pin is on, a click on the title toggles, Escape closes.
 *   - Location: one pill per city the fleet works in (the host sends them, from the routes'
 *     garages); a click flies there and shows its garages as Q W E R T landmarks. The search asks
 *     the host, which geocodes through Raphael.Api's routing cache, never Google from here.
 *   - Voice: OpenAI Realtime over WebRTC, as God's Eye does it; the host holds the key and mints
 *     a short-lived secret. Without OPENAI_API_KEY it fails exactly as God's Eye does.
 *   - Visual presets 1-7 filter the map; Map source picks the Google map type.
 */
(function () {
    'use strict';

    var $ = function (id) { return document.getElementById(id); };

    // The page's own texts in the user's language (?lang=es): a dictionary keyed by the English text.
    // T() for strings built in code; translatePage() for the markup (text, title, placeholder,
    // aria-label). HUD codes (MIC, STD, NVG, FLIR...) and provider names are not translated.
    var PAGE_LANG = (new URLSearchParams(location.search).get('lang') || 'en').slice(0, 2);
    var ES = {
        "SEARCH": "BÚSQUEDA",
        "Expand SEARCH": "Expandir BÚSQUEDA",
        "Collapse panel": "Plegar panel",
        "Pin search tray": "Fijar la bandeja de búsqueda",
        "Keep search tray open": "Mantener abierta la bandeja de búsqueda",
        "Search a line": "Buscar una línea",
        "Search a line, driver, plate or vehicle...": "Buscar línea, driver, placa o vehículo...",
        "Search a line by name, driver, plate or vehicle": "Buscar una línea por nombre, driver, placa o vehículo",
        "Search a trip": "Buscar un viaje",
        "Search a trip by Trip Id or patient name...": "Buscar un viaje por Trip Id o paciente...",
        "Search a trip by Trip Id or patient name": "Buscar un viaje por Trip Id o nombre del paciente",
        "Search any location": "Buscar cualquier lugar",
        "Search any location...": "Buscar cualquier lugar...",
        "Search location by name or coordinates": "Buscar un lugar por nombre o coordenadas",
        "No line matches": "Ninguna línea coincide",
        "No trip matches today": "Ningún viaje de hoy coincide",
        "Landmark: --": "Punto: --",
        "Landmark: ": "Punto: ",
        "📍 Location: ": "📍 Lugar: ",
        "Navigation, voice, and visual preset controls": "Controles de navegación, voz y estilos visuales",
        "AI AGENT": "AGENTE IA",
        "VOICE STANDBY": "VOZ EN ESPERA",
        "VOICE ACTIVE": "VOZ ACTIVA",
        "VOICE UNAVAILABLE": "VOZ NO DISPONIBLE",
        "VOICE CONTROL": "CONTROL POR VOZ",
        "VOICE SYSTEM ERROR": "ERROR DEL SISTEMA DE VOZ",
        "DISMISS": "CERRAR",
        "Space turns the voice on and off": "Espacio enciende y apaga la voz",
        "Check microphone permission and network access, then try again.": "Revisa el permiso del micrófono y la conexión, y vuelve a intentarlo.",
        "Voice control — press to turn it on or off; Space does the same": "Control por voz: púlsalo para encender o apagar; Espacio hace lo mismo",
        "Estimated session cost": "Coste estimado de la sesión",
        "OFF": "APAGADO",
        "CONNECTING": "CONECTANDO",
        "LISTENING": "ESCUCHANDO",
        "EXECUTING": "EJECUTANDO",
        "ERROR": "ERROR",
        "Requesting microphone": "Pidiendo el micrófono",
        "Ask or command": "Pregunta u ordena",
        "Voice session could not be started.": "No se pudo iniciar la sesión de voz.",
        "Failed to create Realtime token": "No se pudo crear el token de Realtime",
        "WebRTC microphone support unavailable": "Micrófono WebRTC no disponible",
        "WebRTC connection lost": "Se perdió la conexión WebRTC",
        "Microphone permission denied": "Permiso del micrófono denegado",
        "Realtime connection: ": "Conexión Realtime: ",
        "Realtime error": "Error de Realtime",
        "Voice model: ": "Modelo de voz: ",
        "OPENAI_API_KEY is not set": "OPENAI_API_KEY no está configurada",
        "Expand Visual Presets": "Expandir Estilos visuales",
        "Pin visual presets": "Fijar estilos visuales",
        "Keep visual presets open": "Mantener abiertos los estilos visuales",
        "Show the map without a visual filter.": "El mapa sin filtro visual.",
        "Emulate a green phosphor CRT with scanlines and screen curvature.": "Imita un monitor CRT de fósforo verde con líneas y pantalla curva.",
        "Simulate night-vision goggles with green intensification and a tube vignette.": "Simula gafas de visión nocturna con intensificación verde y viñeta de tubo.",
        "Simulate FLIR-style thermal contrast.": "Simula el contraste térmico de una cámara FLIR.",
        "Apply bright cel-shaded color and illustrated outlines.": "Color brillante tipo dibujo animado con contornos.",
        "Apply high-contrast monochrome film-noir grading.": "Blanco y negro de alto contraste, de cine negro.",
        "Add a cold, snowy whiteout treatment to the scene.": "Tratamiento frío y nevado de la escena.",
        "MAP SOURCE": "TIPO DE MAPA",
        "Map source": "Tipo de mapa",
        "Map · No labels": "Mapa · Sin etiquetas",
        "Google Map": "Mapa de Google",
        "Satellite": "Satélite",
        "Sat · No labels": "Satélite · Sin etiquetas",
        "Google Map without labels": "Mapa de Google sin etiquetas",
        "Google Satellite with labels": "Satélite de Google con etiquetas",
        "Google Satellite without labels": "Satélite de Google sin etiquetas",
        "Google photorealistic 3D": "3D fotorrealista de Google",
        "Style": "Estilo",
        "ACTIVE STYLE": "ESTILO ACTIVO",
        "DISPATCH STATION · PROVIDER SETTINGS": "ESTACIÓN DE DESPACHO · PROVEEDORES",
        "Power up the live map": "Potencia el mapa en vivo",
        "The live map already runs on Raphael's Google Maps. Every key below switches on another capability for dispatch: voice control (OpenAI) and live traffic for the delays layers (Google Maps, TomTom). Keys are saved in this computer's appsettings.json, for your Windows user only, are tested with their provider before they are kept, and are never shown again.": "El mapa en vivo ya funciona con el Google Maps de Raphael. Cada clave de abajo activa otra capacidad para el despacho: el control por voz (OpenAI) y el tráfico en vivo de las capas de retrasos (Google Maps, TomTom). Las claves se guardan en el appsettings.json de este equipo, solo para tu usuario de Windows, se prueban con su proveedor antes de guardarlas y no se vuelven a mostrar.",
        "Each key is tested with its provider before it is saved.": "Cada clave se prueba con su proveedor antes de guardarla.",
        "SAVE KEYS": "GUARDAR CLAVES",
        "ESC to close": "ESC para cerrar",
        "Close key setup": "Cerrar la configuración de claves",
        "Voice control — talk to the live map": "Control por voz: habla con el mapa en vivo",
        "Delays on each route: live traffic from Google (Routes API), billed to this key": "Retrasos en cada ruta: tráfico en vivo de Google (Routes API), a cargo de esta clave",
        "Delays TomTom (trial): live traffic from TomTom, billed to this key": "Retrasos TomTom (prueba): tráfico en vivo de TomTom, a cargo de esta clave",
        "Voice with Claude": "Voz con Claude",
        "Voice with Grok": "Voz con Grok",
        "Voice with any model through OpenRouter": "Voz con cualquier modelo a través de OpenRouter",
        "Maps and traffic from Mapbox": "Mapas y tráfico de Mapbox",
        "Maps and live traffic from HERE": "Mapas y tráfico en vivo de HERE",
        "Maps, places and routes from Amazon": "Mapas, lugares y rutas de Amazon",
        "Metered — a billing-enabled account": "De pago: cuenta con facturación",
        "Free key — register, paste, done": "Clave gratuita: regístrate, pega y listo",
        "Coming later — not available yet": "Más adelante: todavía no disponible",
        "GET KEY ↗": "OBTENER CLAVE ↗",
        "MANAGE ↗": "GESTIONAR ↗",
        " saved — paste to replace": " guardada · pega otra para sustituirla",
        "paste ": "pega ",
        "POWER UP · ": "POWER UP · ",
        "KEY": "CLAVE",
        "KEYS": "CLAVES",
        " WAITING": " PENDIENTES",
        "POWERED UP": "TODO ACTIVO",
        "Paste at least one key first.": "Pega antes al menos una clave.",
        "Saving…": "Guardando…",
        "Saved in this computer’s appsettings.json for your Windows user. Voice uses the new key on its next session.": "Guardado en el appsettings.json de este equipo para tu usuario de Windows. La voz usa la clave nueva en su próxima sesión.",
        "Not saved: the settings file could not be written.": "No guardado: no se pudo escribir el fichero de configuración."
};
    /** Errors like 'Realtime connection: OPENAI_API_KEY is not set': each known piece translated. */
    function translateError(msg) {
        if (PAGE_LANG !== 'es') return msg;
        return Object.keys(ES).sort(function (a, b) { return b.length - a.length; })
            .reduce(function (m, k) { return k.length > 6 && m.indexOf(k) >= 0 ? m.split(k).join(ES[k]) : m; }, msg);
    }
    function T(s) { return PAGE_LANG === 'es' && ES[s] != null ? ES[s] : s; }
    function translatePage(root) {
        if (PAGE_LANG !== 'es') return;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        var n; while ((n = walker.nextNode())) { var t = n.nodeValue.trim(); if (t && ES[t] != null) n.nodeValue = n.nodeValue.replace(t, ES[t]); }
        root.querySelectorAll('[title],[placeholder],[aria-label]').forEach(function (el) {
            ['title', 'placeholder', 'aria-label'].forEach(function (a) { var v = el.getAttribute(a); if (v && ES[v] != null) el.setAttribute(a, ES[v]); });
        });
    }
    ['command-dock', 'key-setup', 'key-setup-chip', 'style-indicator'].forEach(function (id) { var el = document.getElementById(id); if (el) translatePage(el); });
    var POI_KEYS = ['Q', 'W', 'E', 'R', 'T'];
    var STYLES = ['normal', 'retro', 'surveillance', 'thermal', 'anime', 'noir', 'snow'];

    function post(m) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(m); }
    function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) { return null; } }

    // ------------------------------------------------------------------ trays
    function tray(panel) {
        var toggle = panel.querySelector('.dock-tray-toggle');
        var pin = panel.querySelector('.dock-pin-btn');
        var openTimer = null, closeTimer = null;

        function set(collapsed) {
            panel.classList.toggle('collapsed', collapsed);
            toggle.setAttribute('aria-expanded', String(!collapsed));
            requestAnimationFrame(function () { if (typeof stack === 'function') stack(); });
        }
        function clear() { clearTimeout(openTimer); clearTimeout(closeTimer); openTimer = closeTimer = null; }

        toggle.addEventListener('click', function (e) { e.stopPropagation(); clear(); set(!panel.classList.contains('collapsed')); });
        panel.addEventListener('click', function (e) {
            if (e.target.closest('.panel-collapse-btn, .dock-tray-toggle')) return;
            clear();
            if (panel.classList.contains('collapsed')) set(false);
        });
        panel.addEventListener('pointerenter', function () {
            clearTimeout(closeTimer);
            if (!panel.classList.contains('collapsed')) return;
            openTimer = setTimeout(function () { if (panel.matches(':hover') && panel.classList.contains('collapsed')) set(false); }, 850);
        });
        panel.addEventListener('pointerleave', function () {
            clearTimeout(openTimer);
            closeTimer = setTimeout(function () {
                if (panel.matches(':hover') || panel.classList.contains('dock-pinned') || panel.classList.contains('collapsed')) return;
                var a = document.activeElement;
                if (a && panel.contains(a) && a.matches(':focus-visible')) return;
                set(true);
            }, 1000);
        });
        panel.addEventListener('keydown', function (e) { if (e.key === 'Escape') { clear(); set(true); } });
        pin.addEventListener('click', function (e) {
            e.stopPropagation();
            var on = !panel.classList.contains('dock-pinned');
            panel.classList.toggle('dock-pinned', on);
            pin.setAttribute('aria-pressed', String(on));
            document.querySelectorAll('#command-dock .dock-pinned-top').forEach(function (p) { p.classList.remove('dock-pinned-top'); });
            if (on) { panel.classList.add('dock-pinned-top'); set(false); }
            else {
                var rest = document.querySelector('#command-dock .dock-pinned');
                if (rest) rest.classList.add('dock-pinned-top');
                if (!panel.matches(':hover')) set(true);
            }
            stack();
        });
    }

    // God's Eye's tray stack: a pinned tray keeps its place and the other one opens above it.
    function stack() {
        var dock = $('command-dock');
        var h = function (sel) { var el = dock.querySelector(sel); return el ? Math.ceil(el.querySelector('.dock-popover-content').getBoundingClientRect().height) : 0; };
        var loc = h('#location-bar.dock-pinned:not(.collapsed)'), pre = h('#control-panel.dock-pinned:not(.collapsed)');
        var count = Number(loc > 0) + Number(pre > 0);
        var top = dock.querySelector('.dock-pinned-top.dock-pinned:not(.collapsed)');
        var lower = top && top.id === 'location-bar' ? pre : loc;
        dock.style.setProperty('--dock-location-pinned-height', loc + 'px');
        dock.style.setProperty('--dock-presets-pinned-height', pre + 'px');
        dock.style.setProperty('--dock-lower-pinned-height', lower + 'px');
        dock.style.setProperty('--dock-pinned-stack-height', count > 1 ? 'calc(' + loc + 'px + ' + pre + 'px + 1.2rem)' : (loc + pre) + 'px');
        dock.classList.toggle('dock-has-pinned-tray', count > 0);
        dock.classList.toggle('dock-has-two-pinned-trays', count > 1);
    }
    if (window.ResizeObserver) {
        var trayObserver = new ResizeObserver(function () { stack(); });
        document.querySelectorAll('#command-dock .dock-popover-content').forEach(function (t) { trayObserver.observe(t); });
    }
    tray($('location-bar'));
    tray($('control-panel'));

    // ------------------------------------------------------------------ location
    var cities = [];
    var expandedCity = null;

    function fly(lat, lng, zoom) { if (window.__liveMapFly) window.__liveMapFly(lat, lng, zoom); }

    function showPois(city) {
        var row = $('poi-row');
        row.replaceChildren();
        (city.pois || []).slice(0, POI_KEYS.length).forEach(function (poi, i) {
            var b = document.createElement('button');
            b.type = 'button'; b.className = 'poi-pill'; b.dataset.poiIndex = i;
            var k = document.createElement('span'); k.className = 'poi-pill-key'; k.textContent = POI_KEYS[i];
            var n = document.createElement('span'); n.className = 'poi-pill-name'; n.textContent = poi.name;
            b.append(k, n);
            b.addEventListener('click', function () { goPoi(city, i); });
            row.appendChild(b);
        });
        requestAnimationFrame(function () {
            row.classList.toggle('expanded', (city.pois || []).length > 0);
            $('location-bar-divider').classList.toggle('visible', (city.pois || []).length > 0);
        });
    }

    function goCity(city) {
        expandedCity = city;
        document.querySelectorAll('.location-pill').forEach(function (p) { p.classList.toggle('active', p.dataset.locationId === city.id); });
        $('location-mini-city').textContent = T('📍 Location: ') + city.name;
        $('location-mini-poi').textContent = T('Landmark: --');
        fly(city.lat, city.lng, 12);
        showPois(city);
    }

    function goPoi(city, i) {
        var poi = city.pois[i];
        if (!poi) return;
        $('location-mini-poi').textContent = T('Landmark: ') + poi.name;
        document.querySelectorAll('.poi-pill').forEach(function (p) { p.classList.toggle('active', +p.dataset.poiIndex === i); });
        fly(poi.lat, poi.lng, 16);
    }

    function setCities(items) {
        cities = items || [];
        var pills = $('location-pills');
        pills.replaceChildren();
        cities.forEach(function (c) {
            var b = document.createElement('button');
            b.type = 'button'; b.className = 'location-pill'; b.dataset.locationId = c.id; b.textContent = c.name;
            b.addEventListener('click', function () { goCity(c); });
            pills.appendChild(b);
        });
    }

    document.addEventListener('keydown', function (e) {
        if (!expandedCity || (e.target && e.target.matches && e.target.matches('select, input, textarea'))) return;
        var i = POI_KEYS.indexOf((e.key || '').toUpperCase());
        if (i !== -1 && i < (expandedCity.pois || []).length) goPoi(expandedCity, i);
    });

    $('search-toggle').addEventListener('click', function () {
        var s = $('location-search');
        s.classList.toggle('expanded');
        if (s.classList.contains('expanded')) s.focus();
    });
    $('location-search').addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') return;
        var q = this.value.trim();
        if (!q) return;
        var m = q.match(/^\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*$/);
        if (m) { fly(+m[1], +m[2], 15); return; }
        post({ type: 'geocode', q: q });
    });

    // ------------------------------------------------------------------ visual presets
    var LANG = (new URLSearchParams(location.search).get('lang') || 'en').slice(0, 2);
    var PRESETS_TITLE = { en: 'VISUAL PRESETS', es: 'ESTILOS VISUALES' };
    (function () {
        var title = document.querySelector('[aria-controls="control-panel-popover"] .panel-title');
        if (!title) return;
        var text = PRESETS_TITLE[LANG] || PRESETS_TITLE.en;
        title.lastChild.textContent = text;
        title.parentNode.setAttribute('aria-label', (LANG === 'es' ? 'Expandir ' : 'Expand ') + text);
    })();

    function setStyle(style, save) {
        if (STYLES.indexOf(style) === -1) style = 'normal';
        STYLES.forEach(function (s) { document.body.classList.toggle('lm-style-' + s, s === style && s !== 'normal'); });
        document.querySelectorAll('.style-btn').forEach(function (b) { b.classList.toggle('active', b.dataset.style === style); });
        var btn = document.querySelector('.style-btn[data-style="' + style + '"]');
        $('style-mini-value').textContent = btn ? btn.querySelector('.btn-label').textContent.toUpperCase() : 'NORMAL';
        $('active-style-name').textContent = btn ? btn.querySelector('.btn-label').textContent.toUpperCase() : 'NORMAL';
        post({ type: 'presetChanged', style: style });
        if (save) store('lm.visualPreset', style);
    }
    document.querySelectorAll('.style-btn').forEach(function (b) { b.addEventListener('click', function () { setStyle(b.dataset.style, true); }); });
    document.addEventListener('keydown', function (e) {
        if (e.target && e.target.matches && e.target.matches('select, input, textarea')) return;
        var n = parseInt(e.key, 10);
        if (n >= 1 && n <= STYLES.length && !e.ctrlKey && !e.altKey) setStyle(STYLES[n - 1], true);
    });
    setStyle(store('lm.visualPreset') || 'normal', false);

    var STACK_STATUS = { 'roadmap': 'MAP', 'roadmap-nolabels': 'MAP', 'hybrid': 'SAT', 'satellite': 'SAT', '3d': '3D' };
    function showStack(id) {
        document.querySelectorAll('.map-stack-chip').forEach(function (c) {
            var on = c.dataset.stackId === id;
            c.classList.toggle('active', on); c.setAttribute('aria-pressed', String(on));
        });
        $('map-stack-status').textContent = STACK_STATUS[id] || 'MAP';
    }
    document.querySelectorAll('.map-stack-chip').forEach(function (c) {
        c.addEventListener('click', function () {
            var id = c.dataset.stackId;
            showStack(id);
            // 3D sits over the 2D map; any other source takes it away again.
            if (window.__liveMap3d) window.__liveMap3d(id === '3d');
            if (id === '3d') return;
            var satellite = id === 'hybrid' || id === 'satellite';
            var labels = id === 'roadmap' || id === 'hybrid';
            if (window.__liveMapView) window.__liveMapView({ mapTypeId: satellite ? 'satellite' : 'roadmap', labels: labels });
            post({ type: 'viewChanged', mapTypeId: satellite ? 'satellite' : 'roadmap', labels: labels });
        });
    });

    // ------------------------------------------------------------------ voice (OpenAI Realtime, as God's Eye)
    // 1. The page asks the host for a token; the host holds OPENAI_API_KEY and mints a short-lived
    //    client secret with the session (model, voice, instructions, tools) already configured.
    // 2. The page opens the microphone and a WebRTC call straight to OpenAI with that secret.
    // 3. The model speaks back over the audio track and calls tools over the 'oai-events' channel;
    //    each call runs here or in the host, and its result goes back as function_call_output.
    var voice = $('gev-voice-control');
    var STATUS = { idle: 'OFF', connecting: 'CONNECTING', listening: 'LISTENING', executing: 'EXECUTING', error: 'ERROR' };
    var tier = store('godsEyeView.voiceCost.tier') === 'mini' ? 'mini' : 'standard';
    var rtc = null, tokenWaiter = null, pendingTools = {}, toolSeq = 0;

    function setStatus(status, detail) {
        voice.dataset.status = status;
        if (status === 'error') voice.classList.remove('error-dismissed');
        voice.querySelector('.gev-mic-label').textContent = 'MIC';
        $('gev-voice-status').textContent = T(STATUS[status] || STATUS.idle);
        var primary = T(status === 'error' ? 'VOICE UNAVAILABLE' : detail || (status === 'idle' ? 'VOICE STANDBY' : 'VOICE ACTIVE'));
        $('gev-voice-detail').textContent = primary;
        $('gev-voice-detail').title = primary;
        $('gev-voice-error-detail').textContent = status === 'error' ? translateError(detail || 'Voice session could not be started.') : '';
    }

    function syncTier() {
        var mini = tier === 'mini', b = $('gev-voice-tier');
        b.textContent = mini ? 'MINI' : 'STD';
        b.setAttribute('aria-pressed', mini ? 'true' : 'false');
        b.title = T('Voice model: ') + (mini ? 'gpt-realtime-2.1-mini' : 'gpt-realtime-2') + ' — click to switch to ' + (mini ? 'standard' : 'mini') + '; applies next session';
    }

    function requestToken() {
        return new Promise(function (resolve, reject) {
            tokenWaiter = { resolve: resolve, reject: reject };
            post({ type: 'voiceToken', tier: tier });
            setTimeout(function () { if (tokenWaiter) { tokenWaiter = null; reject(new Error('Failed to create Realtime token')); } }, 30000);
        });
    }

    function stop(keepStatus) {
        if (rtc) {
            try { rtc.dc && rtc.dc.close(); } catch (e) { }
            try { rtc.pc && rtc.pc.close(); } catch (e) { }
            if (rtc.stream) rtc.stream.getTracks().forEach(function (t) { t.stop(); });
            if (rtc.audio) rtc.audio.remove();
        }
        rtc = null; pendingTools = {};
        if (!keepStatus) setStatus('idle');
    }

    function send(event) { if (rtc && rtc.dc && rtc.dc.readyState === 'open') { try { rtc.dc.send(JSON.stringify(event)); return true; } catch (e) { } } return false; }

    async function start() {
        if (rtc) { stop(); return; }   // the mic button toggles the session
        if (!window.RTCPeerConnection || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            setStatus('error', 'WebRTC microphone support unavailable'); return;
        }
        var session = rtc = { epoch: Date.now() };
        setStatus('connecting', 'Requesting microphone');
        try {
            var minted = await requestToken();
            if (rtc !== session) return;
            session.stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 } });
            if (rtc !== session) { session.stream.getTracks().forEach(function (t) { t.stop(); }); return; }
            session.audio = document.createElement('audio');
            session.audio.autoplay = true; session.audio.style.display = 'none';
            document.body.appendChild(session.audio);

            var pc = session.pc = new RTCPeerConnection();
            pc.ontrack = function (e) { if (rtc === session) session.audio.srcObject = e.streams[0]; };
            pc.onconnectionstatechange = function () {
                if (rtc === session && (pc.connectionState === 'failed' || pc.connectionState === 'closed')) { stop(true); setStatus('error', 'WebRTC connection lost'); }
            };
            session.stream.getTracks().forEach(function (t) { pc.addTrack(t, session.stream); });

            var dc = session.dc = pc.createDataChannel('oai-events');
            dc.addEventListener('open', function () { if (rtc === session) setStatus('listening', 'Ask or command'); });
            dc.addEventListener('message', function (e) { if (rtc === session) onRealtimeEvent(e.data); });

            var offer = await pc.createOffer();
            await pc.setLocalDescription(offer);
            var res = await fetch('https://api.openai.com/v1/realtime/calls', {
                method: 'POST', body: offer.sdp, cache: 'no-store', redirect: 'error',
                headers: { Authorization: 'Bearer ' + minted.value, 'Content-Type': 'application/sdp' }
            });
            if (!res.ok) throw new Error('Realtime SDP failed: HTTP ' + res.status);
            var answer = await res.text();
            if (rtc !== session) return;
            await pc.setRemoteDescription({ type: 'answer', sdp: answer });
        } catch (err) {
            if (rtc !== session) return;
            stop(true);
            var msg = err && err.name === 'NotAllowedError' ? 'Microphone permission denied' : (err && err.message) || 'Voice session could not be started.';
            setStatus('error', 'Realtime connection: ' + msg);
        }
    }

    // The tools that change only the page run here; the rest go to the host (lines, trips, places).
    var PAGE_TOOLS = {
        set_visual_style: function (a) { if (STYLES.indexOf(a.style) === -1) return { ok: false, error: 'Unknown style' }; setStyle(a.style, true); return { ok: true, style: a.style }; },
        set_map_source: function (a) {
            var chip = document.querySelector('.map-stack-chip[data-stack-id="' + a.source + '"]');
            if (!chip) return { ok: false, error: 'Unknown map source' };
            chip.click(); return { ok: true, source: a.source };
        }
    };

    function runTool(name, argsJson) {
        if (PAGE_TOOLS[name]) {
            var args = {}; try { args = JSON.parse(argsJson || '{}'); } catch (e) { }
            return Promise.resolve(PAGE_TOOLS[name](args));
        }
        return new Promise(function (resolve) {
            var id = 'c' + (++toolSeq);
            pendingTools[id] = resolve;
            post({ type: 'voiceTool', callId: id, name: name, args: argsJson || '{}' });
            setTimeout(function () { if (pendingTools[id]) { delete pendingTools[id]; resolve({ ok: false, error: 'Timed out' }); } }, 20000);
        });
    }

    // Tool calls of one response run in order as they arrive; only after response.done, with every
    // call answered, is one response.create sent, so the model confirms the resulting state once
    // (God's Eye does the same: a call left unanswered strands the turn).
    var turnCalls = [], chain = Promise.resolve();
    function onRealtimeEvent(raw) {
        var ev; try { ev = JSON.parse(raw); } catch (e) { return; }
        if (ev.type === 'response.function_call_arguments.done') {
            setStatus('executing', ev.name.replace(/_/g, ' ').toUpperCase());
            var call = chain.then(function () { return runTool(ev.name, ev.arguments); }).then(function (output) {
                send({ type: 'conversation.item.create', item: { type: 'function_call_output', call_id: ev.call_id, output: JSON.stringify(output) } });
            });
            chain = call; turnCalls.push(call);
        }
        if (ev.type === 'response.done' && turnCalls.length) {
            var calls = turnCalls; turnCalls = [];
            Promise.all(calls).then(function () { send({ type: 'response.create' }); setStatus('listening', 'Ask or command'); });
        }
        if (ev.type === 'error' && ev.error) setStatus('listening', (ev.error.message || 'Realtime error').slice(0, 80));
    }

    $('gev-voice-button').addEventListener('click', start);
    $('gev-voice-tier').addEventListener('click', function (e) {
        e.stopPropagation();
        tier = tier === 'mini' ? 'standard' : 'mini';
        store('godsEyeView.voiceCost.tier', tier);
        syncTier();
    });
    voice.querySelector('.gev-voice-error-dismiss').addEventListener('click', function () { voice.classList.add('error-dismissed'); });
    document.addEventListener('keydown', function (e) {
        if (e.code !== 'Space' || e.repeat || !$('key-setup').hidden) return;
        var t = e.target;
        if (t && t.matches && t.matches('input, textarea, select, button, [role="button"]')) return;
        e.preventDefault();
        start();
    });
    syncTier();
    setStatus('idle');

    // ------------------------------------------------------------------ line and trip finders
    var lines = [];
    function norm(t) { return String(t || '').toLowerCase(); }
    function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

    function finder(inputId, listId, search, pick, empty) {
        var input = $(inputId), list = $(listId), items = [], active = -1;
        function close() { list.classList.remove('open'); list.replaceChildren(); items = []; active = -1; }
        function show(results) {
            if (!input.value.trim()) { close(); return; }
            list.replaceChildren(); items = results; active = results.length ? 0 : -1;
            if (!results.length) list.appendChild(el('div', 'lm-suggest-empty', empty));
            results.forEach(function (r, i) {
                var b = el('button', 'lm-suggest-item' + (i === 0 ? ' active' : ''));
                b.type = 'button';
                b.appendChild(el('span', 'lm-suggest-main', r.main));
                b.appendChild(el('span', 'lm-suggest-sub', r.sub));
                if (r.tag) b.appendChild(el('span', 'lm-suggest-tag' + (r.live ? ' live' : ''), r.tag));
                b.addEventListener('mousedown', function (e) { e.preventDefault(); choose(i); });
                list.appendChild(b);
            });
            list.classList.add('open');
        }
        function choose(i) { var r = items[i]; if (!r) return; input.value = r.main; close(); pick(r); }
        function mark() { list.querySelectorAll('.lm-suggest-item').forEach(function (b, i) { b.classList.toggle('active', i === active); }); }
        input.addEventListener('input', function () { var q = input.value.trim(); if (!q) { close(); return; } search(q, show); });
        input.addEventListener('focus', function () { if (input.value.trim()) search(input.value.trim(), show); });
        input.addEventListener('blur', function () { setTimeout(close, 120); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' && items.length) { active = (active + 1) % items.length; mark(); e.preventDefault(); }
            else if (e.key === 'ArrowUp' && items.length) { active = (active - 1 + items.length) % items.length; mark(); e.preventDefault(); }
            else if (e.key === 'Enter') { choose(active < 0 ? 0 : active); e.preventDefault(); }
            else if (e.key === 'Escape' && list.classList.contains('open')) { close(); e.stopPropagation(); }
        });
        return { show: show };
    }

    // Lines: the host sends them; the search runs here, on every keystroke, with no round trip.
    finder('line-search', 'line-suggest', function (q, show) {
        var t = norm(q);
        show(lines.filter(function (l) { return [l.name, l.driver, l.vehicle, l.plate].some(function (f) { return norm(f).indexOf(t) !== -1; }); })
            .slice(0, 12)
            .map(function (l) { return { id: l.id, main: l.name, sub: [l.driver, l.vehicle, l.plate].filter(Boolean).join(' · '), tag: l.group, live: l.onRoute }; }));
    }, function (r) { post({ type: 'selectLine', id: r.id }); }, T('No line matches'));

    // Trips: the host searches today's trips (Trip Id, patient) and ranks the one on board first.
    var tripTimer = null, tripSeq = 0;
    var tripShow = finder('trip-search', 'trip-suggest', function (q) {
        clearTimeout(tripTimer);
        tripTimer = setTimeout(function () { post({ type: 'tripSearch', q: q, seq: ++tripSeq }); }, 180);
    }, function (r) { post({ type: 'selectTrip', id: r.id, routeId: r.routeId }); }, T('No trip matches today')).show;

    // ------------------------------------------------------------------ POWER UP (provider keys)
    // OpenAI (voice) and Google Maps (the map's own key, delays layer) work today; the rest are shown
    // at the end, disabled, until each has its own code (realtime adapter, turn-based voice, map engine).
    var KEYS = [
        { id: 'openai', title: 'OPENAI', tier: 'metered', env: 'OPENAI_API_KEY', url: 'https://platform.openai.com/api-keys', unlocks: 'Voice control — talk to the live map' },
        { id: 'google-maps', title: 'GOOGLE MAPS', tier: 'metered', env: 'GOOGLE_MAPS_API_KEY', url: 'https://console.cloud.google.com/google/maps-apis/credentials', unlocks: 'Delays on each route: live traffic from Google (Routes API), billed to this key' },
        { id: 'tomtom', title: 'TOMTOM', tier: 'free', env: 'TOMTOM_API_KEY', url: 'https://developer.tomtom.com', unlocks: 'Delays TomTom (trial): live traffic from TomTom, billed to this key' },
        { id: 'anthropic', title: 'ANTHROPIC (CLAUDE)', tier: 'metered', env: 'ANTHROPIC_API_KEY', url: 'https://console.anthropic.com/settings/keys', unlocks: 'Voice with Claude', disabled: true },
        { id: 'xai', title: 'GROK (xAI)', tier: 'metered', env: 'XAI_API_KEY', url: 'https://console.x.ai', unlocks: 'Voice with Grok', disabled: true },
        { id: 'openrouter', title: 'OPENROUTER', tier: 'metered', env: 'OPENROUTER_API_KEY', url: 'https://openrouter.ai/settings/keys', unlocks: 'Voice with any model through OpenRouter', disabled: true },
        { id: 'mapbox', title: 'MAPBOX', tier: 'free', env: 'MAPBOX_ACCESS_TOKEN', url: 'https://account.mapbox.com/access-tokens/', unlocks: 'Maps and traffic from Mapbox', disabled: true },
        { id: 'here', title: 'HERE TECH', tier: 'free', env: 'HERE_API_KEY', url: 'https://platform.here.com/admin/apps', unlocks: 'Maps and live traffic from HERE', disabled: true },
        { id: 'aws-location', title: 'AMAZON LOCATION (AWS)', tier: 'metered', env: 'AWS_LOCATION_API_KEY', url: 'https://console.aws.amazon.com/location/', unlocks: 'Maps, places and routes from Amazon', disabled: true }
    ];
    var TIER_DOTS = { metered: '🔴', free: '🟡' };
    var chip = $('key-setup-chip'), dialog = $('key-setup'), rows = dialog.querySelector('[data-key-setup-rows]');
    var statusLine = dialog.querySelector('[data-key-setup-status]'), defaultStatus = statusLine.textContent, dialogOpen = false;

    var keyStatus = {};
    function renderKeys() {
        rows.replaceChildren();
        KEYS.forEach(function (k) {
            var set = !!keyStatus[k.env];
            var row = el('section', 'key-setup-row'); row.dataset.keyId = k.id; row.dataset.set = String(set);
            if (k.disabled) row.classList.add('key-setup-disabled');
            var head = el('div', 'key-setup-row-head');
            var led = el('span', 'key-setup-led'); led.setAttribute('aria-hidden', 'true');
            var tier = el('span', 'key-setup-tier', TIER_DOTS[k.tier]);
            tier.title = T(k.tier === 'metered' ? 'Metered — a billing-enabled account' : 'Free key — register, paste, done');
            head.append(led, el('strong', null, k.title), tier);
            if (k.exposed) { var ex = el('span', 'key-setup-exposed', 'browser-side'); ex.title = 'This key runs in the browser by design — restrict it at the provider'; head.append(ex); }
            var get = el('a', 'key-setup-get', T(set ? 'MANAGE ↗' : 'GET KEY ↗')); get.href = k.url; get.target = '_blank'; get.rel = 'noopener noreferrer';
            get.addEventListener('click', function (e) { e.preventDefault(); post({ type: 'openUrl', url: k.url }); });
            head.append(get);
            var fields = el('div', 'key-setup-fields');
            var input = el('input'); input.type = 'password'; input.autocomplete = 'off'; input.spellcheck = false;
            input.setAttribute('aria-label', k.env);
            if (k.disabled) { input.disabled = true; input.placeholder = T('Coming later — not available yet'); get.style.display = 'none'; }
            else if (k.readOnly) { input.disabled = true; input.placeholder = set ? k.env + ' configured by the installation' : k.env + ' missing in the installation'; }
            else { input.dataset.envVar = k.env; input.placeholder = set ? k.env + T(' saved — paste to replace') : T('paste ') + k.env; }
            fields.append(input);
            row.append(head, el('p', 'key-setup-unlocks', T(k.unlocks)), fields);
            rows.appendChild(row);
        });
        var missing = KEYS.filter(function (k) { return !k.disabled && !keyStatus[k.env]; }).length;
        chip.querySelector('[data-key-setup-chip-label]').textContent = missing > 0 ? 'POWER UP · ' + missing + ' ' + T(missing === 1 ? 'KEY' : 'KEYS') + T(' WAITING') : T('POWERED UP');
    }
    renderKeys();

    function openDialog() {
        if (dialogOpen) return; dialogOpen = true; dialog.hidden = false; post({ type: 'keyDialog', open: true });
        requestAnimationFrame(function () { if (!dialogOpen) return; dialog.classList.add('visible'); var i = dialog.querySelector('input'); if (i) i.focus({ preventScroll: true }); });
    }
    function closeDialog() {
        if (!dialogOpen) return; dialogOpen = false; dialog.classList.remove('visible'); post({ type: 'keyDialog', open: false });
        var hide = function () { if (!dialogOpen) dialog.hidden = true; };
        dialog.addEventListener('transitionend', hide, { once: true }); setTimeout(hide, 400);
        statusLine.textContent = defaultStatus;
    }
    chip.addEventListener('click', function () { if (dialogOpen) closeDialog(); else openDialog(); });
    dialog.querySelector('[data-key-setup-close]').addEventListener('click', closeDialog);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && dialogOpen) { e.preventDefault(); closeDialog(); } });
    dialog.querySelector('[data-key-setup-apply]').addEventListener('click', function () {
        var any = [].some.call(dialog.querySelectorAll('input[data-env-var]'), function (i) { return i.value.trim(); });
        if (!any) { statusLine.textContent = T('Paste at least one key first.'); return; }
        var keys = {};
        dialog.querySelectorAll('input[data-env-var]').forEach(function (i) { if (i.value.trim()) keys[i.dataset.envVar] = i.value.trim(); });
        statusLine.textContent = T('Saving…');
        post({ type: 'saveKeys', keys: keys });
    });

    // ------------------------------------------------------------------ host
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (e) {
            var m = e.data;
            if (!m || !m.type) return;
            if (m.type === 'cities') setCities(m.items);
            if (m.type === 'openSearch') {
                var bar = $('location-bar'), box = { lines: 'line-search', trips: 'trip-search', places: 'location-search' }[m.box];
                bar.classList.remove('collapsed'); bar.querySelector('.dock-tray-toggle, .panel-collapse-btn').setAttribute('aria-expanded', 'true'); stack();
                if (m.box === 'places') $('location-search').classList.add('expanded');
                if (box) setTimeout(function () { $(box).focus(); }, 120);
            }
            if (m.type === 'keysStatus') { keyStatus = m.keys || {}; renderKeys(); }
            if (m.type === 'keysSaved') {
                if (m.ok) dialog.querySelectorAll('input[data-env-var]').forEach(function (i) { i.value = ''; });
                statusLine.textContent = m.message || T(m.ok ? 'Saved in this computer’s appsettings.json for your Windows user. Voice uses the new key on its next session.' : 'Not saved: the settings file could not be written.');
            }
            if (m.type === 'voiceToken' && tokenWaiter) {
                var w = tokenWaiter; tokenWaiter = null;
                if (m.value) w.resolve({ value: m.value, model: m.model }); else w.reject(new Error(m.error || 'Failed to create Realtime token'));
            }
            if (m.type === 'voiceToolResult' && pendingTools[m.callId]) { var done = pendingTools[m.callId]; delete pendingTools[m.callId]; done(m.output); }
            if (m.type === 'lines') lines = m.items || [];
            if (m.type === 'tripResults' && m.seq === tripSeq && tripShow) tripShow((m.items || []).map(function (t) { return { id: t.id, routeId: t.routeId, main: t.label, sub: t.sub, tag: t.tag, live: t.live }; }));
            if (m.type === 'geocoded' && m.lat != null) fly(m.lat, m.lng, 15);
            if (m.type === 'view') showStack(m.mapTypeId === 'satellite' ? (m.labels ? 'hybrid' : 'satellite') : (m.labels ? 'roadmap' : 'roadmap-nolabels'));
        });
    }
})();
