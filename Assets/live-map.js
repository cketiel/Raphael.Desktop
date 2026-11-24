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

    // A passenger van, side-on, facing right, drawn to read at 24-40 px: a white body with depth, tinted
    // glass with a reflection, wheels with rims, lights front and back, and a stripe in the group's
    // colour. Ids are made unique per call so many vans on one page never share a gradient.
    var vanSeq = 0;
    function vanSvg(stripe, width) {
        var n = 'v' + (++vanSeq);
        return '<svg class="lm-van" viewBox="0 0 48 26" width="' + (width || 28) + '" height="' + Math.round((width || 28) * 26 / 48) + '">' +
            '<defs>' +
            '<linearGradient id="' + n + 'b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset=".55" stop-color="#eef2f6"/><stop offset="1" stop-color="#b9c3cd"/></linearGradient>' +
            '<linearGradient id="' + n + 'g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3d5a73"/><stop offset=".55" stop-color="#1b2a38"/><stop offset="1" stop-color="#101a24"/></linearGradient>' +
            '<radialGradient id="' + n + 'w" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#d9dee3"/><stop offset=".55" stop-color="#8e99a4"/><stop offset="1" stop-color="#4a535c"/></radialGradient>' +
            '</defs>' +
            '<ellipse cx="24" cy="23.6" rx="21" ry="1.9" fill="#0b1620" opacity=".28"/>' +
            // body: long roof, sloped windscreen, short bonnet
            '<path d="M3.5 7.2C3.5 5.4 4.8 4 6.6 4H30.5c1.1 0 2.1.5 2.8 1.4l5.3 6.4 4.6 1.3c1.6.5 2.8 2 2.8 3.7v2.6c0 1.1-.9 2-2 2H5.4c-1.1 0-1.9-.9-1.9-1.9z" fill="url(#' + n + 'b)" stroke="#5d6b78" stroke-width=".7"/>' +
            // group stripe
            '<path d="M3.6 14.2H45.6v1.9H3.6z" fill="' + stripe + '"/>' +
            // windows: three side windows and the windscreen
            '<path d="M6.4 6.2h7.4v5.6H6.4zM15.2 6.2h7.4v5.6h-7.4zM24 6.2h6.1c.6 0 1.1.3 1.5.7l4 4.9H24z" fill="url(#' + n + 'g)"/>' +
            '<path d="M7 6.8l3.2 0-2.6 4.4H7zM15.8 6.8h3.2l-2.6 4.4h-.6zM24.6 6.8h3l-2.5 4.4h-.5z" fill="#ffffff" opacity=".22"/>' +
            // sliding door seam, handle, mirror
            '<path d="M14.5 6v13.4" stroke="#7d8a96" stroke-width=".5"/><path d="M19.4 13h2.2" stroke="#55626e" stroke-width=".8" stroke-linecap="round"/>' +
            '<path d="M35.6 10.6l1.6-.9.6 1.4-1.6.4z" fill="#33414d"/>' +
            // lights
            '<path d="M44.6 14.5h1.5v1.8h-1.9z" fill="#fff4c2" stroke="#c9a94a" stroke-width=".4"/>' +
            '<path d="M3.6 8.6h1.2v3.4H3.6z" fill="#d43c3c"/>' +
            // bumpers
            '<path d="M41 19.6h5c.3 0 .6.3.6.6v.6c0 .3-.3.6-.6.6h-5z" fill="#3c4650"/><path d="M3.4 19.6h4.2v1.8H3.4z" fill="#3c4650"/>' +
            // wheels: arch shadow, tyre, rim, hub
            '<circle cx="12" cy="20.4" r="4.6" fill="#2a333c"/><circle cx="36" cy="20.4" r="4.6" fill="#2a333c"/>' +
            '<circle cx="12" cy="20.4" r="3.7" fill="#14191e"/><circle cx="36" cy="20.4" r="3.7" fill="#14191e"/>' +
            '<circle cx="12" cy="20.4" r="2.1" fill="url(#' + n + 'w)"/><circle cx="36" cy="20.4" r="2.1" fill="url(#' + n + 'w)"/>' +
            '<circle cx="12" cy="20.4" r=".6" fill="#3a434c"/><circle cx="36" cy="20.4" r=".6" fill="#3a434c"/>' +
            '</svg>';
    }

    // The van seen from above, nose up (north). It is turned to the GPS heading, so it always points
    // where it goes, in any direction: tinted windscreen at the front, roof panel, rear glass, mirrors
    // and a stripe in the group's colour along the roof.
    var topSeq = 0;
    function topVanSvg(stripe, height) {
        var n = 't' + (++topSeq), h = height || 24, w = Math.round(h * 20 / 36);
        return '<svg class="lm-van" viewBox="0 0 20 36" width="' + w + '" height="' + h + '">' +
            '<defs><linearGradient id="' + n + 'b" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#c4ccd4"/><stop offset=".22" stop-color="#ffffff"/><stop offset=".78" stop-color="#ffffff"/><stop offset="1" stop-color="#c4ccd4"/></linearGradient>' +
            '<linearGradient id="' + n + 'g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4a6884"/><stop offset="1" stop-color="#16222e"/></linearGradient></defs>' +
            '<ellipse cx="10" cy="19" rx="9.6" ry="16.6" fill="#0b1620" opacity=".25"/>' +
            // mirrors
            '<path d="M1.2 9.6l-1 .6v1.6h1.4zM18.8 9.6l1 .6v1.6h-1.4z" fill="#33414d"/>' +
            // body: rounded nose, long box
            '<path d="M3.2 6.4C3.2 3.4 6 1.6 10 1.6s6.8 1.8 6.8 4.8V32c0 1.4-1.1 2.4-2.4 2.4H5.6c-1.3 0-2.4-1-2.4-2.4z" fill="url(#' + n + 'b)" stroke="#5d6b78" stroke-width=".6"/>' +
            // headlights
            '<path d="M4.6 3.4l1.8-.9.3 1.1-1.9.6zM15.4 3.4l-1.8-.9-.3 1.1 1.9.6z" fill="#fff4c2"/>' +
            // windscreen and its glint
            '<path d="M4.4 9.2C5.6 7.6 7.6 7 10 7s4.4.6 5.6 2.2l-.8 3.4H5.2z" fill="url(#' + n + 'g)"/>' +
            '<path d="M6.2 8.6c1-.6 2.2-.8 3.6-.8l-1.6 3.6H5.6z" fill="#fff" opacity=".22"/>' +
            // roof with the group stripe
            '<rect x="5" y="13.6" width="10" height="16.4" rx="1.2" fill="#f4f7fa" stroke="#c9d1d9" stroke-width=".5"/>' +
            '<rect x="8.6" y="13.6" width="2.8" height="16.4" fill="' + stripe + '"/>' +
            // rear glass and tail lights
            '<path d="M5.4 31h9.2v1.6H5.4z" fill="url(#' + n + 'g)"/>' +
            '<path d="M3.4 32.6h1.6v1.4H3.6zM15 32.6h1.6v1.4H15z" fill="#d43c3c"/>' +
            '</svg>';
    }

    // God's Eye View's detection callout (Street Traffic), copied: four L-shaped corner brackets round the
    // vehicle (1.25 px in the tier colour over a light 2.6 px casing at 35 %, each arm 55 % of the half-side), a
    // 1 px leader at 60 % from the bracket's top-right corner to the card, 8 px across and 12 px up, and a
    // card on a translucent dark plate (rgba(2,18,26,.46), 3 px corners) with a 2 px accent bar, the id in
    // 10 px JetBrains Mono and a 9 px micro-field at 80 %. Here the tier colour is the line's pace (green,
    // yellow, red), the id is the line's name, and the micro-field the delay (+N') or else the speed.
    var BR = 19, BR_SEG = Math.max(4, Math.floor(BR * 0.55));
    function bracketPath(c, h) {
        var x0 = c - h, y0 = c - h, x1 = c + h, y1 = c + h, s = BR_SEG;
        return 'M' + x0 + ' ' + (y0 + s) + 'V' + y0 + 'H' + (x0 + s) +
               'M' + (x1 - s) + ' ' + y0 + 'H' + x1 + 'V' + (y0 + s) +
               'M' + x1 + ' ' + (y1 - s) + 'V' + y1 + 'H' + (x1 - s) +
               'M' + (x0 + s) + ' ' + y1 + 'H' + x0 + 'V' + (y1 - s);
    }
    function callout(d) {
        var color = d.routeColor || '#1FA35A', p = bracketPath(20, BR);
        var micro = d.delay > 0 ? '+' + d.delay + "'" : (d.moving && d.speedMph >= 1 ? Math.round(d.speedMph) + 'mph' : '');
        return '<svg class="lm-bracket" viewBox="0 0 40 40" width="40" height="40">' +
                   '<path d="' + p + '" fill="none" stroke="rgba(5,8,12,0.35)" stroke-width="2.6" stroke-linecap="square"/>' +
                   '<path d="' + p + '" fill="none" stroke="' + color + '" stroke-width="1.25" stroke-linecap="square"/></svg>' +
               '<svg class="lm-lead" width="10" height="14" viewBox="0 0 10 14"><path d="M0.5 13.5L8.5 1.5" stroke="' + color + '" stroke-width="1" stroke-opacity=".6"/></svg>' +
               '<span class="lm-callout" style="--accent:' + color + '"><b>' + escapeHtml(d.name || '') + '</b>' +
                   (micro ? '<i>' + escapeHtml(micro) + '</i>' : '') + '</span>';
    }

    function vehicleHtml(d, heading) {
        var pointer = '';   // the aerial van itself points the way (2026-11-22)
        // A speedometer and the mph: what is wrong, and by how much.
        var speed = d.speeding
            ? '<span class="lm-speed"><svg viewBox="0 0 24 24"><path fill="#fff" d="M12 4a10 10 0 0 0-8.66 15h17.32A10 10 0 0 0 12 4zm0 2a8 8 0 0 1 6.93 11H5.07A8 8 0 0 1 12 6zm4.24 2.76-4.95 4.95a1.5 1.5 0 1 0 2.12 2.12l2.83-7.07z"/></svg>' + Math.round(d.speedMph || 0) + '<small>mph</small></span>'
            : '';
        var pause = d.moving ? '' : '<span class="lm-pause"><svg viewBox="0 0 12 12"><rect x="3" y="2.5" width="2" height="7" fill="#fff"/><rect x="7" y="2.5" width="2" height="7" fill="#fff"/></svg></span>';
        return pointer +
            '<span class="lm-disc" style="background:' + d.color + ';color:' + d.color + '"><span class="lm-rot" style="transform:rotate(' + (heading || 0) + 'deg)">' + topVanSvg(d.color, 25) + '</span></span>' +
            pause + speed + callout(d);
    }


    // ================================================================ hover card
    // The vehicle's details in our dark glass instead of the browser's grey tooltip: name and pace,
    // driver and vehicle, group and trips, speed and the time of the last report. Built from the
    // same lines the host sends as the vehicle's title, so the text never disagrees with the panels.
    var card = null;
    function showCard(entry) {
        if (!entry || !entry.data) return;
        if (!card) { card = document.createElement('div'); card.id = 'lm-vcard'; document.body.appendChild(card); }
        var d = entry.data, lines = String(d.title || '').split('\n');
        var rows = lines.slice(1, 3).map(function (l) {
            var i = l.indexOf(':');
            return i < 0 ? '' : '<div class="r"><span>' + escapeHtml(l.slice(0, i)) + '</span><b>' + escapeHtml(l.slice(i + 1).trim()) + '</b></div>';
        }).join('');
        var pace = d.routeColor || '#1FA35A';
        var late = d.delay > 0 ? '<span class="late" style="background:' + pace + ';color:' + (pace === '#E5B21A' ? '#1E2A36' : '#fff') + '">+' + d.delay + "'</span>" : '';
        var speed = d.speeding ? '<span class="spd">' + Math.round(d.speedMph || 0) + ' mph</span>' : '';
        card.innerHTML =
            '<div class="h"><i style="background:' + pace + '"></i><b>' + escapeHtml(lines[0] || d.name || '') + '</b>' + late + speed + '</div>' +
            rows +
            '<div class="f"><span class="g" style="background:' + (d.color || '#5B6B7C') + '"></span>' + escapeHtml(lines[3] || '') + '</div>' +
            '<div class="t">' + escapeHtml(lines[4] || '') + '</div>';
        var r = entry.el.getBoundingClientRect();
        var x = r.right + 10, y = r.top - 6;
        card.style.left = '0px'; card.style.top = '0px'; card.classList.add('on');
        var w = card.offsetWidth, h = card.offsetHeight;
        if (x + w > innerWidth - 8) x = r.left - w - 10;
        if (y + h > innerHeight - 8) y = innerHeight - h - 8;
        card.style.left = Math.max(8, x) + 'px'; card.style.top = Math.max(8, y) + 'px';
    }
    function hideCard() { if (card) card.classList.remove('on'); }

    function escapeHtml(t) {
        return String(t).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
    }

    function render(entry) {
        var d = entry.data;
        entry.el.innerHTML = vehicleHtml(d, entry.heading);
        entry.el.removeAttribute('title');
        var sel = layers.selected;
        entry.el.classList.toggle('stale', !!d.stale);
        entry.el.classList.toggle('hidden', !d.shown);
        entry.el.classList.toggle('speeding', !!d.speeding);
        entry.el.classList.toggle('selected', sel === d.id);
        entry.el.classList.toggle('named', layers.routes || sel === d.id);
        entry.el.style.setProperty('--route', d.routeColor || '#fff');
        // +N': minutes late (punctuality) or minutes the traffic adds (delays layer), in the pace's colour.
        setBadge(entry, d.delay, d.routeColor);
        if (tripFocus && tripFocus.id === d.id) {
            var badge = document.createElement('div');
            badge.className = 'lm-tripbadge';
            badge.textContent = tripFocus.label;
            entry.el.appendChild(badge);
        }
    }
    var tripFocus = null;

    function setHeading(entry, heading) {
        entry.heading = heading;
        var p = entry.el.querySelector('.lm-rot');
        if (p) p.style.transform = 'rotate(' + heading + 'deg)';
    }


    function setBadge(entry, minutes, color) {
        return;   // the delay is the callout's micro-field now (God's Eye style); kept for the call sites
        var badge = entry.el.querySelector('.lm-delay');
        if (!minutes || minutes < 1) { if (badge) badge.remove(); return; }
        if (!badge) { badge = document.createElement('span'); badge.className = 'lm-delay'; entry.el.appendChild(badge); }
        badge.textContent = '+' + minutes + "'";
        if (color) { badge.style.background = color; badge.style.color = color === '#E5B21A' ? '#1E2A36' : '#fff'; }
    }

    function VehicleLayer() { this.div = null; }

    function defineLayer() {
        VehicleLayer.prototype = new google.maps.OverlayView();
        VehicleLayer.prototype.onAdd = function () {
            if (!vlayer) {
                vlayer = document.createElement('div');
                vlayer.id = 'lm-vlayer';
                document.getElementById('map').insertAdjacentElement('afterend', vlayer);
            }
            this.div = vlayer;
            Object.keys(vehicles).forEach(function (id) { vlayer.appendChild(vehicles[id].el); });
        };
        VehicleLayer.prototype.draw = function () { placeAll(); };
        VehicleLayer.prototype.onRemove = function () { };
    }

    // Vehicles live in a layer of their own over the map and are placed with the map's own Web Mercator
    // maths from its current centre and zoom. Google's overlay projection is only refreshed when the
    // camera rests, so under Follow (a camera that never rests) icons placed with it drifted off their
    // vehicles by hundreds of pixels (2026-11-22). From the live centre and zoom they are always right.
    var vlayer = null, tlayer = null, topMarkers = [];
    function worldPx(lat, lng, scale) {
        var s = Math.sin(Math.max(-85.05, Math.min(85.05, lat)) * Math.PI / 180);
        return { x: scale * (lng + 180) / 360, y: scale * (0.5 - Math.log((1 + s) / (1 - s)) / (4 * Math.PI)) };
    }
    var viewCache = null;
    function camView() {
        if (!map) return null;
        var c = map.getCenter(), z = map.getZoom(), el = document.getElementById('map');
        if (!c || z == null) return null;
        var scale = 256 * Math.pow(2, z);
        return { c: worldPx(c.lat(), c.lng(), scale), scale: scale, w: el.clientWidth, h: el.clientHeight };
    }
    function place(entry, v) {
        v = v || camView();
        if (!v || !entry.pos) return;
        var p = worldPx(entry.pos.lat, entry.pos.lng, v.scale);
        var x = p.x - v.c.x, y = p.y - v.c.y;
        // across the antimeridian, take the nearer copy of the world
        if (x > v.scale / 2) x -= v.scale; else if (x < -v.scale / 2) x += v.scale;
        entry.el.style.left = (x + v.w / 2) + 'px';
        entry.el.style.top = (y + v.h / 2) + 'px';
    }
    function placeAll() {
        viewCache = camView();
        Object.keys(vehicles).forEach(function (id) { place(vehicles[id], viewCache); });
        topMarkers.forEach(function (m) { if (m.div) place({ el: m.div, pos: m.position }, viewCache); });
        arrangeCallouts();
    }

    // Callouts never sit on one another: as God's Eye does, each tries its corners in turn (NE, NW, SE,
    // SW) and takes the first that is clear of the callouts and vehicles already placed; the selected
    // vehicle chooses first. Re-solved on every camera change and twice a second while vehicles move.
    var CORNERS = ['ne', 'nw', 'se', 'sw'], lastArrange = 0;
    function arrangeCallouts() {
        lastArrange = performance.now();
        var list = Object.keys(vehicles).map(function (id) { return vehicles[id]; })
            .filter(function (e) { return e.data.shown && e.el.offsetParent !== null; });
        list.sort(function (a, b) { return (b.data.id === layers.selected) - (a.data.id === layers.selected); });
        var taken = list.map(function (e) {
            var x = parseFloat(e.el.style.left), y = parseFloat(e.el.style.top);
            return { x: x - 20, y: y - 20, w: 40, h: 40 };            // every vehicle's own box is taken
        });
        list.forEach(function (e) {
            var card = e.el.querySelector('.lm-callout');
            if (!card) return;
            var w = card.offsetWidth || 60, h = 19, x = parseFloat(e.el.style.left) - 20, y = parseFloat(e.el.style.top) - 20;
            var boxes = { ne: { x: x + 47, y: y - 30 }, nw: { x: x - 7 - w, y: y - 30 }, se: { x: x + 47, y: y + 51 }, sw: { x: x - 7 - w, y: y + 51 } };
            var pick = CORNERS.filter(function (k) {
                var r = { x: boxes[k].x, y: boxes[k].y, w: w, h: h };
                return !taken.some(function (t) { return r.x < t.x + t.w + 2 && r.x + r.w + 2 > t.x && r.y < t.y + t.h + 2 && r.y + r.h + 2 > t.y; });
            })[0] || 'ne';
            CORNERS.forEach(function (k) { e.el.classList.toggle('co-' + k, k === pick); });
            taken.push({ x: boxes[pick].x, y: boxes[pick].y, w: w, h: h });
        });
    }

    function upsertVehicles(items) {
        var seen = {};
        items.forEach(function (d) {
            seen[d.id] = true;
            var entry = vehicles[d.id];
            if (!entry) {
                var el = document.createElement('div');
                el.className = 'lm-v';
                el.addEventListener('click', function (e) { e.stopPropagation(); hideCard(); RM.post({ type: 'vehicleClick', id: d.id, double: false }); });
                el.addEventListener('mouseenter', function () { showCard(vehicles[d.id]); });
                el.addEventListener('mouseleave', hideCard);
                // The wheel over a vehicle still zooms the map (under Follow the vehicle is always under the cursor).
                el.addEventListener('wheel', function (ev) { ev.preventDefault(); map.setZoom(map.getZoom() + (ev.deltaY < 0 ? 1 : -1)); }, { passive: false });
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
        e.data.speedMph = m.speedMph;
        render(e);
        // Where it was really reported; the drawn position cruises from there (see "always moving").
        e.target = { lat: m.lat, lng: m.lng, heading: shortestTurn(e.heading, m.heading) };
        report(e, m);
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

    // ================================================================ always moving (dead reckoning)
    // The Driver app reports every ~30 s. Between two reports a moving vehicle keeps going along the
    // road of the leg it is on, at its reported speed, for at most CRUISE_MAX_MS; a new report is then
    // blended in over GLIDE_MS (the gap between where it was drawn and where it really is fades out),
    // so it never jumps. Stopped, stale or without speed, it stays where it was reported. Nothing is
    // invented beyond the next 45 s of a road it is already driving.
    var CRUISE_MAX_MS = 45000, CRUISE_MIN_MPH = 3, MPH_TO_MPS = 0.44704, OFF_ROAD_M = 150;
    var cruising = false, follow = null;

    function metres(a, b) {
        var k = Math.cos(a.lat * Math.PI / 180) * 111320, dy = (b.lat - a.lat) * 110540, dx = (b.lng - a.lng) * k;
        return Math.sqrt(dx * dx + dy * dy);
    }
    function bearing(a, b) {
        var y = (b.lng - a.lng) * Math.cos(a.lat * Math.PI / 180), x = b.lat - a.lat;
        return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /** The road ahead of a report: the current leg's shape from the point nearest to it, or null. */
    function roadAhead(id, lat, lng) {
        var r = routes[id];
        var leg = r && r.legs.filter(function (l) { return l.current; })[0];
        if (!leg || !leg.path) return null;
        var parts = splitAt(decode(leg), lat, lng);
        var ahead = parts.ahead.map(function (p) { return { lat: p.lat(), lng: p.lng() }; });
        if (ahead.length < 2 || metres({ lat: lat, lng: lng }, ahead[0]) > OFF_ROAD_M) return null;   // off its road: no guessing
        return ahead;
    }

    /** Where the vehicle is `dist` metres further along the road (or straight on, with no road). */
    function along(c, dist) {
        if (c.road) {
            for (var i = 0; i < c.road.length - 1; i++) {
                var a = c.road[i], b = c.road[i + 1], seg = metres(a, b);
                if (dist <= seg) { var t = seg ? dist / seg : 0; return { lat: a.lat + (b.lat - a.lat) * t, lng: a.lng + (b.lng - a.lng) * t, heading: bearing(a, b) }; }
                dist -= seg;
            }
            var end = c.road[c.road.length - 1];
            return { lat: end.lat, lng: end.lng, heading: c.heading };           // reached the stop: wait there
        }
        var h = c.heading * Math.PI / 180;
        return { lat: c.lat + dist * Math.cos(h) / 110540, lng: c.lng + dist * Math.sin(h) / (Math.cos(c.lat * Math.PI / 180) * 111320), heading: c.heading };
    }

    /** A real report: cruise from it, and fade out the gap to where the vehicle was drawn. */
    function report(e, m) {
        var mph = m.speedMph || 0;
        e.cruise = (m.moving && !m.stale && mph >= CRUISE_MIN_MPH)
            ? { lat: m.lat, lng: m.lng, heading: m.heading || e.heading, mps: mph * MPH_TO_MPS, at: performance.now(), road: roadAhead(+e.data.id, m.lat, m.lng) }
            : null;
        var base = e.cruise ? { lat: m.lat, lng: m.lng } : { lat: m.lat, lng: m.lng };
        e.gap = { lat: e.pos.lat - base.lat, lng: e.pos.lng - base.lng, at: performance.now() };
        e.headingFrom = e.heading;
        if (!cruising) { cruising = true; requestAnimationFrame(cruiseStep); }
    }

    function cruiseStep(now) {
        var any = false;
        // The camera moves first (Follow), and only then are the vehicles placed: placed before, they used
        // the projection of the old centre and stayed off by the camera's move (seen 2026-11-22).
        // Follow keeps the vehicle in view at every zoom, cruising or not, with an eased camera; a hand
        // drag pauses it and it comes back after.
        if (follow != null && vehicles[follow] && !dragging) {
            var tgt = vehicles[follow].pos;
            if (map3d && document.getElementById('map3d').style.display === 'block') {
                map3d.center = { lat: tgt.lat, lng: tgt.lng, altitude: 0 };
            } else {
                // Eased, every frame: the camera covers a share of the gap to the vehicle that depends on
                // the time since the last frame (exponential smoothing, time constant FOLLOW_TAU). At a
                // steady speed it settles a little behind the vehicle and moves exactly as fast, so it
                // glides with it instead of catching up in steps. Far away (a jump, a long gap) it cuts.
                var dt = Math.min(0.1, Math.max(0, (now - (lastFollowAt || now)) / 1000));
                lastFollowAt = now;
                var cv = camView();
                if (cv) {
                    var wp = worldPx(tgt.lat, tgt.lng, cv.scale);
                    var dx = wp.x - cv.c.x, dy = wp.y - cv.c.y;
                    if (Math.abs(dx) > cv.w || Math.abs(dy) > cv.h) map.moveCamera({ center: tgt });
                    else if (Math.abs(dx) > 0.3 || Math.abs(dy) > 0.3) {
                        var k = 1 - Math.exp(-dt / FOLLOW_TAU);
                        var c0 = map.getCenter();
                        map.moveCamera({ center: { lat: c0.lat() + (tgt.lat - c0.lat()) * k, lng: c0.lng() + (tgt.lng - c0.lng()) * k } });
                    }
                }
            }
        }
        viewCache = camView();
        Object.keys(vehicles).forEach(function (id) {
            var e = vehicles[id];
            if (!e.target) { if (follow != null) place(e, viewCache); return; }
            var c = e.cruise, p;
            if (c && now - c.at <= CRUISE_MAX_MS) { p = along(c, c.mps * (now - c.at) / 1000); any = true; }
            else p = { lat: e.target.lat, lng: e.target.lng, heading: e.target.heading };

            // The gap to the drawn position fades out over GLIDE_MS (ease out).
            var g = e.gap, k = g ? Math.min(1, (now - g.at) / GLIDE_MS) : 1, fade = 1 - (1 - Math.pow(1 - k, 3));
            if (g && k < 1) any = true;
            e.pos = { lat: p.lat + (g ? g.lat * fade : 0), lng: p.lng + (g ? g.lng * fade : 0) };
            setHeading(e, k < 1 ? e.headingFrom + (shortestTurn(e.headingFrom, p.heading) - e.headingFrom) * (1 - fade) : shortestTurn(e.heading, p.heading));
            place(e, viewCache);
            if (k >= 1 && e.gap) { e.gap = null; if (!(layers.selected === +id && anim)) drawRoute(+id); }
        });
        if (now - lastArrange > 500) arrangeCallouts();
        if (any || follow != null) requestAnimationFrame(cruiseStep); else cruising = false;
    }

    // Follow: the camera stays on one vehicle until Follow is pressed again, Esc, or another selection.
    // Dragging the map only pauses it while the button is held. Nothing else moves the camera then.
    var dragging = false, lastFollowAt = 0;
    var FOLLOW_TAU = 0.6;   // seconds: how closely the camera trails the vehicle (smaller = tighter)
    function setFollow(id) {
        follow = id;
        if (id != null && vehicles[id]) { map.setZoom(Math.max(map.getZoom(), 15)); map.setCenter(vehicles[id].pos); }
        if (!cruising) { cruising = true; requestAnimationFrame(cruiseStep); }
    }
    function stopFollow() {
        if (follow == null) return;
        follow = null;
        RM.post({ type: 'followStopped' });
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
            if (this.cls && this.cls.indexOf('lm-top') >= 0) {
                // Over the vehicles' layer (a stop's label must never sit under a van), placed with the
                // same live-camera maths as the vehicles.
                if (!tlayer) { tlayer = document.createElement('div'); tlayer.id = 'lm-tlayer'; (vlayer || document.getElementById('map')).insertAdjacentElement('afterend', tlayer); }
                tlayer.appendChild(this.div);
                topMarkers.push(this);
                this.draw();
                return;
            }
            this.getPanes().overlayLayer.appendChild(this.div);
        };
        HtmlMarker.prototype.draw = function () {
            if (this.div && this.cls && this.cls.indexOf('lm-top') >= 0) { place({ el: this.div, pos: this.position }); return; }
            var projection = this.getProjection();
            if (!projection || !this.div) return;
            var p = projection.fromLatLngToDivPixel(new google.maps.LatLng(this.position.lat, this.position.lng));
            this.div.style.left = p.x + 'px';
            this.div.style.top = p.y + 'px';
        };
        HtmlMarker.prototype.onRemove = function () {
            var self = this;
            topMarkers = topMarkers.filter(function (m) { return m !== self; });
            if (this.div) this.div.remove(); this.div = null;
        };
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
        var html = '<div class="lm-pin"><svg width="30" height="38" viewBox="0 0 12 16"><path d="M6 0a6 6 0 0 0-6 6c0 4.4 6 10 6 10s6-5.6 6-10a6 6 0 0 0-6-6z" fill="' + fill + '" stroke="#fff" stroke-width=".9"/><circle cx="6" cy="6" r="2.2" fill="#fff"/></svg></div>';
        htmlMarker(drawn, { lat: st.lat, lng: st.lng }, html, 60);
        // The label goes on the top layer, so a vehicle at its stop never covers it.
        htmlMarker(drawn, { lat: st.lat, lng: st.lng }, '<span class="lm-pinlabel lm-stoplabel" style="--accent:' + fill + '">' + escapeHtml(text) + '</span>', 60, 'lm-top');
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
            else if (current.toGarage) marker(r.drawn, { lat: current.to[0], lng: current.to[1] }, garageIcon(), 39);
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
        // The drive home (Pull-in): no trip, the road behind in gray and the rest to the garage.
        if (current.toGarage) {
            var home = splitAt(decode(current), v.pos.lat, v.pos.lng);
            lineWith(r.drawn, home.done, DRIVEN, 7, 0.95, 30, { icons: arrows(DRIVEN, '150px') });
            lineWith(r.drawn, home.ahead, color, 8, 1, 32, { icons: arrows(color, '150px') });
            marker(r.drawn, { lat: current.to[0], lng: current.to[1] }, garageIcon(), 39);
            return;
        }
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
                trail.push({ path: path, stop: leg.toGarage ? null : leg.toStop, z: z });
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
        var home = r.legs.filter(function (l) { return l.toGarage; })[0];
        if (first) marker(r.drawn, { lat: first.from[0], lng: first.from[1] }, garageIcon(), 39);
        else if (home) marker(r.drawn, { lat: home.to[0], lng: home.to[1] }, garageIcon(), 39);

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

    // F1: the help of the part of the page under the mouse, else of the tray that is open.
    function postHelp() {
        var over = function (sel) { return !!document.querySelector(sel + ':hover'); };
        var open = function (sel) { var p = document.querySelector(sel); return p && !p.classList.contains('collapsed') && !p.hidden; };
        var topic = open('#key-setup') || over('#key-setup-chip') ? 'power-up' : over('#gev-voice-control') ? 'voice'
            : over('#location-bar') ? 'search' : over('#control-panel') || over('#style-indicator') ? 'view'
            : open('#location-bar') ? 'search' : open('#control-panel') ? 'view' : 'overview';
        RM.post({ type: 'help', topic: topic });
    }

    var hostActions = null;   // set by initActions
    var rightPad = 360;       // what the panels on the right cover; more while the In progress list is open

    function fitRoute(id) {
        if (follow != null) return;                       // Follow owns the camera
        if (is3d()) { frame3dRoute(id); return; }
        var r = routes[id], v = vehicles[id];
        if (!r && !v) return;
        var bounds = new google.maps.LatLngBounds(), n = 0;
        var current = r ? r.legs.filter(function (l) { return l.current; })[0] : null;
        var trip = r && current ? (stopByNumber(r, current.toStop) || {}).trip : null;
        if (r) r.stops.forEach(function (st) {
            if (layers.wholeDay || st.trip === trip) { bounds.extend({ lat: st.lat, lng: st.lng }); n++; }
        });
        if (current && current.toGarage) { bounds.extend({ lat: current.to[0], lng: current.to[1] }); n++; }
        if (v) { bounds.extend(new google.maps.LatLng(v.pos.lat, v.pos.lng)); n++; }
        // The camera comes closer: the selection is what the user wants to look at.
        if (n > 1) map.fitBounds(bounds, { top: 150, bottom: 110, left: 500, right: rightPad });   // clear of both side panels
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
        if (follow != null) return;                       // Follow owns the camera
        if (is3d()) { frame3d(ids); return; }
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

    var setActionTexts = function () { };
    function initActions() {
    // ------------------------------------------------------------------ top-centre actions (God's Eye's)
    // Five glass circles, God's Eye's #top-center-actions: Street View, map view, full screen, fit
    // the vehicles, own window. Hovering one grows it and opens a card with its name and what it
    // does, so the four that look alike are told apart before the click.
    var ACTION_ICONS = { streetview: 'streetview', camera: 'control_camera', fullscreen: 'fullscreen', fit: 'fit_screen', window: 'open_in_new', panels: 'visibility_off' };
    var actionTexts = {
        streetview: { title: 'Street View', desc: 'Pick a spot on the map to see the street' },
        camera: { title: 'Map view', desc: 'Zoom and move the map step by step' },
        fullscreen: { title: 'Full screen', desc: 'The map over the whole screen · Esc leaves' },
        fit: { title: 'Fit the vehicles', desc: 'Frame every vehicle shown' },
        window: { title: 'Own window', desc: 'Move the map to a window of its own' },
        panels: { title: 'Hide panels', desc: 'Only the map, without the windows on top · H' },
        panelsShow: { title: 'Show panels', desc: 'Bring the windows back on top of the map · H' },
        exitStreetView: 'Exit Street View', pickSpot: 'Click the street to look at · Esc cancels'
    };
    var actions = document.getElementById('lm-actions'), tip = document.getElementById('lm-action-tip');

    function showTip(btn) {
        var t = btn.dataset.k === 'panels' && clean ? actionTexts.panelsShow : actionTexts[btn.dataset.k]; if (!t) return;
        tip.querySelector('.lm-tip-title').textContent = t.title;
        tip.querySelector('.lm-tip-desc').textContent = t.desc;
        var r = btn.getBoundingClientRect();
        tip.style.left = (r.left + r.width / 2) + 'px';
        tip.classList.add('visible');
    }
    function hideTip() { tip.classList.remove('visible'); }

    Object.keys(ACTION_ICONS).forEach(function (k) {
        var b = document.createElement('button');
        b.type = 'button'; b.dataset.k = k;
        b.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">' + ACTION_ICONS[k] + '</span>';
        b.addEventListener('mouseenter', function () { showTip(b); });
        b.addEventListener('focus', function () { showTip(b); });
        b.addEventListener('mouseleave', hideTip);
        b.addEventListener('blur', hideTip);
        b.addEventListener('click', function (e) { e.stopPropagation(); hideTip(); runAction(k); });
        actions.appendChild(b);
    });
    function labelActions() {
        actions.querySelectorAll('button[data-k]').forEach(function (b) {
            var t = actionTexts[b.dataset.k]; if (t) b.setAttribute('aria-label', t.title + ' — ' + t.desc);
        });
        document.getElementById('lm-sv-exit-label').textContent = actionTexts.exitStreetView;
        document.getElementById('lm-sv-pick-label').textContent = actionTexts.pickSpot;
    }
    labelActions();

    function runAction(k) {
        if (k === 'streetview') { pickStreetView(!picking); return; }
        if (k === 'camera') { document.getElementById('lm-camera').classList.toggle('open'); return; }
        if (k === 'fullscreen') RM.post({ type: 'fullScreen' });
        if (k === 'fit') RM.post({ type: 'fitFleet' });
        if (k === 'window') RM.post({ type: 'toggleWindow' });
        if (k === 'panels') setClean(!clean);
    }

    // Only the map: every window drawn on top goes (the host hides its own), the actions stay so they
    // can be brought back. H does the same.
    var clean = false;
    function setClean(on) {
        clean = on;
        document.body.classList.toggle('lm-clean', on);
        var b = actions.querySelector('[data-k="panels"]');
        b.querySelector('.material-symbols-outlined').textContent = on ? 'visibility' : 'visibility_off';
        b.classList.toggle('active', on);
        if (on) document.getElementById('lm-camera').classList.remove('open');
        RM.post({ type: 'cleanView', on: on });
    }
    document.addEventListener('keydown', function (e) {
        var t = e.target;
        if ((e.key === 'h' || e.key === 'H') && !e.ctrlKey && !e.altKey && !(t && t.matches && t.matches('input, textarea, select'))) setClean(!clean);
    });

    // Map view: zoom and pan in steps, for those who do not drag or scroll.
    document.querySelectorAll('#lm-camera [data-cam]').forEach(function (b) {
        b.addEventListener('click', function (e) {
            e.stopPropagation();
            var c = b.dataset.cam, step = 160;
            if (c === 'in') map.setZoom(map.getZoom() + 1);
            else if (c === 'out') map.setZoom(map.getZoom() - 1);
            else map.panBy(c === 'left' ? -step : c === 'right' ? step : 0, c === 'up' ? -step : c === 'down' ? step : 0);
        });
    });

    // Street View: click the button, then the street. The blue coverage shows where there is one.
    var picking = false, coverage = null, pano = null;
    function pickStreetView(on) {
        picking = on;
        document.body.classList.toggle('lm-sv-picking', on);
        actions.querySelector('[data-k="streetview"]').classList.toggle('active', on);
        if (!coverage) coverage = new google.maps.StreetViewCoverageLayer();
        coverage.setMap(on ? map : null);
        map.setOptions({ draggableCursor: on ? 'crosshair' : null });
    }
    function openStreetView(latLng) {
        // About 30 screen pixels around the click, in metres at this zoom: a click on a zoomed-out
        // map cannot hit a street within a few metres.
        var metresPerPixel = 156543.03392 * Math.cos(latLng.lat() * Math.PI / 180) / Math.pow(2, map.getZoom());
        var radius = Math.min(5000, Math.max(50, metresPerPixel * 30));
        new google.maps.StreetViewService().getPanorama({ location: latLng, radius: radius, preference: google.maps.StreetViewPreference.NEAREST }, function (data, status) {
            if (status !== 'OK') {
                // Stay in pick mode and say why nothing opened.
                document.getElementById('lm-sv-pick-label').textContent = actionTexts.noImagery || 'No street imagery here · try a street';
                return;
            }
            pickStreetView(false);
            document.getElementById('lm-sv-pick-label').textContent = actionTexts.pickSpot;
            pano.setPano(data.location.pano);
            pano.setPov({ heading: 0, pitch: 0 });
            pano.setVisible(true);
        });
    }
    function setupStreetView() {
        pano = map.getStreetView();
        // While Street View is open every panel steps aside (the host hides its own) and the exit is a
        // big labelled button at the top: Google's controls are no longer under our windows.
        pano.setOptions({
            enableCloseButton: false, fullscreenControl: false, motionTrackingControl: false,
            addressControlOptions: { position: google.maps.ControlPosition.BOTTOM_CENTER },
            panControlOptions: { position: google.maps.ControlPosition.RIGHT_BOTTOM },
            zoomControlOptions: { position: google.maps.ControlPosition.RIGHT_BOTTOM }
        });
        pano.addListener('visible_changed', function () {
            var on = pano.getVisible();
            document.body.classList.toggle('lm-streetview', on);
            RM.post({ type: 'streetView', on: on });
        });
        document.getElementById('lm-sv-exit').addEventListener('click', function () { pano.setVisible(false); });
        map.addListener('click', function (e) { if (picking && e.latLng) openStreetView(e.latLng); });
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (pano.getVisible()) { pano.setVisible(false); e.stopImmediatePropagation(); }
            else if (picking) { pickStreetView(false); e.stopImmediatePropagation(); }
        }, true);
    }

    setActionTexts = function (t) {
        if (!t) return; actionTexts = t; labelActions();
        if (t.window && t.window.icon) actions.querySelector('[data-k="window"] .material-symbols-outlined').textContent = t.window.icon;
    };
    setupStreetView();
    // What the voice assistant may press here (RE-086).
    hostActions = {
        clean: function (on) { if (clean !== on) setClean(on); },
        streetView: function (on) { if (!on && pano.getVisible()) pano.setVisible(false); else if (picking !== on) pickStreetView(on); }
    };
    }

    // ------------------------------------------------------------------ 3D (Maps JavaScript, maps3d)
    // Photorealistic 3D over the 2D map, centred where the 2D map is, with the vehicles as 3D
    // markers. Loaded only when chosen, so it costs nothing until then.
    var map3d = null, markers3d = {};
    async function show3d(on) {
        var host = document.getElementById('map3d');
        if (!on) { if (host) host.style.display = 'none'; clearInterval(timer3d); timer3d = null; return { ok: true }; }
        try {
            var lib = await google.maps.importLibrary('maps3d');
            if (!map3d) {
                var c = map.getCenter();
                map3d = new lib.Map3DElement({
                    center: { lat: c.lat(), lng: c.lng(), altitude: 0 },
                    range: Math.max(800, 40000000 / Math.pow(2, map.getZoom())), tilt: 60, heading: 0,
                    mode: lib.MapMode ? lib.MapMode.HYBRID : 'HYBRID'
                });
                host.appendChild(map3d);
            } else {
                var c2 = map.getCenter();
                map3d.center = { lat: c2.lat(), lng: c2.lng(), altitude: 0 };
            }
            host.style.display = 'block';
            draw3dVehicles(lib);
            draw3dRoutes();
            if (follow == null) { if (layers.selected != null) frame3dRoute(layers.selected); else frame3d(); }
            // The vans keep moving in 3D too (their drawn positions, once a second); the roads, every 5 s.
            clearInterval(timer3d);
            var tick = 0;
            timer3d = setInterval(function () { draw3dVehicles(); if (++tick % 5 === 0) draw3dRoutes(); }, 1000);
            return { ok: true };
        } catch (e) {
            if (host) host.style.display = 'none';
            RM.post({ type: 'error', message: '3D map: ' + String(e && e.message || e) });
            return { ok: false, error: String(e && e.message || e) };
        }
    }
    // The marker: our disc and van as one image, in the group's colour with the pace ring.
    function marker3dImage(d, heading) {
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="56" height="56" viewBox="0 0 56 56">' +
            '<circle cx="28" cy="28" r="25" fill="' + (d.routeColor || '#1FA35A') + '"/>' +
            '<circle cx="28" cy="28" r="21" fill="' + d.color + '" stroke="#fff" stroke-width="3"/>' +
            '<g transform="rotate(' + Math.round(heading || 0) + ' 28 28) translate(18 8) scale(1.11)">' + topVanSvg(d.color, 36).replace(/^<svg[^>]*>/, '').replace(/<\/svg>$/, '') + '</g></svg>';
        var img = document.createElement('img');
        img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
        img.width = 44; img.height = 44;
        return img;
    }

    var lib3d = null, timer3d = null;
    function draw3dVehicles(lib) {
        if (!map3d) return;
        lib3d = lib || lib3d;
        var seen = {};
        Object.keys(vehicles).forEach(function (id) {
            var v = vehicles[id];
            if (!v.data.shown || !v.pos) return;
            seen[id] = true;
            var m = markers3d[id];
            var key = v.data.color + v.data.routeColor + Math.round(v.heading / 15);   // redrawn every 15° of turn
            if (!m) {
                var Cls = lib3d.Marker3DInteractiveElement || lib3d.Marker3DElement;
                m = markers3d[id] = new Cls({ altitudeMode: 'RELATIVE_TO_GROUND', extruded: true, label: v.data.name || '' });
                m.addEventListener('gmp-click', function () { RM.post({ type: 'vehicleClick', id: +id, double: false }); });
                map3d.append(m);
            }
            if (m._key !== key) {
                m.replaceChildren();
                var tpl = document.createElement('template');
                tpl.content.appendChild(marker3dImage(v.data, v.heading));
                m.append(tpl);
                m._key = key;
            }
            m.position = { lat: v.pos.lat, lng: v.pos.lng, altitude: 30 };
        });
        Object.keys(markers3d).forEach(function (id) { if (!seen[id]) { markers3d[id].remove(); delete markers3d[id]; } });
    }

    function is3d() { return !!map3d && document.getElementById('map3d').style.display === 'block'; }

    /** The 3D camera over some points: their middle, far enough back to hold all of them. */
    function frame3dPoints(pts) {
        if (!pts.length || !map3d) return;
        var la = pts.map(function (p) { return p.lat; }), ln = pts.map(function (p) { return p.lng; });
        var c = { lat: (Math.min.apply(null, la) + Math.max.apply(null, la)) / 2, lng: (Math.min.apply(null, ln) + Math.max.apply(null, ln)) / 2 };
        var span = Math.max(metres({ lat: Math.min.apply(null, la), lng: c.lng }, { lat: Math.max.apply(null, la), lng: c.lng }),
                            metres({ lat: c.lat, lng: Math.min.apply(null, ln) }, { lat: c.lat, lng: Math.max.apply(null, ln) }));
        map3d.center = { lat: c.lat, lng: c.lng, altitude: 0 };
        map3d.range = Math.max(1200, span * 1.9);
    }

    /** The shown fleet (or the given lines) in 3D. */
    function frame3d(ids) {
        frame3dPoints((ids || Object.keys(vehicles)).map(function (id) { return vehicles[id]; })
            .filter(function (v) { return v && v.data.shown && v.pos; }).map(function (v) { return v.pos; }));
    }

    /** A selected line in 3D: its vehicle and the stops of its trip (or of its day). */
    function frame3dRoute(id) {
        var r = routes[id], v = vehicles[id], pts = [];
        var current = r ? r.legs.filter(function (l) { return l.current; })[0] : null;
        var trip = r && current ? (stopByNumber(r, current.toStop) || {}).trip : null;
        if (r) r.stops.forEach(function (st) { if (layers.wholeDay || st.trip === trip) pts.push({ lat: st.lat, lng: st.lng }); });
        if (current && current.toGarage) pts.push({ lat: current.to[0], lng: current.to[1] });
        if (v && v.pos) pts.push(v.pos);
        frame3dPoints(pts);
    }

    // Routes in 3D: the selected line's legs (driven grey, ahead in its pace) and its stops; with
    // "Routes in progress", every line's current leg. Ground-hugging, drawn over the buildings.
    var drawn3d = [];
    function draw3dRoutes() {
        if (!lib3d || !map3d) return;
        drawn3d.forEach(function (x) { x.remove(); });
        drawn3d = [];
        var Line = lib3d.Polyline3DElement;
        if (!Line) return;
        function add(path, color, width) {
            if (!path || path.length < 2) return;
            var p = new Line({ altitudeMode: 'CLAMP_TO_GROUND', strokeColor: color, strokeWidth: width, outerColor: '#ffffff', outerWidth: 0.35, drawsOccludedSegments: true });
            p.coordinates = path.map(function (q) { return { lat: q.lat(), lng: q.lng() }; });
            map3d.append(p); drawn3d.push(p);
        }
        Object.keys(routes).forEach(function (key) {
            var id = +key, r = routes[id], v = vehicles[id];
            if (!r || !v || !v.data.shown) return;
            var color = v.data.routeColor || '#1FA35A';
            var selected = layers.selected === id;
            if (!selected && !layers.routes) return;
            var current = r.legs.filter(function (l) { return l.current; })[0];
            if (!selected) {
                if (current && current.visible) add(splitAt(decode(current), v.pos.lat, v.pos.lng).ahead, color, 7);
                return;
            }
            var trip = current ? (stopByNumber(r, current.toStop) || {}).trip : null;
            r.legs.forEach(function (l) {
                var to = stopByNumber(r, l.toStop) || {};
                if (!layers.wholeDay && (l.toGarage ? !l.current : to.trip !== trip)) return;
                if (l.current) {
                    var parts = splitAt(decode(l), v.pos.lat, v.pos.lng);
                    add(parts.done, DRIVEN, 6); add(parts.ahead, color, 9);
                } else add(decode(l), l.driven ? DRIVEN : color, l.driven ? 6 : 8);
            });
            r.stops.forEach(function (st) {
                if (!layers.wholeDay && st.trip !== trip) return;
                var m = new (lib3d.Marker3DElement)({ altitudeMode: 'RELATIVE_TO_GROUND', extruded: true, position: { lat: st.lat, lng: st.lng, altitude: 20 } });
                var img = document.createElement('img'); img.src = stopIcon(st).url; img.width = 26; img.height = 26;
                var tpl = document.createElement('template'); tpl.content.appendChild(img); m.append(tpl);
                map3d.append(m); drawn3d.push(m);
            });
        });
    }
    window.__liveMap3d = show3d;
    // Diagnosis only: the camera's state, read through the debugging port.
    window.__liveMapDebug = function () { return { follow: follow, selected: layers.selected, dragging: dragging, cruising: cruising, is3d: is3d(), center: map && map.getCenter().toJSON(), zoom: map && map.getZoom(), pos: follow != null && vehicles[follow] ? vehicles[follow].pos : null, rect: follow != null && vehicles[follow] ? vehicles[follow].el.getBoundingClientRect().toJSON() : null }; };

    // The dock (live-map-dock/dock.js) flies the map and changes its type without a round trip.
    window.__liveMapFly = function (lat, lng, zoom) { if (!map) return; map.panTo({ lat: lat, lng: lng }); if (zoom) map.setZoom(zoom); };
    window.__liveMapView = function (m) { if (map) view(m); };

    function onMessage(m) {
        if (!m || !m.type) return;
        handleMessage(m);
        // In 3D the roads are redrawn whenever the selection, the layers or a line's legs change.
        if ((m.type === 'layers' || m.type === 'legs') && is3d()) draw3dRoutes();
    }

    function handleMessage(m) {
        switch (m.type) {
            case 'vehicles': upsertVehicles(m.items || []); break;
            case 'move': move(m); break;
            case 'rightPad': rightPad = m.px; break;
            case 'pan': map.panBy(m.direction === 'left' ? -240 : m.direction === 'right' ? 240 : 0, m.direction === 'up' ? -180 : m.direction === 'down' ? 180 : 0); break;
            case 'streetViewPick': if (hostActions) hostActions.streetView(!!m.on); break;
            case 'helpHere': postHelp(); break;
            case 'setClean': if (hostActions) hostActions.clean(!!m.on); break;
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
            case 'redraw3d': if (is3d()) draw3dRoutes(); break;
            case 'zoom': map.setZoom(map.getZoom() + (m.delta || 0)); break;
            case 'follow': if (m.id == null) follow = null; else setFollow(m.id); break;
            case 'mapButtons': setActionTexts(m.texts); break;
            case 'view': view(m); break;
            case 'tripFocus':
                // A trip found in the dock's Search: its Trip Id on the vehicle carrying it.
                var before = tripFocus;
                tripFocus = m.id != null && m.label ? { id: m.id, label: m.label } : null;
                if (before && vehicles[before.id]) render(vehicles[before.id]);
                if (tripFocus && vehicles[tripFocus.id]) render(vehicles[tripFocus.id]);
                break;
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

    // Any script error on this page reaches the Desktop's log instead of failing silently.
    window.addEventListener('error', function (e) { RM.post({ type: 'error', message: 'script: ' + (e.message || e) + ' @' + (e.lineno || '?') }); });

    RM.ready().then(function () {
        map = new google.maps.Map(document.getElementById('map'), {
            center: { lat: 26.6166, lng: -81.8333 }, zoom: 10, mapTypeId: 'roadmap',
            // Map · No labels is the default: the vehicles and the routes are the labels that matter.
            styles: noLabels,
            disableDefaultUI: true, clickableIcons: false, gestureHandling: 'greedy', keyboardShortcuts: false,
            // Google's own controls stay off: the top-centre actions (initActions) replace them.
            streetViewControl: false, fullscreenControl: false
        });
        initActions();
        // Esc reaches the page, not the window: the host uses it to leave full screen.
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { stopFollow(); RM.post({ type: 'escapeKey' }); } });
        // F1 here never reaches WPF: the host opens the help of the part of the page that has the focus.
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'F1') return;
            e.preventDefault();
            postHelp();
        });
        map.addListener('dragstart', function () { dragging = true; });
        map.addListener('dragend', function () { dragging = false; });
        defineLayer();
        defineHtmlMarker();
        map.addListener('click', function () { RM.post({ type: 'mapClick' }); });
        overlay = new VehicleLayer();
        overlay.setMap(map);
        map.addListener('bounds_changed', placeAll);
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', function (e) { onMessage(e.data); });
        }
        RM.post({ type: 'ready' });
    }).catch(function (err) {
        RM.post({ type: 'error', message: String(err && err.message || err) });
    });
})();
