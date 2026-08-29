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

            window[callbackName] = function () { resolve(window.google.maps); };

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

    var geocoder = null;

    /**
     * Turns a point back into an address for the marker-drag path.
     * </p>
     * Still Google's own Geocoder, unlike the routing above: geocoding is not a legacy service,
     * this runs only when a dispatcher drags a pin, and going through the host would mean posting
     * a patient's coordinates through one more hop for no saving worth the change.
     */
    function reverseGeocode(position) {
        return new Promise(function (resolve) {
            if (!geocoder) geocoder = new google.maps.Geocoder();

            geocoder.geocode({ location: position }, function (results, status) {
                if (status === 'OK' && results && results.length) {
                    resolve(results[0]);
                } else {
                    resolve(null);
                }
            });
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
            '.rm-secondary{color:#70757a;font-size:12px}'
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
                var chosen = suggestion.placePrediction.toPlace();

                await chosen.fetchFields({
                    fields: ['location', 'addressComponents', 'formattedAddress']
                });

                // The token dies with the selection; the next word typed starts a new session.
                sessionToken = null;

                var parsed = parseComponents(chosen.addressComponents, chosen.location);

                input.value = chosen.formattedAddress || parsed.address;

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

    function clearRoute() {
        if (routeLine) { routeLine.setMap(null); routeLine = null; }
        if (routeWindow) { routeWindow.close(); routeWindow = null; }
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

        routeLine = new google.maps.Polyline({
            path: path,
            map: map,
            strokeColor: '#4285F4',
            strokeOpacity: 0.9,
            strokeWeight: 6,
            icons: [{
                icon: {
                    path: google.maps.SymbolPath.FORWARD_CLOSED_ARROW,
                    scale: 4,
                    strokeColor: '#4285F4',
                    strokeWeight: 2
                },
                offset: '100%',
                repeat: '50px'
            }]
        });

        var bounds = new google.maps.LatLngBounds();
        path.forEach(function (point) { bounds.extend(point); });
        map.fitBounds(bounds);

        if (payload.label) {
            routeWindow = new google.maps.InfoWindow({
                content: '<b>' + payload.label + '</b>',
                position: path[Math.floor(path.length / 2)]
            });

            routeWindow.open(map);
        }
    }

    window.RaphaelMaps = {
        config: config,
        ready: ready,
        num: num,
        flag: flag,
        post: post,
        parseComponents: parseComponents,
        splitFormatted: splitFormatted,
        reverseGeocode: reverseGeocode,
        attachAutocomplete: attachAutocomplete,
        requestRoute: requestRoute,
        clearRoute: clearRoute,
        showRoute: showRoute
    };

    // The host calls this one by name through ExecuteScriptAsync.
    window.showRoute = showRoute;
})();
