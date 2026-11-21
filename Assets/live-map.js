/*
 * RE-080 · Live map drawing surface.
 *
 * The Desktop decides everything (which lines exist, their group, colour, whether they are shown,
 * where they are, which layers are on, which vehicle is selected) and posts it here; this page only
 * draws. Messages from the host:
 *
 *   vehicles  { items: [Vehicle] }           upsert every vehicle; ids not in the list are removed
 *   move      { id, lat, lng, heading, moving, speeding, stale, title }   a live fix: glide there
 *   legs      { id, legs: [Leg], stops: [Stop] }   one line's day
 *   layers    { routes, traffic, delays, selected, wholeDay }
 *   traffic   { items: [{ id, path, intervals: [{ s, e, speed }], delay }] }
 *   fit       { ids }      fitRoute { id }      zoom { delta }      view { mapTypeId, labels }
 *
 *   Vehicle = { id, name, lat, lng, heading, color, routeColor, moving, speeding, stale, shown, title }
 *
 * To the host: { type: 'vehicleClick', id, double }, plus the shared 'usage' and 'maptype' messages.
 *
 * What is drawn, and why (the legend in the Desktop says the same):
 *   - Vehicles: a disc in the group colour, white van, a pointer turned to the heading when moving,
 *     a pause badge when stopped, a red ring when speeding, faded when the last fix is old.
 *   - Routes in progress (layer): each vehicle's road from where it is to its next stop, in the line's
 *     own colour over a white casing, ending at a chequered finish flag.
 *   - The selected vehicle, whatever the layers. Trip in progress: Booking's pickup and drop-off pins,
 *     arrows only from the pickup to the drop-off, the approach to the pickup dotted on top. Whole day:
 *     stops as on the Dispatch map (green pickup, red drop-off, purple once arrived; one label per
 *     place), every leg with its own arrows in order, gray where driven, the line's colour ahead,
 *     dashed where only planned; on selection the driven road is drawn leg by leg and each stop pops.
 *   - Delays (layer): only the slow (amber) and jammed (red) stretches of each vehicle's road ahead,
 *     and "+N'" on the vehicle when traffic adds a minute or more.
 */
(function () {
    'use strict';

    var RM = window.RaphaelMaps;

    // Script errors reach the Desktop's log instead of dying silently in the page.
    window.addEventListener('error', function (e) {
        RM.post({ type: 'error', message: (e.message || 'script error') + ' @' + (e.filename || '') + ':' + (e.lineno || '') });
    });

    var GLIDE_MS = 1500;
    var DISPATCH = { pickup: '#008000', dropoff: '#FF0000', arrived: '#800080' };   // WPF Green, Red, Purple: the Dispatch map's colours
    var DRIVEN = '#525C6B';                 // the road already driven: neutral gray, whatever the line
    var SPEED = { SLOW: '#E8A33D', TRAFFIC_JAM: '#D93B3B' };

    var map = null, overlay = null;
    var vehicles = {};          // id -> { data, el, pos, heading, from, to, start }
    var routes = {};            // id -> { legs, stops, drawn: [] }
    var traffic = {};           // id -> { path, intervals, delay, drawn: [] }
    var layers = { routes: false, traffic: false, delays: false, selected: null, wholeDay: false };
    var trafficLayer = null;
    var animating = false;
    var noLabels = [{ elementType: 'labels', stylers: [{ visibility: 'off' }] },
                    { featureType: 'poi', stylers: [{ visibility: 'off' }] },
                    { featureType: 'transit', stylers: [{ visibility: 'off' }] }];

    // ================================================================ vehicles

    // A van, side view, white on the disc.
    var VAN = '<path fill="#fff" d="M4 9.5C4 8.7 4.7 8 5.5 8h9.2c.5 0 .9.2 1.2.6l2.4 3.1 1.9.6c.5.2.8.6.8 1.2V17c0 .6-.4 1-1 1h-1.1a2.3 2.3 0 0 1-4.5 0H9.6a2.3 2.3 0 0 1-4.5 0H5c-.6 0-1-.4-1-1z"/>' +
              '<path fill-opacity=".9" d="M6 10h3.6v2.6H6zM10.6 10h3.7l1.8 2.6h-5.5z" fill="currentColor"/>';

    function vehicleHtml(d, heading) {
        var pointer = d.moving
            ? '<svg class="lm-ptr" viewBox="0 0 40 40" style="transform:rotate(' + (heading || 0) + 'deg)"><path d="M20 0.5 L25.5 7.5 H14.5 Z" fill="' + d.color + '" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/></svg>'
            : '';
        var pause = d.moving ? '' : '<span class="lm-pause"><svg viewBox="0 0 12 12"><rect x="3" y="2.5" width="2" height="7" fill="#fff"/><rect x="7" y="2.5" width="2" height="7" fill="#fff"/></svg></span>';
        return pointer +
            '<span class="lm-disc" style="background:' + d.color + ';color:' + d.color + '"><svg viewBox="0 0 24 24">' + VAN + '</svg></span>' +
            pause +
            '<span class="lm-name">' + escapeHtml(d.name || '') + '</span>';
    }

    function escapeHtml(t) {
        return String(t).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
    }

    function render(entry) {
        var d = entry.data;
        entry.el.innerHTML = vehicleHtml(d, entry.heading);
        entry.el.title = d.title || '';
        var sel = layers.selected;
        entry.el.classList.toggle('stale', !!d.stale);
        entry.el.classList.toggle('hidden', !d.shown);
        entry.el.classList.toggle('speeding', !!d.speeding);
        entry.el.classList.toggle('selected', sel === d.id);
        entry.el.classList.toggle('named', layers.routes || sel === d.id);
        entry.el.style.setProperty('--route', d.routeColor || '#fff');
        if (layers.delays && traffic[d.id]) setBadge(entry, traffic[d.id].delay);
    }

    function setHeading(entry, heading) {
        entry.heading = heading;
        var p = entry.el.querySelector('.lm-ptr');
        if (p) p.style.transform = 'rotate(' + heading + 'deg)';
    }

    function setBadge(entry, minutes) {
        var badge = entry.el.querySelector('.lm-delay');
        if (!minutes || minutes < 1) { if (badge) badge.remove(); return; }
        if (!badge) { badge = document.createElement('span'); badge.className = 'lm-delay'; entry.el.appendChild(badge); }
        badge.textContent = '+' + minutes + "'";
    }

    function VehicleLayer() { this.div = null; }

    function defineLayer() {
        VehicleLayer.prototype = new google.maps.OverlayView();
        VehicleLayer.prototype.onAdd = function () {
            this.div = document.createElement('div');
            this.div.style.position = 'absolute';
            this.getPanes().overlayMouseTarget.appendChild(this.div);
            Object.keys(vehicles).forEach(function (id) { overlay.div.appendChild(vehicles[id].el); });
        };
        VehicleLayer.prototype.draw = function () {
            var projection = this.getProjection();
            if (!projection) return;
            Object.keys(vehicles).forEach(function (id) { place(vehicles[id], projection); });
        };
        VehicleLayer.prototype.onRemove = function () {
            if (this.div) this.div.parentNode.removeChild(this.div);
            this.div = null;
        };
    }

    function place(entry, projection) {
        projection = projection || (overlay && overlay.getProjection());
        if (!projection) return;
        var p = projection.fromLatLngToDivPixel(new google.maps.LatLng(entry.pos.lat, entry.pos.lng));
        entry.el.style.left = p.x + 'px';
        entry.el.style.top = p.y + 'px';
    }

    function upsertVehicles(items) {
        var seen = {};
        items.forEach(function (d) {
            seen[d.id] = true;
            var entry = vehicles[d.id];
            if (!entry) {
                var el = document.createElement('div');
                el.className = 'lm-v';
                el.addEventListener('click', function (e) { e.stopPropagation(); RM.post({ type: 'vehicleClick', id: d.id, double: false }); });
                el.addEventListener('dblclick', function (e) { e.stopPropagation(); RM.post({ type: 'vehicleClick', id: d.id, double: true }); });
                // Google builds its own map click from the pointer events, so stopping 'click' is not
                // enough: without this, selecting a vehicle also clicked the map and undid the selection.
                google.maps.OverlayView.preventMapHitsAndGesturesFrom(el);
                entry = vehicles[d.id] = { data: d, el: el, pos: { lat: d.lat, lng: d.lng }, heading: d.heading || 0 };
                if (overlay && overlay.div) overlay.div.appendChild(el);
            }
            entry.data = d;
            render(entry);
            place(entry);
        });
        Object.keys(vehicles).forEach(function (id) {
            if (seen[id]) return;
            var gone = vehicles[id];
            if (gone.el.parentNode) gone.el.parentNode.removeChild(gone.el);
            delete vehicles[id];
        });
        redrawAll();
    }

    function shortestTurn(from, to) { return from + ((((to - from) % 360) + 540) % 360 - 180); }

    function move(m) {
        var e = vehicles[m.id];
        if (!e) return;
        e.data.moving = m.moving; e.data.speeding = m.speeding; e.data.stale = m.stale;
        if (m.title) e.data.title = m.title;
        render(e);
        e.from = { lat: e.pos.lat, lng: e.pos.lng, heading: e.heading };
        e.to = { lat: m.lat, lng: m.lng, heading: shortestTurn(e.heading, m.heading) };
        e.start = performance.now();
        if (!animating) { animating = true; requestAnimationFrame(step); }
    }

    function step(now) {
        var any = false;
        Object.keys(vehicles).forEach(function (id) {
            var e = vehicles[id];
            if (!e.to) return;
            var t = Math.min(1, (now - e.start) / GLIDE_MS), k = 1 - Math.pow(1 - t, 3);
            e.pos = { lat: e.from.lat + (e.to.lat - e.from.lat) * k, lng: e.from.lng + (e.to.lng - e.from.lng) * k };
            setHeading(e, e.from.heading + (e.to.heading - e.from.heading) * k);
            place(e);
            if (t >= 1) { e.to = null; if (!(layers.selected === +id && anim)) drawRoute(+id); } else any = true;
        });
        if (any) requestAnimationFrame(step); else animating = false;
    }

    // ================================================================ geometry

    function decode(leg) {
        if (leg.path) {
            try { return google.maps.geometry.encoding.decodePath(leg.path); } catch (e) { /* straight line */ }
        }
        return [new google.maps.LatLng(leg.from[0], leg.from[1]), new google.maps.LatLng(leg.to[0], leg.to[1])];
    }

    /** Splits a path at the point nearest to (lat, lng): the part behind it and the part ahead. */
    function splitAt(path, lat, lng) {
        var best = { d: Infinity, i: 0, p: path[0] }, k = Math.cos(lat * Math.PI / 180);
        for (var i = 0; i < path.length - 1; i++) {
            var ax = path[i].lng() * k, ay = path[i].lat(), bx = path[i + 1].lng() * k, by = path[i + 1].lat();
            var px = lng * k, py = lat, dx = bx - ax, dy = by - ay;
            var t = dx === 0 && dy === 0 ? 0 : Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
            var qx = ax + t * dx, qy = ay + t * dy, d = (qx - px) * (qx - px) + (qy - py) * (qy - py);
            if (d < best.d) best = { d: d, i: i, p: new google.maps.LatLng(qy, qx / k) };
        }
        return { done: path.slice(0, best.i + 1).concat([best.p]), ahead: [best.p].concat(path.slice(best.i + 1)) };
    }

    function line(drawn, path, color, weight, opacity, z, dashed) {
        if (!path || path.length < 2) return;
        // White casing first, so the line reads on any map, traffic layer included.
        drawn.push(new google.maps.Polyline({ path: path, map: map, strokeColor: '#ffffff', strokeOpacity: 0.95 * opacity, strokeWeight: weight + 4, zIndex: z, clickable: false }));
        var o = { path: path, map: map, strokeColor: color, strokeWeight: weight, zIndex: z + 1, clickable: false };
        if (dashed) {
            o.strokeOpacity = 0;
            o.icons = [{ icon: { path: 'M 0,-1 0,1', strokeOpacity: opacity, strokeColor: color, strokeWeight: weight, scale: 1 }, offset: '0', repeat: (weight * 2 + 8) + 'px' }];
        } else {
            o.strokeOpacity = opacity;
        }
        drawn.push(new google.maps.Polyline(o));
    }

    function stopIcon(st) {
        var fill = st.arrived ? DISPATCH.arrived : st.kind === 'Dropoff' ? DISPATCH.dropoff : DISPATCH.pickup;
        var label = String(st.n);
        var w = label.length > 1 ? 30 : 26;
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' + w + '" height="26" viewBox="0 0 ' + w + ' 26">' +
            '<rect x="1.5" y="1.5" width="' + (w - 3) + '" height="23" rx="11.5" fill="' + fill + '" stroke="#fff" stroke-width="2.5"/>' +
            '<text x="' + (w / 2) + '" y="17.6" font-family="Segoe UI,Arial" font-size="13" font-weight="700" fill="#fff" text-anchor="middle">' + label + '</text></svg>';
        return { url: 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg), anchor: new google.maps.Point(w / 2, 13), scaledSize: new google.maps.Size(w, 26) };
    }

    function garageIcon() {
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="26" height="26" viewBox="0 0 26 26"><circle cx="13" cy="13" r="11.5" fill="#008000" stroke="#fff" stroke-width="2.5"/>' +
            '<path fill="#fff" d="M8 15.5l1.2-3.6c.2-.5.6-.9 1.2-.9h5.2c.6 0 1 .4 1.2.9l1.2 3.6v3h-1.3a1 1 0 0 1-2 0h-3.4a1 1 0 0 1-2 0H8z"/></svg>';
        return { url: 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg), anchor: new google.maps.Point(13, 13), scaledSize: new google.maps.Size(26, 26) };
    }

    function marker(drawn, pos, icon, z) {
        drawn.push(new google.maps.Marker({ position: pos, map: map, icon: icon, zIndex: z, clickable: false }));
    }

    // ================================================================ routes

    var LABELS = { en: { pickup: 'PICKUP', dropoff: 'DROPOFF' }, es: { pickup: 'RECOGIDA', dropoff: 'ENTREGA' } };
    var BOOKING = { pickup: '#c0392b', dropoff: '#0a5c7e' };   // Booking's StopMarker colours
    var anim = null;                                            // the whole-day path being drawn
    var animatedKey = null;                                     // selection + mode already animated: later redraws are static

    function clear(list) { (list || []).forEach(function (o) { o.setMap(null); }); }

    function stopByNumber(r, n) { return r.stops.filter(function (s) { return s.n === n; })[0]; }

    /** A Google arrow every so often along a line, pointing the way it is driven (as the Home map). */
    function arrows(color, repeat) {
        // Solid in the line's colour with a white edge: reads on the line, on the map and on traffic.
        return [{ icon: { path: google.maps.SymbolPath.FORWARD_CLOSED_ARROW, scale: 4.2, fillColor: color, fillOpacity: 1,
                          strokeColor: '#ffffff', strokeWeight: 1.8 }, offset: '50px', repeat: repeat || '150px' }];
    }

    /**
     * An HTML marker on the map (Booking's pins, the numbered stops, the finish flag). It lives in the
     * overlay pane, under the vehicles: a vehicle at its stop is never hidden by the stop's marker.
     */
    function HtmlMarker(position, html, z, cls) { this.position = position; this.html = html; this.z = z; this.cls = cls || 'lm-pinwrap'; this.div = null; }
    function defineHtmlMarker() {
        HtmlMarker.prototype = new google.maps.OverlayView();
        HtmlMarker.prototype.onAdd = function () {
            this.div = document.createElement('div');
            this.div.className = this.cls;
            this.div.style.zIndex = this.z;
            this.div.innerHTML = this.html;
            this.getPanes().overlayLayer.appendChild(this.div);
        };
        HtmlMarker.prototype.draw = function () {
            var projection = this.getProjection();
            if (!projection || !this.div) return;
            var p = projection.fromLatLngToDivPixel(new google.maps.LatLng(this.position.lat, this.position.lng));
            this.div.style.left = p.x + 'px';
            this.div.style.top = p.y + 'px';
        };
        HtmlMarker.prototype.onRemove = function () { if (this.div) this.div.remove(); this.div = null; };
        /** Runs fn(element) once the element exists (onAdd runs on the next frame). */
        HtmlMarker.prototype.apply = function (fn) {
            var self = this;
            var run = function () { if (self.div) fn(self.div); };
            if (this.div) run(); else this.pendingFlags = (this.pendingFlags || []).concat([run]);
        };
        HtmlMarker.prototype.flag = function (cls, on) { this.apply(function (el) { el.classList.toggle(cls, on); }); };
        var add = HtmlMarker.prototype.onAdd;
        HtmlMarker.prototype.onAdd = function () { add.call(this); (this.pendingFlags || []).forEach(function (f) { f(); }); this.pendingFlags = null; };
    }

    function htmlMarker(drawn, pos, html, z, cls) {
        var m = new HtmlMarker(pos, html, z, cls);
        m.setMap(map);
        drawn.push(m);
        return m;
    }

    /**
     * Where the vehicle is going: a chequered finish flag planted on the spot, on a base in the line's
     * colour. Says "arrival" at a glance, and cannot be mistaken for a stop number or a vehicle.
     */
    function finishFlag(drawn, pos, color, title) {
        var html = '<div class="lm-finish" title="' + escapeHtml(title || '') + '"><svg width="38" height="44" viewBox="0 0 38 44">' +
            '<ellipse cx="9" cy="40.5" rx="6.5" ry="2.6" fill="rgba(15,30,46,.28)"/>' +
            '<rect x="7.6" y="3" width="2.8" height="37" rx="1.2" fill="#1E2A36"/>' +
            '<path d="M10.4 4.2c5-2.6 9.4 2.6 14.6 0 2.6-1.3 5.2-1.6 8.6-.6v15.2c-3.4-1-6-.7-8.6.6-5.2 2.6-9.6-2.6-14.6 0z" fill="url(#lm-checker)" stroke="#1E2A36" stroke-width="1.2" stroke-linejoin="round"/>' +
            '<circle cx="9" cy="40" r="4.6" fill="' + color + '" stroke="#fff" stroke-width="2.2"/>' +
            '<circle cx="9" cy="3" r="2.4" fill="#F5B301" stroke="#1E2A36" stroke-width="1"/></svg></div>';
        return htmlMarker(drawn, pos, html, 70, 'lm-finishwrap');
    }

    function stopFill(st) { return st.arrived ? DISPATCH.arrived : st.kind === 'Dropoff' ? DISPATCH.dropoff : DISPATCH.pickup; }

    /**
     * The numbered stops at one place, as on the Dispatch map: green pickup, red drop-off, purple once
     * arrived. Stops at the same place (every drop-off at the same hospital) share one chip, "2 4 6",
     * each number in its own colour: drawn apart, the last one hid the others.
     */
    function stopChip(drawn, group, color, z) {
        var html = '<div class="lm-stopin' + (group.length > 1 ? ' multi' : '') + '" style="--route:' + color + ';background:' + stopFill(group[0]) + '">' +
            group.map(function (st) { return '<span data-n="' + st.n + '" style="background:' + stopFill(st) + '">' + st.n + '</span>'; }).join('') + '</div>';
        return htmlMarker(drawn, { lat: group[0].lat, lng: group[0].lng }, html, z || 40, 'lm-stop');
    }

    /** Stops grouped by place: within about 40 m they are the same door. */
    function stopGroups(stops) {
        var groups = [];
        stops.forEach(function (st) {
            var g = groups.filter(function (x) { return Math.abs(x[0].lat - st.lat) < 0.0004 && Math.abs(x[0].lng - st.lng) < 0.0004; })[0];
            if (g) g.push(st); else groups.push([st]);
        });
        return groups;
    }

    /** Booking's StopMarker: the pin, and under it a solid label in the pin's colour with a white ring. */
    function bookingStop(drawn, st) {
        var kind = st.kind === 'Dropoff' ? 'dropoff' : 'pickup';
        var fill = BOOKING[kind];
        var text = (LABELS[layers.lang] || LABELS.en)[kind] + (st.time ? ' ' + st.time : '');
        var html = '<div class="lm-pin"><svg width="30" height="38" viewBox="0 0 12 16"><path d="M6 0a6 6 0 0 0-6 6c0 4.4 6 10 6 10s6-5.6 6-10a6 6 0 0 0-6-6z" fill="' + fill + '" stroke="#fff" stroke-width=".9"/><circle cx="6" cy="6" r="2.2" fill="#fff"/></svg>' +
                   '<span class="lm-pinlabel" style="background:' + fill + '">' + escapeHtml(text) + '</span></div>';
        htmlMarker(drawn, { lat: st.lat, lng: st.lng }, html, 60);
    }

    function lineWith(drawn, path, color, weight, opacity, z, extra) {
        if (!path || path.length < 2) return null;
        drawn.push(new google.maps.Polyline({ path: path, map: map, strokeColor: '#ffffff', strokeOpacity: 0.95 * opacity, strokeWeight: weight + 4, zIndex: z, clickable: false }));
        var o = { path: path, map: map, strokeColor: color, strokeOpacity: opacity, strokeWeight: weight, zIndex: z + 1, clickable: false };
        Object.keys(extra || {}).forEach(function (k) { o[k] = extra[k]; });
        var pl = new google.maps.Polyline(o);
        drawn.push(pl);
        return pl;
    }

    function stopAnimation() { if (anim) { cancelAnimationFrame(anim.frame); anim = null; } }

    function drawRoute(id) {
        var r = routes[id];
        if (!r) return;
        // The whole day is being drawn for this selection: let it finish. The legs refreshed on
        // selection and the first fixes used to restart it as a static drawing, so nobody saw it.
        if (anim && anim.key === id + ':day' && layers.selected === id && layers.wholeDay) return;
        // The old drawing goes a moment after the new one is in: HTML markers appear on the next
        // frame, and clearing first made every fix blink.
        var old = r.drawn; r.drawn = [];
        setTimeout(function () { clear(old); }, 80);

        var v = vehicles[id];
        if (!v || !v.data.shown) return;
        var color = v.data.routeColor || '#2563EB';
        var selected = layers.selected === id;
        var current = r.legs.filter(function (l) { return l.current; })[0];

        if (!selected) {
            // Routes layer: from the vehicle to where it is going, ending at a finish flag.
            // Nothing when the driver has not started that leg.
            if (!layers.routes || !current || !current.visible) return;
            var parts = splitAt(decode(current), v.pos.lat, v.pos.lng);
            line(r.drawn, parts.ahead, color, 5, 1, 10);
            var target = stopByNumber(r, current.toStop);
            if (target) finishFlag(r.drawn, { lat: target.lat, lng: target.lng }, color, v.data.name);
            return;
        }

        if (layers.wholeDay) drawWholeDay(id, r, v, color, current);
        else drawTripInProgress(r, v, color, current);
    }

    function legIndexTo(r, n) {
        for (var i = 0; i < r.legs.length; i++) if (r.legs[i].toStop === n) return i;
        return -1;
    }

    /**
     * The selected vehicle's trip in progress, as Booking shows a trip: its pickup and drop-off pins
     * with their times, and the trip itself — the road from the pickup to the drop-off — with arrows
     * the way it is driven: gray behind the vehicle, the line's colour ahead.
     * Still heading to the pickup, the way there is a thin dashed line without arrows: it is the
     * approach, not the trip.
     */
    function drawTripInProgress(r, v, color, current) {
        if (!current) return;
        var target = stopByNumber(r, current.toStop);
        var trip = target ? target.trip : null;
        var tripStops = r.stops.filter(function (s) { return s.trip === trip; });
        var pickup = tripStops.filter(function (s) { return s.kind === 'Pickup'; })[0];
        var dropoff = tripStops.filter(function (s) { return s.kind === 'Dropoff'; })[0];
        var iP = pickup ? legIndexTo(r, pickup.n) : -1;
        var iD = dropoff ? legIndexTo(r, dropoff.n) : -1;

        // The approach: only while the vehicle is still on its way to the pickup. Dotted and on top —
        // it often runs on the same road as the trip, the other way, and must not hide under it.
        var approaching = iP >= 0 && r.legs[iP].current;
        if (approaching) {
            var toPickup = splitAt(decode(r.legs[iP]), v.pos.lat, v.pos.lng).ahead;
            if (toPickup.length > 1) {
                r.drawn.push(new google.maps.Polyline({ path: toPickup, map: map, strokeOpacity: 0, zIndex: 40, clickable: false,
                    icons: [{ icon: { path: google.maps.SymbolPath.CIRCLE, scale: 3.2, fillColor: color, fillOpacity: 1, strokeColor: '#ffffff', strokeWeight: 1.5 },
                              offset: '0', repeat: '13px' }] }));
            }
        }

        // The trip: every leg after the pickup up to the drop-off (others' stops in between included).
        for (var i = Math.max(iP + 1, 0); iD >= 0 && i <= iD; i++) {
            var leg = r.legs[i], path = decode(leg);
            if (leg.driven) {
                lineWith(r.drawn, path, DRIVEN, 7, 0.95, 30, { icons: arrows(DRIVEN, '150px') });
            } else if (leg.current) {
                var p = splitAt(path, v.pos.lat, v.pos.lng);
                lineWith(r.drawn, p.done, DRIVEN, 7, 0.95, 30, { icons: arrows(DRIVEN, '150px') });
                lineWith(r.drawn, p.ahead, color, 8, 1, 32, { icons: arrows(color, '150px') });
            } else {
                // Not started yet: the same line, quieter, so the approach on top reads first.
                lineWith(r.drawn, path, color, approaching ? 6 : 7, approaching ? 0.55 : 0.95, 31, { icons: arrows(color, '150px') });
            }
        }

        if (pickup) bookingStop(r.drawn, pickup);
        if (dropoff) bookingStop(r.drawn, dropoff);
    }

    /** The trail reached stop n: its number comes in and the chip pops. */
    function popStop(chip, n) {
        chip.apply(function (el) {
            var span = el.querySelector('span[data-n="' + n + '"]');
            if (span) { span.classList.remove('wait'); span.classList.remove('pop'); void span.offsetWidth; span.classList.add('pop'); }
            el.classList.remove('pop'); void el.offsetWidth; el.classList.add('pop');
        });
    }

    /** Arrows at fixed fractions of one leg: each leg says which way it goes, even where legs overlap. */
    function legArrows(color, scale) {
        var icon = { path: google.maps.SymbolPath.FORWARD_CLOSED_ARROW, scale: scale || 4.4, fillColor: color, fillOpacity: 1, strokeColor: '#ffffff', strokeWeight: 1.6 };
        return [{ icon: icon, offset: '28%', repeat: '0' }, { icon: icon, offset: '62%', repeat: '0' }];
    }

    /**
     * The selected vehicle's whole day, read in order 1 → 2 → 3: numbered stops (Dispatch colours),
     * each leg drawn on its own with its own arrows, so the order reads even where the van drove the
     * same road twice. Legs already driven in gray, the one under way in the line's colour up to the
     * vehicle, the rest of the plan dashed. On selection the driven road is drawn leg by leg from
     * stop 1, and each stop pops as the trail reaches it; it stops where the vehicle is now.
     */
    function drawWholeDay(id, r, v, color, current) {
        stopAnimation();
        var key = id + ':day';
        var animate = animatedKey !== key;
        var chips = {};

        // The legs, in order. Later legs on top: the order of the day is the order of the layers.
        var trail = [];       // driven road, in order, with the stop each piece ends at
        r.legs.forEach(function (leg, i) {
            var path = decode(leg), z = 30 + i * 2;
            if (leg.driven) {
                trail.push({ path: path, stop: leg.toStop, z: z });
            } else if (leg.current) {
                var p = splitAt(path, v.pos.lat, v.pos.lng);
                trail.push({ path: p.done, stop: null, z: z });
                lineWith(r.drawn, p.ahead, color, 8, 1, z + 60, { icons: legArrows(color, 4.6) });
            } else {
                r.drawn.push(new google.maps.Polyline({ path: path, map: map, strokeOpacity: 0, zIndex: 25, clickable: false,
                    icons: [{ icon: { path: 'M 0,-1 0,1', strokeOpacity: 0.6, strokeColor: color, strokeWeight: 4, scale: 1 }, offset: '0', repeat: '14px' }]
                        .concat(legArrows(color, 3.6).map(function (a) { a.icon.strokeOpacity = 0.7; a.icon.fillOpacity = 0.9; return a; })) }));
            }
        });

        // Stops: reached ones wait faded until the trail reaches them, when they pop.
        var nextStop = current ? current.toStop : null;
        stopGroups(r.stops).forEach(function (group) {
            var hasNext = group.some(function (st) { return st.n === nextStop; });
            var m = stopChip(r.drawn, group, color, hasNext ? 48 : 40);
            group.forEach(function (st) {
                chips[st.n] = m;
                var reached = st.performed || st.arrived;
                m.apply(function (el) {
                    var span = el.querySelector('span[data-n="' + st.n + '"]');
                    if (animate && st.n !== nextStop && reached) span.classList.add('wait');
                });
            });
            if (hasNext) m.flag('next', true);
        });
        var first = r.legs.filter(function (l) { return l.fromGarage; })[0];
        if (first) marker(r.drawn, { lat: first.from[0], lng: first.from[1] }, garageIcon(), 39);

        if (!animate) {
            trail.forEach(function (t) { lineWith(r.drawn, t.path, DRIVEN, 7, 0.95, t.z, { icons: legArrows(DRIVEN) }); });
            return;
        }
        animatedKey = key;

        // Leg by leg: the time each one takes is shared out by its length, with a floor so a short leg
        // is still seen.
        var pieces = trail.filter(function (t) { return t.path.length > 1; }).map(function (t) {
            var cum = [0], total = 0;
            for (var i = 1; i < t.path.length; i++) { total += google.maps.geometry.spherical.computeDistanceBetween(t.path[i - 1], t.path[i]); cum.push(total); }
            var casing = new google.maps.Polyline({ path: [], map: map, strokeColor: '#ffffff', strokeOpacity: 0.95, strokeWeight: 11, zIndex: t.z, clickable: false });
            var pl = new google.maps.Polyline({ path: [], map: map, strokeColor: DRIVEN, strokeOpacity: 0.95, strokeWeight: 7, zIndex: t.z + 1, clickable: false });
            r.drawn.push(casing, pl);
            return { t: t, cum: cum, total: total, casing: casing, pl: pl };
        });
        var sum = pieces.reduce(function (a, q) { return a + q.total; }, 0) || 1;
        var budget = Math.min(8000, 2200 + pieces.length * 650);   // slow enough to follow 1 → 2 → 3
        var start = performance.now(), at = 0;
        pieces.forEach(function (q) { q.ms = Math.max(550, budget * q.total / sum); q.from = at; at += q.ms; });

        var head = new google.maps.Marker({ map: map, zIndex: 400, clickable: false, icon: { path: google.maps.SymbolPath.CIRCLE, scale: 6, fillColor: color, fillOpacity: 1, strokeColor: '#ffffff', strokeWeight: 2.5 } });
        r.drawn.push(head);
        var idx = 0;
        anim = { frame: 0, key: key };
        var frame = function (now) {
            var elapsed = now - start;
            while (idx < pieces.length) {
                var q = pieces[idx], k = Math.min(1, (elapsed - q.from) / q.ms);
                if (k <= 0) break;
                var reach = q.total * (1 - Math.pow(1 - k, 2)), n = 1;
                while (n < q.cum.length && q.cum[n] <= reach) n++;
                var path = q.t.path.slice(0, n);
                if (n < q.t.path.length && q.cum[n] > q.cum[n - 1]) {
                    path.push(google.maps.geometry.spherical.interpolate(q.t.path[n - 1], q.t.path[n], (reach - q.cum[n - 1]) / (q.cum[n] - q.cum[n - 1])));
                }
                q.casing.setPath(path); q.pl.setPath(path);
                head.setPosition(path[path.length - 1]);
                if (k < 1) break;
                // This leg is done: its arrows appear and its stop pops.
                q.pl.setOptions({ icons: legArrows(DRIVEN) });
                var chip = q.t.stop != null ? chips[q.t.stop] : null;
                if (chip) popStop(chip, q.t.stop);
                idx++;
            }
            if (idx < pieces.length) anim.frame = requestAnimationFrame(frame);
            else { head.setMap(null); anim = null; setTimeout(function () { if (layers.selected === id && layers.wholeDay) drawRoute(id); }, 900); }
        };
        anim.frame = requestAnimationFrame(frame);
    }

    function fitRoute(id) {
        var r = routes[id], v = vehicles[id];
        if (!r && !v) return;
        var bounds = new google.maps.LatLngBounds(), n = 0;
        var current = r ? r.legs.filter(function (l) { return l.current; })[0] : null;
        var trip = r && current ? (stopByNumber(r, current.toStop) || {}).trip : null;
        if (r) r.stops.forEach(function (st) {
            if (layers.wholeDay || st.trip === trip) { bounds.extend({ lat: st.lat, lng: st.lng }); n++; }
        });
        if (v) { bounds.extend(new google.maps.LatLng(v.pos.lat, v.pos.lng)); n++; }
        // The camera comes closer: the selection is what the user wants to look at.
        if (n > 1) map.fitBounds(bounds, { top: 150, bottom: 110, left: 500, right: 360 });   // clear of both side panels
        else if (v) { map.panTo(new google.maps.LatLng(v.pos.lat, v.pos.lng)); map.setZoom(15); }
    }

    // ================================================================ delays

    function drawTraffic() {
        Object.keys(traffic).forEach(function (id) { clear(traffic[id].drawn); traffic[id].drawn = []; });
        Object.keys(vehicles).forEach(function (id) { setBadge(vehicles[id], 0); });
        if (!layers.delays) return;

        Object.keys(traffic).forEach(function (id) {
            var t = traffic[id], v = vehicles[id];
            if (!t.path || !v || !v.data.shown) return;
            var path = google.maps.geometry.encoding.decodePath(t.path);
            (t.intervals || []).forEach(function (i) {
                var color = SPEED[i.speed];
                if (!color) return;                        // NORMAL: nothing to point out
                line(t.drawn, path.slice(i.s, Math.max(i.e, i.s + 1) + 1), color, 6, 1, 50);
            });
            setBadge(v, t.delay);
        });
    }

    // ================================================================ messages

    function redrawAll() {
        Object.keys(vehicles).forEach(function (id) { render(vehicles[id]); });
        Object.keys(routes).forEach(function (id) { drawRoute(+id); });
        drawTraffic();
    }

    function fit(ids) {
        var bounds = new google.maps.LatLngBounds(), count = 0;
        (ids || Object.keys(vehicles)).forEach(function (id) {
            var e = vehicles[id];
            if (!e || !e.data.shown) return;
            bounds.extend(new google.maps.LatLng(e.pos.lat, e.pos.lng)); count++;
        });
        if (count === 0) return;
        if (count === 1) { map.setCenter(bounds.getCenter()); map.setZoom(14); return; }
        map.fitBounds(bounds, { top: 90, bottom: 90, left: 300, right: 300 });
    }

    function view(m) {
        var type = m.mapTypeId === 'satellite' ? (m.labels ? 'hybrid' : 'satellite') : 'roadmap';
        map.setMapTypeId(type);
        map.setOptions({ styles: type === 'roadmap' && !m.labels ? noLabels : null });
    }

    function onMessage(m) {
        if (!m || !m.type) return;
        switch (m.type) {
            case 'vehicles': upsertVehicles(m.items || []); break;
            case 'move': move(m); break;
            case 'legs':
                if (routes[m.id]) clear(routes[m.id].drawn);
                routes[m.id] = { legs: m.legs || [], stops: m.stops || [], drawn: [] };
                drawRoute(m.id);
                break;
            case 'layers':
                var newSelected = m.selected == null ? null : m.selected;
                var selectionChanged = layers.selected !== newSelected;
                var modeChanged = layers.wholeDay !== !!m.wholeDay;
                layers = { routes: !!m.routes, traffic: !!m.traffic, delays: !!m.delays, selected: newSelected, wholeDay: !!m.wholeDay, lang: m.lang || 'en' };
                if (selectionChanged || modeChanged) { stopAnimation(); animatedKey = null; }
                if (layers.traffic && !trafficLayer) trafficLayer = new google.maps.TrafficLayer();
                if (trafficLayer) trafficLayer.setMap(layers.traffic ? map : null);
                redrawAll();
                if (layers.selected !== null && (selectionChanged || modeChanged)) fitRoute(layers.selected);
                break;
            case 'traffic':
                Object.keys(traffic).forEach(function (id) { clear(traffic[id].drawn); });
                traffic = {};
                (m.items || []).forEach(function (t) { traffic[t.id] = { path: t.path, intervals: t.intervals, delay: t.delay, drawn: [] }; });
                drawTraffic();
                break;
            case 'fit': fit(m.ids); break;
            case 'fitRoute': fitRoute(m.id); break;
            case 'zoom': map.setZoom(map.getZoom() + (m.delta || 0)); break;
            case 'view': view(m); break;
        }
    }

    // Read-only snapshot for diagnosis from the WebView2 debugger. Ids, flags and counts only.
    window.__liveMapState = function () {
        return {
            layers: layers,
            vehicles: Object.keys(vehicles).map(function (id) { var v = vehicles[id]; return { id: +id, shown: v.data.shown, stale: v.data.stale, moving: v.data.moving }; }),
            routes: Object.keys(routes).map(function (id) {
                var r = routes[id], c = r.legs.filter(function (l) { return l.current; })[0];
                return { id: +id, legs: r.legs.length, withShape: r.legs.filter(function (l) { return !!l.path; }).length, current: c ? { visible: c.visible, toStop: c.toStop } : null, drawn: r.drawn.length };
            }),
            traffic: Object.keys(traffic).length
        };
    };

    // Diagnosis: redraws one line and returns the error, if any, instead of losing it.
    window.__liveMapRedraw = function (id) {
        try { drawRoute(id); return { ok: true, drawn: routes[id] ? routes[id].drawn.length : null, stops: routes[id] ? routes[id].stops.map(function (s) { return [s.n, s.kind, s.trip, s.arrived]; }) : null }; }
        catch (e) { return { ok: false, error: String(e && e.stack || e) }; }
    };

    RM.ready().then(function () {
        map = new google.maps.Map(document.getElementById('map'), {
            center: { lat: 26.6166, lng: -81.8333 }, zoom: 10, mapTypeId: 'roadmap',
            disableDefaultUI: true, clickableIcons: false, gestureHandling: 'greedy', keyboardShortcuts: false
        });
        defineLayer();
        defineHtmlMarker();
        map.addListener('click', function () { RM.post({ type: 'mapClick' }); });
        overlay = new VehicleLayer();
        overlay.setMap(map);
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', function (e) { onMessage(e.data); });
        }
        RM.post({ type: 'ready' });
    }).catch(function (err) {
        RM.post({ type: 'error', message: String(err && err.message || err) });
    });
})();
