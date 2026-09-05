/*
 * Shared map plumbing for the dispatcher's WebView2 screens.
 *
 * Why this file exists
 * --------------------
 * Every map page used to carry its own copy of the same three things: a Google Maps script tag
 * with the API key pasted into it, an address autocomplete, and a route drawn on the map. Two of
 * those stopped being available. As of 1 March 2025 Google does not serve
 * `google.maps.places.Autocomplete` or the JavaScript `DirectionsService` to projects created
 * after that date, and Raphael's Cloud project is new. The pages would have loaded and then
 * failed at the first keystroke.
 *
 * So:
 *   - autocomplete now uses the Places Data API (`AutocompleteSuggestion`) behind the same plain
 *     `<input>` the WPF side reads and writes, so nothing on the C# side had to change;
 *   - the route is no longer computed in the browser at all. The page asks its host for one, the
 *     host asks Raphael.Api, and what comes back is a shape to draw. That request goes through
 *     the routing cache like every other, which is the second reason to do it this way: the map's
 *     old DirectionsService call was billed every single time and no cache ever saw it.
 *
 * The API key reaches this file as `window.RAPHAEL_MAPS.apiKey`, injected by the host before the
 * document runs. It is never in the URL and never in the file on disk.
 */
(function () {
    'use strict';

    var config = window.RAPHAEL_MAPS || {};
    var query = new URLSearchParams(window.location.search);

    var mapsReady = null;

    /** Reads a query-string number, e.g. the coordinates the host navigated with. */
    function num(name, fallback) {
        var raw = query.get(name);

        if (raw === null || raw === '') return fallback;

        var value = parseFloat(raw);

        return isNaN(value) ? fallback : value;
    }

    function flag(name) {
        return query.get(name) === '1';
    }

    /** Posts to the WPF host. Silent when the page is opened outside WebView2. */
    function post(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
        }
    }

    /**
     * Tells the host we just spent something at Google.
     *
     * These calls carry the browser key and go straight from this page to Google, so the server
     * never sees them and the administrator's usage panel would be blind to a third of the bill.
     * Counted here, at the moment of spending, and forwarded by the host.
     *
     * ⚠️ Approximate by nature. Google's autocomplete session billing does not map exactly onto
     * "one request per keystroke burst", and the panel labels these SKUs as reported rather than
     * measured for that reason.
     */
    function meter(sku, count) {
        post({ type: 'usage', sku: sku, count: count || 1 });
    }

    /**
     * Loads the Maps JavaScript API once, with the libraries every page here needs:
     * `places` for the autocomplete data calls and `geometry` to decode the route shape.
     */
    function ready() {
        if (mapsReady) return mapsReady;

        mapsReady = new Promise(function (resolve, reject) {
            if (!config.apiKey) {
                reject(new Error('No Google Maps key was supplied to this page.'));
                return;
            }

            var callbackName = '__raphaelMapsReady';

            window[callbackName] = function () {
                // A page that loads the API and draws a map is one Dynamic Maps load, billed the
                // moment this callback fires. The autocomplete-only page draws no map, so it
                // declares itself with data-no-map and is not counted.
                if (!document.body || document.body.dataset.noMap !== '1') {
                    meter('DynamicMaps');
                }

                resolve(window.google.maps);
            };

            var script = document.createElement('script');

            script.src = 'https://maps.googleapis.com/maps/api/js'
                + '?key=' + encodeURIComponent(config.apiKey)
                + '&v=weekly'
                + '&libraries=places,geometry'
                + '&callback=' + callbackName;

            script.async = true;
            script.onerror = function () { reject(new Error('Google Maps could not be loaded.')); };

            document.head.appendChild(script);
        });

        return mapsReady;
    }

    // ------------------------------------------------------------------ addresses

    /**
     * Flattens a Place into the four fields every screen in this application stores.
     * </p>
     * The new Places API names these `longText` and `shortText`; the old one said `long_name` and
     * `short_name`. Both shapes are read because a Place can arrive either way depending on which
     * call produced it.
     */
    function parseComponents(components, location) {
        var result = { address: '', city: '', state: '', zip: '', lat: null, lng: null };

        if (location) {
            result.lat = typeof location.lat === 'function' ? location.lat() : location.lat;
            result.lng = typeof location.lng === 'function' ? location.lng() : location.lng;
        }

        (components || []).forEach(function (component) {
            var types = component.types || [];
            var long = component.longText || component.long_name || '';
            var short = component.shortText || component.short_name || '';

            if (types.indexOf('street_number') !== -1) result.address = long + ' ';
            if (types.indexOf('route') !== -1) result.address += long;
            if (types.indexOf('locality') !== -1) result.city = long;
            if (types.indexOf('administrative_area_level_1') !== -1) result.state = short;
            if (types.indexOf('postal_code') !== -1) result.zip = long;
        });

        result.address = result.address.trim();

        return result;
    }

    /** Splits a formatted address the way the old code did, for the marker-drag path. */
    function splitFormatted(formatted, lat, lng) {
        var parts = (formatted || '').split(',');
        var stateZip = (parts[2] || '').trim().split(' ');

        return {
            address: parts[0] || '',
            city: (parts[1] || '').trim(),
            state: stateZip[0] || '',
            zip: stateZip[1] || '',
            lat: lat,
            lng: lng
        };
    }

    var pending = {};
    var nextRequestId = 1;

    /**
     * Asks the host something and waits for its answer.
     *
     * The host replies by calling `RaphaelMaps.resolve(id, payload)`, which is how anything that
     * has to go through Raphael.Api gets back here. The page cannot call the API itself: that
     * would mean a session token living in a document that also runs Google's script.
     */
    function ask(message) {
        return new Promise(function (resolve) {
            var id = nextRequestId++;

            pending[id] = resolve;

            message.requestId = id;

            post(message);

            // A host that never answers must not leave a dragged pin waiting forever.
            setTimeout(function () {
                if (pending[id]) { delete pending[id]; resolve(null); }
            }, 8000);
        });
    }

    /** Called from C# with the answer to an `ask`. */
    function resolve(id, payload) {
        var waiting = pending[id];

        if (!waiting) return;

        delete pending[id];

        waiting(payload);
    }

    /**
     * Turns a point back into an address when a dispatcher drags a pin.
     *
     * ⚠️ This goes through the host, not through google.maps.Geocoder. It used to call Google
     * straight from here — a billed request on every drag that the cache never saw, while the
     * server had both the Geocoding key and the table to remember it in. The answer now comes
     * from our own database whenever anyone has dropped a pin within about eleven metres before.
     */
    function reverseGeocode(position) {
        var lat = typeof position.lat === 'function' ? position.lat() : position.lat;
        var lng = typeof position.lng === 'function' ? position.lng() : position.lng;

        return ask({ type: 'reverseGeocode', latitude: lat, longitude: lng })
            .then(function (answer) {
                if (!answer || answer.status !== 'Ok') return null;

                // Shaped like a google.maps.GeocoderResult so the callers did not have to change.
                return {
                    formatted_address: answer.formattedAddress || '',
                    parts: {
                        address: answer.street || '',
                        city: answer.city || '',
                        state: answer.state || '',
                        zip: answer.zip || '',
                        lat: answer.latitude,
                        lng: answer.longitude
                    }
                };
            });
    }

    // ------------------------------------------------------------------ autocomplete

    var stylesInjected = false;

    function injectStyles() {
        if (stylesInjected) return;

        stylesInjected = true;

        var style = document.createElement('style');

        style.textContent = [
            '.rm-suggestions{position:absolute;z-index:2147483000;background:#fff;border:1px solid #c8c8c8;',
            'border-top:none;box-shadow:0 2px 6px rgba(0,0,0,.3);max-height:260px;overflow-y:auto;',
            'font:13px/1.4 system-ui,Segoe UI,sans-serif;display:none}',
            '.rm-suggestions.rm-open{display:block}',
            '.rm-item{padding:7px 10px;cursor:pointer;border-bottom:1px solid #f0f0f0}',
            '.rm-item:last-child{border-bottom:none}',
            '.rm-item.rm-active,.rm-item:hover{background:#e8f0fe}',
            '.rm-main{color:#202124}',
            '.rm-secondary{color:#70757a;font-size:12px}',

            // ===== The route summary =====
            // A card of our own instead of Google's InfoWindow. The InfoWindow arrives with white
            // chrome, a tail and a close button we never wanted, it sat in the middle of the road
            // it was describing, and there is no way to restyle any of that. This is anchored to
            // a corner, out of the route's way, and looks like the rest of the application.
            '.rm-route{position:absolute;left:12px;bottom:22px;z-index:5;display:none;',
            'background:rgba(255,255,255,.97);border-radius:12px;padding:10px 14px;',
            'box-shadow:0 6px 18px rgba(0,0,0,.22);border:1px solid rgba(0,0,0,.06);',
            'font:13px/1.2 system-ui,Segoe UI,sans-serif;color:#202124;',
            'backdrop-filter:blur(2px)}',
            '.rm-route.rm-on{display:flex;align-items:center;gap:14px}',
            '.rm-stat{display:flex;align-items:center;gap:7px}',
            '.rm-stat svg{width:17px;height:17px;fill:#673AB7;flex:none}',
            '.rm-value{font-weight:600;font-size:15px;letter-spacing:-.2px}',
            '.rm-unit{color:#70757a;font-size:11px;margin-left:1px}',
            // The hairline between the two figures, so they read as two facts and not one string.
            '.rm-sep{width:1px;height:26px;background:rgba(0,0,0,.10)}'
        ].join('');

        document.head.appendChild(style);
    }

    /**
     * Address autocomplete over a plain `<input>`.
     *
     * The visible input is deliberately left alone: the WPF host writes into `#pickup` and
     * `#dropoff` by id and reads them back, and the replacement web component Google recommends
     * (`PlaceAutocompleteElement`) renders its own field, which would have broken every one of
     * those call sites. The Data API gives the same suggestions with the markup left in our hands.
     *
     * `onSelect` receives `{ address, city, state, zip, lat, lng }`.
     */
    function attachAutocomplete(inputId, onSelect) {
        var input = document.getElementById(inputId);

        if (!input) return null;

        injectStyles();

        var list = document.createElement('div');
        list.className = 'rm-suggestions';
        document.body.appendChild(list);

        var suggestions = [];
        var activeIndex = -1;
        var sessionToken = null;
        var debounce = null;

        function place() {
            var box = input.getBoundingClientRect();

            list.style.left = (box.left + window.scrollX) + 'px';
            list.style.top = (box.bottom + window.scrollY) + 'px';
            list.style.width = box.width + 'px';
        }

        function close() {
            list.classList.remove('rm-open');
            activeIndex = -1;
        }

        function render() {
            list.innerHTML = '';

            if (!suggestions.length) {
                close();
                return;
            }

            suggestions.forEach(function (suggestion, index) {
                var prediction = suggestion.placePrediction;
                var item = document.createElement('div');

                item.className = 'rm-item' + (index === activeIndex ? ' rm-active' : '');

                var main = document.createElement('div');
                main.className = 'rm-main';
                main.textContent = prediction.mainText
                    ? prediction.mainText.toString()
                    : prediction.text.toString();

                item.appendChild(main);

                if (prediction.secondaryText) {
                    var secondary = document.createElement('div');
                    secondary.className = 'rm-secondary';
                    secondary.textContent = prediction.secondaryText.toString();
                    item.appendChild(secondary);
                }

                // mousedown, not click: the input's blur would close the list first.
                item.addEventListener('mousedown', function (event) {
                    event.preventDefault();
                    choose(index);
                });

                list.appendChild(item);
            });

            place();
            list.classList.add('rm-open');
        }

        async function search(text) {
            if (!text || text.length < 3) {
                suggestions = [];
                render();
                return;
            }

            try {
                var places = google.maps.places;

                // One token covers a whole typing session through to the selection, which is what
                // makes Google bill it as one autocomplete rather than one per keystroke.
                if (!sessionToken) sessionToken = new places.AutocompleteSessionToken();

                var request = { input: text, sessionToken: sessionToken };

                if (config.regionCode) request.includedRegionCodes = [config.regionCode];

                meter('PlacesAutocomplete');

                var response = await places.AutocompleteSuggestion.fetchAutocompleteSuggestions(request);

                suggestions = (response.suggestions || []).filter(function (s) {
                    return s.placePrediction;
                });

                activeIndex = -1;

                render();
            } catch (error) {
                console.error('Autocomplete failed', error);
                suggestions = [];
                close();
            }
        }

        async function choose(index) {
            var suggestion = suggestions[index];

            if (!suggestion) return;

            close();

            try {
                var prediction = suggestion.placePrediction;
                var placeId = prediction.placeId;

                // The token dies with the selection; the next word typed starts a new session.
                sessionToken = null;

                // Ask our own database first. A place chosen once has been chosen for everyone:
                // in this business the same dialysis clinic is picked hundreds of times a month,
                // and it should be bought exactly once.
                var known = placeId
                    ? await ask({ type: 'placeLookup', placeId: placeId })
                    : null;

                if (known && known.status === 'Ok') {
                    var cached = {
                        address: known.street || '',
                        city: known.city || '',
                        state: known.state || '',
                        zip: known.zip || '',
                        lat: known.latitude,
                        lng: known.longitude
                    };

                    input.value = known.formattedAddress || cached.address;

                    if (onSelect) onSelect(cached, null);

                    return;
                }

                // Nobody has bought this one yet. Only this key has Places enabled, so we fetch
                // it — and then hand it to the host so the next dispatcher gets it free.
                var chosen = prediction.toPlace();

                meter('PlaceDetails');

                await chosen.fetchFields({
                    fields: ['location', 'addressComponents', 'formattedAddress']
                });

                var parsed = parseComponents(chosen.addressComponents, chosen.location);

                input.value = chosen.formattedAddress || parsed.address;

                if (placeId) {
                    post({
                        type: 'placeStore',
                        placeId: placeId,
                        latitude: parsed.lat,
                        longitude: parsed.lng,
                        formattedAddress: chosen.formattedAddress || '',
                        street: parsed.address,
                        city: parsed.city,
                        state: parsed.state,
                        zip: parsed.zip
                    });
                }

                if (onSelect) onSelect(parsed, chosen);
            } catch (error) {
                console.error('Could not read the chosen place', error);
            }
        }

        input.setAttribute('autocomplete', 'off');

        input.addEventListener('input', function () {
            clearTimeout(debounce);

            var text = input.value;

            // A quarter of a second of quiet. Without it every keystroke is a billed request.
            debounce = setTimeout(function () { search(text); }, 250);
        });

        input.addEventListener('keydown', function (event) {
            if (!list.classList.contains('rm-open')) return;

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                activeIndex = Math.min(activeIndex + 1, suggestions.length - 1);
                render();
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                activeIndex = Math.max(activeIndex - 1, 0);
                render();
            } else if (event.key === 'Enter') {
                if (activeIndex >= 0) {
                    event.preventDefault();
                    choose(activeIndex);
                }
            } else if (event.key === 'Escape') {
                close();
            }
        });

        input.addEventListener('blur', function () { setTimeout(close, 150); });

        window.addEventListener('resize', place);
        window.addEventListener('scroll', place, true);

        return { close: close };
    }

    // ------------------------------------------------------------------ route drawing

    var routeLine = null;
    var routeWindow = null;

    /**
     * Asks the host for a route. The answer arrives back through `RaphaelMaps.showRoute`.
     * </p>
     * The page cannot call Raphael.Api itself, and should not: that would mean handing a session
     * token to a document that also runs Google's script.
     */
    var routeCasing;

    function requestRoute(origin, destination) {
        if (!origin || !destination) return;

        post({
            type: 'routeRequest',
            originLat: typeof origin.lat === 'function' ? origin.lat() : origin.lat,
            originLng: typeof origin.lng === 'function' ? origin.lng() : origin.lng,
            destLat: typeof destination.lat === 'function' ? destination.lat() : destination.lat,
            destLng: typeof destination.lng === 'function' ? destination.lng() : destination.lng
        });
    }

    /**
     * The pins.
     *
     * Letters in a balloon are what a demo looks like. These are the two shapes every mapping
     * product has settled on, and they are read without a legend: the journey STARTS at a dot and
     * ARRIVES at a pin. Colour carries the same meaning a second time — the brand purple for where
     * the patient is waiting, green for where they are going — so neither shape nor colour is
     * carrying it alone, which matters for whoever cannot tell the two apart.
     *
     * Symbols, not images: vector at every zoom, no file to ship, and nothing else to fetch.
     */
    function pickupSymbol() {
        return {
            path: google.maps.SymbolPath.CIRCLE,
            scale: 8,
            fillColor: '#673AB7',
            fillOpacity: 1,
            strokeColor: '#FFFFFF',
            strokeWeight: 3.5
        };
    }

    function dropoffSymbol() {
        return {
            // Material's place mark, on its own 24x24 grid.
            path: 'M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7z'
                + 'm0 9.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z',
            fillColor: '#2E7D32',
            fillOpacity: 1,
            strokeColor: '#FFFFFF',
            strokeWeight: 1.6,
            scale: 1.5,
            // The tip of the pin is the place, not its middle.
            anchor: new google.maps.Point(12, 22)
        };
    }

    function routeCard() {
        var card = document.getElementById('rm-route');

        if (card) return card;

        injectStyles();

        card = document.createElement('div');
        card.id = 'rm-route';
        card.className = 'rm-route';

        (document.getElementById('map') || document.body).appendChild(card);

        return card;
    }

    var CLOCK_ICON = '<svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm'
        + '1 10.6V6h-2v7.4l5.2 3.1 1-1.7-4.2-2.2z"/></svg>';

    var ROAD_ICON = '<svg viewBox="0 0 24 24"><path d="M18.4 3H16l1.2 18h3.3L18.4 3zM5.6 3 3.5 21h'
        + '3.3L8 3H5.6zM13 3h-2v3.5h2V3zm0 6h-2v3.5h2V9zm0 6h-2v3.5h2V15z"/></svg>';

    function showRouteSummary(eta, distance) {
        var card = routeCard();

        if (!eta && !distance) { card.className = 'rm-route'; return; }

        card.innerHTML =
            '<div class="rm-stat">' + CLOCK_ICON + '<span class="rm-value">' + (eta || '—') + '</span></div>'
            + '<div class="rm-sep"></div>'
            + '<div class="rm-stat">' + ROAD_ICON + '<span class="rm-value">' + (distance || '—')
            + '<span class="rm-unit">mi</span></span></div>';

        card.className = 'rm-route rm-on';
    }

    function clearRoute() {
        if (routeLine) { routeLine.setMap(null); routeLine = null; }
        if (routeCasing) { routeCasing.setMap(null); routeCasing = null; }
        if (routeWindow) { routeWindow.close(); routeWindow = null; }

        showRouteSummary(null, null);
    }

    /**
     * Draws what the host sent back. Called from C# by name — renaming it breaks the map
     * silently, so it is also exported on `window` below.
     */
    function showRoute(payload) {
        var map = window.__raphaelMap;

        if (!map || !payload || !payload.encodedPolyline) return;

        clearRoute();

        var path = google.maps.geometry.encoding.decodePath(payload.encodedPolyline);

        // ===== Two lines, not one =====
        // A single stroke disappears over a motorway of about its own width and colour. The wide
        // dark casing underneath gives the route an edge, so it reads as one continuous path over
        // any part of the map — which is what every serious mapping product draws and why theirs
        // are legible and a bare polyline is not.
        routeCasing = new google.maps.Polyline({
            path: path,
            map: map,
            strokeColor: '#311B92',
            strokeOpacity: 0.55,
            strokeWeight: 10,
            zIndex: 1
        });

        routeLine = new google.maps.Polyline({
            path: path,
            map: map,
            strokeColor: '#7E57C2',
            strokeOpacity: 1,
            strokeWeight: 5,
            zIndex: 2,
            // Arrows say which way, so they only have to be readable, not loud. Every 140px rather
            // than every 50: the old spacing turned the route into a dotted caterpillar and the
            // direction was harder to see, not easier.
            icons: [{
                icon: {
                    path: google.maps.SymbolPath.FORWARD_CLOSED_ARROW,
                    scale: 2.6,
                    fillColor: '#FFFFFF',
                    fillOpacity: 1,
                    strokeColor: '#311B92',
                    strokeWeight: 1
                },
                offset: '6%',
                repeat: '140px'
            }]
        });

        var bounds = new google.maps.LatLngBounds();
        path.forEach(function (point) { bounds.extend(point); });

        // Room for the summary card in the bottom-left corner, so it never sits on the route.
        map.fitBounds(bounds, { top: 40, right: 40, bottom: 80, left: 40 });

        showRouteSummary(payload.eta, payload.distance);
    }

    window.RaphaelMaps = {
        config: config,
        ready: ready,
        num: num,
        flag: flag,
        post: post,
        ask: ask,
        resolve: resolve,
        meter: meter,
        parseComponents: parseComponents,
        splitFormatted: splitFormatted,
        reverseGeocode: reverseGeocode,
        attachAutocomplete: attachAutocomplete,
        requestRoute: requestRoute,
        clearRoute: clearRoute,
        showRoute: showRoute,
        pickupSymbol: pickupSymbol,
        dropoffSymbol: dropoffSymbol
    };

    // The host calls these by name through ExecuteScriptAsync. Renaming one breaks the map
    // silently, which is why they are pinned to window rather than left on the namespace.
    window.showRoute = showRoute;
    window.raphaelResolve = resolve;
})();
