/* Menú configurable por usuario (plan secundario, etapa 3).
   - Buscador rápido Ctrl+K (todos los estilos), con flechas, Enter y ESC.
   - Barra lateral: colapsar (se recuerda en localStorage), abrir en móvil y acordeones por módulo.
   - Barra superior: mega-menú por módulo; se cierra con ESC o al hacer clic fuera.
   - Hub: filtro en vivo, filtros por categoría y accesos frecuentes (localStorage, solo pantallas que la persona puede abrir).
   Los datos del buscador los escribe el servidor en #egm-datos, ya recortados a lo que la persona puede abrir. */
(function () {
    'use strict';

    function leer(clave) { try { return window.localStorage.getItem(clave); } catch (e) { return null; } }
    function guardar(clave, valor) { try { window.localStorage.setItem(clave, valor); } catch (e) { /* sin almacenamiento */ } }

    var nodoDatos = document.getElementById('egm-datos');
    var datos = [];
    try { datos = nodoDatos ? JSON.parse(nodoDatos.textContent || '[]') : []; } catch (e) { datos = []; }
    var cuenta = nodoDatos ? (nodoDatos.getAttribute('data-cuenta') || '') : '';
    var claveFrecuentes = 'eg360.frecuentes.' + cuenta;

    function normalizar(texto) {
        return (texto || '').toString().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    }
    function escapar(texto) {
        return (texto || '').toString().replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    datos.forEach(function (d) { d.n = normalizar(d.x); });

    // ── Accesos frecuentes: se cuenta la visita a cada pantalla del catálogo ─────────────────────
    function frecuentes() {
        var cuentas = {};
        try { cuentas = JSON.parse(leer(claveFrecuentes) || '{}') || {}; } catch (e) { cuentas = {}; }
        return cuentas;
    }
    (function registrarVisita() {
        if (!cuenta || !datos.length) return;
        var actual = window.location.pathname.toLowerCase().replace(/\/index$/, '').replace(/\/$/, '');
        var entrada = datos.find(function (d) { return d.u.toLowerCase().replace(/\/index$/, '').replace(/\/$/, '') === actual; });
        if (!entrada) return;
        var cuentas = frecuentes();
        cuentas[entrada.u] = (cuentas[entrada.u] || 0) + 1;
        guardar(claveFrecuentes, JSON.stringify(cuentas));
    })();

    function masUsadas(cuantas) {
        var cuentas = frecuentes();
        var lista = datos.filter(function (d) { return cuentas[d.u]; })
            .sort(function (a, b) { return cuentas[b.u] - cuentas[a.u]; });
        if (lista.length < cuantas) {
            datos.forEach(function (d) { if (lista.length < cuantas && lista.indexOf(d) < 0) lista.push(d); });
        }
        return lista.slice(0, cuantas);
    }

    // ── Buscador rápido (Ctrl+K) ─────────────────────────────────────────────────────────────────
    var cmd = document.getElementById('egm-cmd');
    var cmdEntrada = document.getElementById('egm-cmd-entrada');
    var cmdResultados = document.getElementById('egm-cmd-resultados');
    var seleccion = 0;

    function buscar(q) {
        var n = normalizar(q).trim();
        if (!n) return masUsadas(8);
        var partes = n.split(/\s+/);
        return datos.filter(function (d) { return partes.every(function (p) { return d.n.indexOf(p) >= 0; }); }).slice(0, 30);
    }
    function pintarCmd() {
        var lista = buscar(cmdEntrada.value);
        if (seleccion >= lista.length) seleccion = Math.max(0, lista.length - 1);
        cmdResultados.innerHTML = lista.length
            ? lista.map(function (d, i) {
                return '<a href="' + escapar(d.u) + '" class="egm-cmd-fila' + (i === seleccion ? ' sel' : '') + '">' +
                    '<i class="fas ' + escapar(d.i) + '" style="color:' + escapar(d.c) + ';width:18px;text-align:center;"></i>' +
                    '<span>' + escapar(d.t) + '</span><small>' + escapar(d.m) + '</small></a>';
            }).join('')
            : '<div class="egm-cmd-vacio">No hay pantallas que coincidan.</div>';
    }
    function abrirCmd() {
        if (!cmd) return;
        cerrarMega();
        cmd.classList.add('abierto');
        cmdEntrada.value = '';
        seleccion = 0;
        pintarCmd();
        setTimeout(function () { cmdEntrada.focus(); }, 20);
    }
    function cerrarCmd() { if (cmd) cmd.classList.remove('abierto'); }

    if (cmd) {
        cmdEntrada.addEventListener('input', function () { seleccion = 0; pintarCmd(); });
        cmdEntrada.addEventListener('keydown', function (e) {
            var filas = cmdResultados.querySelectorAll('.egm-cmd-fila');
            if (e.key === 'ArrowDown') { e.preventDefault(); seleccion = Math.min(filas.length - 1, seleccion + 1); pintarCmd(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); seleccion = Math.max(0, seleccion - 1); pintarCmd(); }
            else if (e.key === 'Enter' && filas[seleccion]) { e.preventDefault(); window.location.href = filas[seleccion].getAttribute('href'); }
        });
        cmd.addEventListener('click', function (e) { if (e.target === cmd) cerrarCmd(); });
        document.querySelectorAll('[data-egm-buscar]').forEach(function (b) {
            b.addEventListener('click', function (e) { e.preventDefault(); abrirCmd(); });
        });
    }

    // ── Barra lateral ────────────────────────────────────────────────────────────────────────────
    var raiz = document.documentElement;
    document.querySelectorAll('[data-egm-colapsar]').forEach(function (b) {
        b.addEventListener('click', function () {
            if (window.matchMedia('(max-width: 991.98px)').matches) { document.body.classList.remove('egm-side-abierta'); return; }
            var colapsado = raiz.classList.toggle('egm-colapsado');
            guardar('eg360.menu.colapsado', colapsado ? '1' : '0');
        });
    });
    document.querySelectorAll('[data-egm-side-abrir]').forEach(function (b) {
        b.addEventListener('click', function () { document.body.classList.add('egm-side-abierta'); });
    });
    document.querySelectorAll('.egm-side-velo').forEach(function (v) {
        v.addEventListener('click', function () { document.body.classList.remove('egm-side-abierta'); });
    });
    document.querySelectorAll('[data-egm-acordeon]').forEach(function (b) {
        b.addEventListener('click', function () {
            var grupo = b.closest('.egm-side-grupo');
            if (raiz.classList.contains('egm-colapsado') && !window.matchMedia('(max-width: 991.98px)').matches) {
                // Colapsada: el icono lleva al centro del módulo (o expande la barra si no lo tiene).
                var destino = b.getAttribute('data-egm-indice');
                if (destino) { window.location.href = destino; return; }
                raiz.classList.remove('egm-colapsado');
                guardar('eg360.menu.colapsado', '0');
            }
            var abierto = grupo.classList.toggle('abierto');
            b.setAttribute('aria-expanded', abierto ? 'true' : 'false');
        });
    });

    // ── Barra superior con mega-menú ─────────────────────────────────────────────────────────────
    var mega = document.getElementById('egm-mega-panel');
    var megaAbierto = null;
    function cerrarMega() {
        if (!mega) return;
        mega.classList.remove('abierto');
        megaAbierto = null;
        document.querySelectorAll('[data-egm-mega]').forEach(function (t) { t.classList.remove('abierto'); t.setAttribute('aria-expanded', 'false'); });
    }
    if (mega) {
        document.querySelectorAll('[data-egm-mega]').forEach(function (t) {
            t.addEventListener('click', function (e) {
                e.stopPropagation();
                var clave = t.getAttribute('data-egm-mega');
                if (megaAbierto === clave) { cerrarMega(); return; }
                cerrarMega();
                mega.querySelectorAll('[data-egm-mega-contenido]').forEach(function (c) {
                    c.hidden = c.getAttribute('data-egm-mega-contenido') !== clave;
                });
                mega.classList.add('abierto');
                t.classList.add('abierto');
                t.setAttribute('aria-expanded', 'true');
                megaAbierto = clave;
            });
        });
        document.addEventListener('click', function (e) { if (megaAbierto && !mega.contains(e.target)) cerrarMega(); });
    }

    // ── Atajos globales ──────────────────────────────────────────────────────────────────────────
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
            if (!cmd) return;
            e.preventDefault();
            if (cmd.classList.contains('abierto')) cerrarCmd(); else abrirCmd();
        } else if (e.key === 'Escape') {
            cerrarCmd();
            cerrarMega();
            document.body.classList.remove('egm-side-abierta');
        }
    });

    // ── Hub (Menú Principal) ─────────────────────────────────────────────────────────────────────
    var hubFiltro = document.getElementById('egm-hub-filtro');
    var tarjetas = Array.prototype.slice.call(document.querySelectorAll('[data-egm-tarjeta]'));
    var categoria = 'todos';
    function filtrarHub() {
        var n = hubFiltro ? normalizar(hubFiltro.value).trim() : '';
        var partes = n ? n.split(/\s+/) : [];
        var visibles = 0;
        tarjetas.forEach(function (t) {
            var enCategoria = categoria === 'todos' || t.getAttribute('data-egm-cat') === categoria;
            var texto = normalizar(t.getAttribute('data-egm-texto'));
            var coincide = partes.every(function (p) { return texto.indexOf(p) >= 0; });
            var mostrar = enCategoria && coincide;
            t.hidden = !mostrar;
            if (mostrar) visibles++;
            t.querySelectorAll('.egm-sublink').forEach(function (s) {
                var st = normalizar(s.textContent);
                s.classList.toggle('coincide', partes.length > 0 && partes.some(function (p) { return st.indexOf(p) >= 0; }));
            });
        });
        var etiqueta = document.getElementById('egm-hub-conteo');
        if (etiqueta) etiqueta.textContent = visibles === 1 ? 'Mostrando 1 módulo' : 'Mostrando ' + visibles + ' módulos';
        var vacio = document.getElementById('egm-hub-vacio');
        if (vacio) vacio.hidden = visibles > 0;
    }
    if (hubFiltro) {
        hubFiltro.addEventListener('input', filtrarHub);
        hubFiltro.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                var primero = buscar(hubFiltro.value)[0];
                if (primero && hubFiltro.value.trim()) { e.preventDefault(); window.location.href = primero.u; }
            }
        });
    }
    document.querySelectorAll('[data-egm-filtro]').forEach(function (b) {
        b.addEventListener('click', function () {
            categoria = b.getAttribute('data-egm-filtro');
            document.querySelectorAll('[data-egm-filtro]').forEach(function (x) { x.classList.toggle('activo', x === b); });
            filtrarHub();
        });
    });
    if (tarjetas.length) filtrarHub();

    var contenedorFrecuentes = document.getElementById('egm-frecuentes');
    if (contenedorFrecuentes) {
        var lista = masUsadas(6);
        contenedorFrecuentes.innerHTML = lista.map(function (d) {
            return '<a href="' + escapar(d.u) + '" class="egm-chip"><i class="fas ' + escapar(d.i) + '" style="color:' + escapar(d.c) + ';"></i>' + escapar(d.t) + '</a>';
        }).join('');
        var bloque = document.getElementById('egm-frecuentes-bloque');
        if (bloque) bloque.hidden = lista.length === 0;
    }
})();
