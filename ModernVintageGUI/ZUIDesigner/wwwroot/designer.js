// Drag and drop for the ModernVintageGUI designer.
//
// The server draws the dialog with the real layout code and sends back one box per control -
// the hit map. Everything the cursor does is answered from that list here in the browser:
// asking the server which container the pointer is over would put a network round trip between
// the mouse and the highlight, and the highlight has to keep up with the mouse.
//
// The server is told once, on drop, and it edits the markup. The markup is the source of truth;
// nothing in this file changes the picture on its own.

window.mvgDesigner = (function () {
    "use strict";

    /** @type {any} */ let dotnet = null;
    /** @type {Array<any>} */ let nodes = [];
    let selectedPath = null;
    let hoverPath = null;

    let imageWidth = 1;
    let imageHeight = 1;

    /** The drag in progress, or null. */
    let drag = null;

    // How far the pointer has to travel on a control before it counts as a drag rather than a
    // click. Without it every attempt to select a control would nudge it somewhere else.
    const DRAG_THRESHOLD = 4;

    // An empty container measures to twice its padding, which can be sixteen pixels square. The
    // drop zone drawn over it is at least this big so it can be aimed at.
    const EMPTY_ZONE = 34;

    // The title bar has one place it belongs - first inside the root - and the server puts it
    // there whatever the pointer says. The drag has to show that, or the caret would promise
    // somewhere the control is not going to end up.
    const PINNED_TO_TOP = "TitleBar";

    function el(id) { return document.getElementById(id); }

    function canvas() { return el("mvg-canvas"); }
    function overlay() { return el("mvg-overlay"); }
    function image() { return el("mvg-image"); }

    /** Device pixels of the rendered image per CSS pixel on screen. */
    function zoom() {
        const img = image();
        if (!img || !img.clientWidth || !imageWidth) return 1;
        return img.clientWidth / imageWidth;
    }

    /** Pointer position in the coordinate space of the rendered image. */
    function toImageSpace(event) {
        const img = image();
        if (!img) return null;

        const box = img.getBoundingClientRect();
        const z = zoom();

        return {
            x: (event.clientX - box.left) / z,
            y: (event.clientY - box.top) / z,
            inside: event.clientX >= box.left && event.clientX <= box.right &&
                    event.clientY >= box.top && event.clientY <= box.bottom
        };
    }

    /**
     * The box a node can be aimed at. Normally its own, but an empty container is grown to the
     * drop zone drawn over it so that the pointer can reach something that is barely there.
     */
    function hitRect(node) {
        if (!node.empty) return node;

        const w = Math.max(node.w, EMPTY_ZONE);
        const h = Math.max(node.h, EMPTY_ZONE);

        return {
            x: node.x - (w - node.w) / 2,
            y: node.y - (h - node.h) / 2,
            w: w,
            h: h
        };
    }

    function contains(node, x, y) {
        const r = hitRect(node);

        return x >= r.x && x <= r.x + r.w &&
               y >= r.y && y <= r.y + r.h;
    }

    function childrenOf(path) {
        return nodes.filter(n => n.parent === path);
    }

    function isInside(path, ancestor) {
        return path === ancestor || path.startsWith(ancestor + "/");
    }

    /** The deepest node under the pointer, container or not. Used for selecting and hovering. */
    function nodeAt(x, y) {
        let best = null;

        for (const node of nodes) {
            if (!contains(node, x, y)) continue;
            if (!best || node.depth > best.depth) best = node;
        }

        return best;
    }

    // ----------------------------------------------------------------- drop resolution

    /**
     * Where a control dropped at (x, y) would land: which container takes it, at which index
     * among that container's children, and the caret to draw for it.
     *
     * The deepest container under the pointer wins, so dropping onto a button inside a row puts
     * the control into the row next to that button rather than into the row's parent. A
     * container that is full - a tab page already holding a control - is skipped and the search
     * carries on outwards.
     */
    function resolveDrop(x, y, excludePath, tag) {
        if (tag === PINNED_TO_TOP) return titleBarDrop();

        const candidates = nodes
            .filter(n => n.container && contains(n, x, y))
            .sort((a, b) => b.depth - a.depth);

        for (const target of candidates) {
            // A container cannot be dropped into itself or into its own contents.
            if (excludePath && isInside(target.path, excludePath)) continue;

            const kids = childrenOf(target.path).sort(childOrder(target.axis));

            const movingWithin = excludePath && kids.some(k => k.path === excludePath);
            const effective = movingWithin ? kids.length - 1 : kids.length;

            if (effective >= target.capacity) continue;

            const spot = insertionSpot(target, kids, x, y);

            return { target: target, index: spot.index, caret: spot.caret };
        }

        return null;
    }

    /** The one place a title bar goes, wherever the pointer happens to be. */
    function titleBarDrop() {
        const root = nodes.find(n => !n.parent);
        if (!root) return null;

        return {
            target: root,
            index: 0,
            caret: { kind: "caret-h", x: root.x, y: root.y, w: Math.max(4, root.w), h: 0 }
        };
    }

    function childOrder(axis) {
        if (axis === "h") return (a, b) => a.x - b.x;
        if (axis === "v") return (a, b) => a.y - b.y;
        return () => 0;
    }

    /**
     * The index a drop at (x, y) means, and the caret that shows it. Children are compared
     * against their midpoint along the stacking axis, which is the same rule the layout uses to
     * put them there: before a child whose middle is past the pointer, after one whose middle is
     * behind it.
     */
    function insertionSpot(target, kids, x, y) {
        if (target.axis === "z" || kids.length === 0) {
            // Nothing to sit between: the whole inside of the container is the target. For an
            // empty one that is the drop zone rather than the box, which is barely there.
            const r = hitRect(target);
            const pad = target.empty ? 0 : target.pad;

            return {
                index: kids.length,
                caret: {
                    kind: "area",
                    x: r.x + pad,
                    y: r.y + pad,
                    w: Math.max(2, r.w - pad * 2),
                    h: Math.max(2, r.h - pad * 2)
                }
            };
        }

        const vertical = target.axis === "v";
        const position = vertical ? y : x;

        let index = kids.length;

        for (let i = 0; i < kids.length; i++) {
            const kid = kids[i];
            const middle = vertical ? kid.y + kid.h / 2 : kid.x + kid.w / 2;

            if (position < middle) { index = i; break; }
        }

        // The caret sits on the gap the control would be inserted into: at the leading edge of
        // the child it goes before, or after the last one when it goes at the end.
        let line;

        if (index < kids.length) {
            const kid = kids[index];
            line = vertical ? kid.y : kid.x;
        } else {
            const last = kids[kids.length - 1];
            line = vertical ? last.y + last.h : last.x + last.w;
        }

        const from = vertical ? target.x + target.pad : target.y + target.pad;
        const span = vertical
            ? Math.max(4, target.w - target.pad * 2)
            : Math.max(4, target.h - target.pad * 2);

        return {
            index: index,
            caret: vertical
                ? { kind: "caret-h", x: from, y: line, w: span, h: 0 }
                : { kind: "caret-v", x: line, y: from, w: 0, h: span }
        };
    }

    // ----------------------------------------------------------------- the outline

    /**
     * Where a drop on an outline row would land.
     *
     * The rows are the document, so the answer comes from the row's own path: "r/1/2" sits at
     * index 2 of "r/1". The band the pointer is in decides between putting the control into that
     * row and putting it beside the row:
     *
     *     ---- top quarter ......... before this row, in its parent
     *         middle .............. inside this row, when it is a container with room
     *     ---- bottom quarter ...... after this row, in its parent
     *
     * A row that is not a container, or one that is full, has no middle - the whole row splits
     * into before and after, so a list of leaves can still be reordered by dragging over it.
     */
    function resolveOutlineDrop(row, event, excludePath) {
        const path = row.getAttribute("data-mvg-path");
        const tag = row.getAttribute("data-mvg-tag");
        const capacity = parseInt(row.getAttribute("data-mvg-capacity"), 10) || 0;
        const kids = parseInt(row.getAttribute("data-mvg-children"), 10) || 0;

        if (excludePath && isInside(path, excludePath)) return null;

        const parent = parentOf(path);
        const box = row.getBoundingClientRect();
        const offset = (event.clientY - box.top) / box.height;

        const movingOut = excludePath && parentOf(excludePath) === path;
        const roomInside = capacity > (movingOut ? kids - 1 : kids);

        // Into this row, when it can take it and the pointer is not on an edge.
        if (roomInside && offset > 0.25 && offset < 0.75) {
            return { targetPath: path, index: kids, row: row, into: true };
        }

        // Beside it, which needs a parent to be beside it in - the root has none, so a pointer
        // on its edges still means "inside the root".
        if (!parent) {
            return roomInside ? { targetPath: path, index: kids, row: row, into: true } : null;
        }

        const own = indexOf(path);

        return {
            targetPath: parent,
            index: offset < 0.5 ? own : own + 1,
            row: row,
            into: false,
            after: offset >= 0.5
        };
    }

    function parentOf(path) {
        const cut = path.lastIndexOf("/");
        return cut < 0 ? null : path.slice(0, cut);
    }

    function indexOf(path) {
        const cut = path.lastIndexOf("/");
        return cut < 0 ? 0 : parseInt(path.slice(cut + 1), 10) || 0;
    }

    /** The row a title bar would go to: the first child of the root. */
    function outlineTitleBarDrop() {
        const rows = [...document.querySelectorAll("#mvg-outline [data-mvg-path]")];
        const root = rows.find(r => r.getAttribute("data-mvg-path") === "r");

        return root ? { targetPath: "r", index: 0, row: rows[1] || root, into: false, after: false } : null;
    }

    function paintOutline() {
        document.querySelectorAll("#mvg-outline .node")
            .forEach(n => n.classList.remove("drop-into", "drop-before", "drop-after"));

        if (!drag || !drag.outline) return;

        const spot = drag.outline;

        if (spot.into) spot.row.classList.add("drop-into");
        else spot.row.classList.add(spot.after ? "drop-after" : "drop-before");
    }

    // ----------------------------------------------------------------- overlay drawing

    function box(className, rect, label) {
        const div = document.createElement("div");
        div.className = "mvg-box " + className;

        const z = zoom();
        div.style.left = (rect.x * z) + "px";
        div.style.top = (rect.y * z) + "px";
        div.style.width = (rect.w * z) + "px";
        div.style.height = (rect.h * z) + "px";

        if (label) {
            const tag = document.createElement("span");
            tag.className = "mvg-box-label";
            tag.textContent = label;
            div.appendChild(tag);
        }

        return div;
    }

    function paint() {
        const layer = overlay();
        if (!layer) return;

        layer.replaceChildren();

        const byPath = path => nodes.find(n => n.path === path);

        if (hoverPath && hoverPath !== selectedPath && !drag) {
            const node = byPath(hoverPath);
            if (node) layer.appendChild(box("mvg-hover", node, null));
        }

        if (selectedPath) {
            const node = byPath(selectedPath);
            if (node) layer.appendChild(box("mvg-selected", node, node.tag + (node.name ? " " + node.name : "")));
        }

        // An empty container is only twice its padding across, so during a drag every one of
        // them gets a zone drawn over it - otherwise the only containers you could aim at would
        // be the ones that already have something in them.
        if (drag && drag.armed) {
            for (const node of nodes) {
                if (!node.empty) continue;
                if (drag.drop && drag.drop.target.path === node.path) continue;

                layer.appendChild(box("mvg-emptyzone", hitRect(node), null));
            }
        }

        if (drag && drag.drop) {
            const target = drag.drop.target;

            layer.appendChild(box("mvg-droptarget", hitRect(target),
                                  target.tag + (target.name ? " " + target.name : "")));

            const caret = drag.drop.caret;
            const z = zoom();
            const div = document.createElement("div");
            div.className = "mvg-box mvg-" + caret.kind;
            div.style.left = (caret.x * z) + "px";
            div.style.top = (caret.y * z) + "px";
            div.style.width = (caret.w * z) + "px";
            div.style.height = (caret.h * z) + "px";
            layer.appendChild(div);
        }
    }

    // ----------------------------------------------------------------- dragging

    function ghostFor(text) {
        const div = document.createElement("div");
        div.className = "mvg-ghost";
        div.textContent = text;
        document.body.appendChild(div);
        return div;
    }

    function beginDrag(kind, payload, label, event, tag) {
        const moved = kind === "move" ? nodes.find(n => n.path === payload) : null;

        drag = {
            kind: kind,               // "new" or "move"
            payload: payload,         // a tag name, or the path being moved
            // What is being dragged. A tab page has no box on the canvas, so when the drag
            // started in the outline the row is the only one who knows.
            tag: tag || (kind === "new" ? payload : (moved ? moved.tag : null)),
            drop: null,               // where it lands on the canvas
            outline: null,            // or in the outline, when the pointer is over that
            ghost: null,
            label: label,
            startX: event.clientX,
            startY: event.clientY,
            armed: kind === "new"     // a toolbox drag is a drag from the first pixel
        };

        if (drag.armed) arm();
    }

    function arm() {
        if (!drag.ghost) {
            drag.ghost = ghostFor(drag.label);
            document.body.classList.add("mvg-dragging");
        }
        drag.armed = true;
    }

    function onPointerMove(event) {
        if (!drag) {
            const point = toImageSpace(event);
            const over = point && point.inside ? nodeAt(point.x, point.y) : null;
            const path = over ? over.path : null;

            if (path !== hoverPath) { hoverPath = path; paint(); }
            return;
        }

        if (!drag.armed) {
            const travelled = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
            if (travelled < DRAG_THRESHOLD) return;
            arm();
        }

        if (drag.ghost) {
            drag.ghost.style.left = (event.clientX + 14) + "px";
            drag.ghost.style.top = (event.clientY + 14) + "px";
        }

        const exclude = drag.kind === "move" ? drag.payload : null;

        // The outline is the document written out, so it takes drops as well as the canvas. It
        // wins while the pointer is over it - the canvas is not under the cursor then anyway.
        const row = rowUnder(event);

        drag.outline = row
            ? (drag.tag === PINNED_TO_TOP
                ? outlineTitleBarDrop()
                : resolveOutlineDrop(row, event, exclude))
            : null;

        if (drag.outline) {
            drag.drop = null;
        } else {
            const point = toImageSpace(event);

            drag.drop = point && point.inside
                ? resolveDrop(point.x, point.y, exclude, drag.tag)
                : null;
        }

        if (drag.ghost) {
            drag.ghost.classList.toggle("mvg-ghost-no", !drag.drop && !drag.outline);
        }

        paint();
        paintOutline();
    }

    /** The outline row under the pointer, or null when the pointer is somewhere else. */
    function rowUnder(event) {
        const element = document.elementFromPoint(event.clientX, event.clientY);
        if (!element) return null;

        const row = element.closest("#mvg-outline [data-mvg-path]");
        return row || null;
    }

    function onPointerUp(event) {
        if (!drag) return;

        const current = drag;
        endDrag();

        if (!current.armed) {
            // Never moved far enough to be a drag, so it was a click: select what was under it.
            if (current.kind === "move") select(current.payload);
            return;
        }

        const landing = current.outline
            ? { path: current.outline.targetPath, index: current.outline.index }
            : current.drop
                ? { path: current.drop.target.path, index: current.drop.index }
                : null;

        if (!landing || !dotnet) { paint(); return; }

        if (current.kind === "new") {
            dotnet.invokeMethodAsync("DropNew", current.payload, landing.path, landing.index);
        } else {
            dotnet.invokeMethodAsync("DropMove", current.payload, landing.path, landing.index);
        }
    }

    function endDrag() {
        if (drag && drag.ghost) drag.ghost.remove();

        document.body.classList.remove("mvg-dragging");
        drag = null;
        paint();
        paintOutline();
    }

    function select(path) {
        if (selectedPath === path) return;

        selectedPath = path;
        paint();

        if (dotnet) dotnet.invokeMethodAsync("SelectPath", path);
    }

    function onPointerDown(event) {
        if (event.button !== 0) return;

        const tool = event.target.closest("[data-mvg-tool]");
        if (tool) {
            event.preventDefault();
            beginDrag("new", tool.getAttribute("data-mvg-tool"), tool.getAttribute("data-mvg-tool"), event);
            return;
        }

        const handle = event.target.closest("[data-mvg-path]");
        if (handle) {
            // An outline row: dragging it moves that control, clicking it selects it.
            event.preventDefault();
            const path = handle.getAttribute("data-mvg-path");

            // The root is the document; there is nowhere to drag it to.
            if (path === "r") { select(path); return; }

            beginDrag("move", path, handle.getAttribute("data-mvg-label") || path, event,
                      handle.getAttribute("data-mvg-tag"));
            return;
        }

        if (!canvas() || !canvas().contains(event.target)) return;

        const point = toImageSpace(event);
        if (!point || !point.inside) return;

        const node = nodeAt(point.x, point.y);
        if (!node) { select(null); return; }

        event.preventDefault();

        // The root is the document itself, so there is nowhere to drag it to.
        if (node.parent === null || node.parent === undefined) { select(node.path); return; }

        beginDrag("move", node.path, node.tag + (node.name ? " " + node.name : ""), event);
    }

    function onKeyDown(event) {
        if (!dotnet) return;

        const tag = (event.target.tagName || "").toLowerCase();
        if (tag === "input" || tag === "textarea" || tag === "select" || event.target.isContentEditable) return;

        if (event.key === "Escape" && drag) { endDrag(); return; }

        const control = event.ctrlKey || event.metaKey;

        if (event.key === "Delete" || event.key === "Backspace") {
            event.preventDefault();
            dotnet.invokeMethodAsync("KeyCommand", "delete");
        } else if (control && event.key.toLowerCase() === "z" && !event.shiftKey) {
            event.preventDefault();
            dotnet.invokeMethodAsync("KeyCommand", "undo");
        } else if (control && (event.key.toLowerCase() === "y" || (event.shiftKey && event.key.toLowerCase() === "z"))) {
            event.preventDefault();
            dotnet.invokeMethodAsync("KeyCommand", "redo");
        } else if (control && event.key.toLowerCase() === "d") {
            event.preventDefault();
            dotnet.invokeMethodAsync("KeyCommand", "duplicate");
        }
    }

    // ----------------------------------------------------------------- public surface

    return {
        init: function (reference) {
            dotnet = reference;

            document.addEventListener("pointerdown", onPointerDown);
            document.addEventListener("pointermove", onPointerMove);
            document.addEventListener("pointerup", onPointerUp);
            document.addEventListener("pointercancel", endDrag);
            document.addEventListener("keydown", onKeyDown);
            window.addEventListener("resize", paint);
        },

        /** Called after every render with the fresh hit map, as JSON. */
        update: function (json) {
            const payload = typeof json === "string" ? JSON.parse(json) : json;

            nodes = payload.nodes || [];
            imageWidth = payload.width || 1;
            imageHeight = payload.height || 1;
            selectedPath = payload.selected || null;

            if (hoverPath && !nodes.some(n => n.path === hoverPath)) hoverPath = null;

            paint();
        },

        /** Saves a file the browser built, since Blazor Server has no local file system. */
        download: function (name, text) {
            const blob = new Blob([text], { type: "application/xml" });
            const url = URL.createObjectURL(blob);

            const link = document.createElement("a");
            link.href = url;
            link.download = name;
            document.body.appendChild(link);
            link.click();
            link.remove();

            URL.revokeObjectURL(url);
        }
    };
})();
