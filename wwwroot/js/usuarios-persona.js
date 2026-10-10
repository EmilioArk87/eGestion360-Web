// Crear usuario: elegir la persona que usará la cuenta (script 021).
// Alterna entre "Ya está registrada" (búsqueda) y "Crearla" (datos mínimos), y arma los resultados con métodos del
// DOM (textContent), nunca con HTML: los nombres vienen de la base.
(function () {
    'use strict';

    const fieldset = document.getElementById('persona');
    if (!fieldset) return;

    const radios = fieldset.querySelectorAll('input[name="ModoPersona"]');
    const bloques = fieldset.querySelectorAll('[data-modo]');
    const caja = document.getElementById('textoPersona');
    const boton = document.getElementById('btnBuscarPersona');
    const lista = document.getElementById('resultadosPersona');
    const elegida = document.getElementById('IdPersonaElegida');
    const avisoElegida = document.getElementById('personaElegida');
    const nombreElegida = document.getElementById('nombrePersonaElegida');

    function modoActual() {
        const marcado = fieldset.querySelector('input[name="ModoPersona"]:checked');
        return marcado ? marcado.value : 'existente';
    }

    function mostrarModo() {
        const modo = modoActual();
        bloques.forEach(function (b) { b.hidden = b.dataset.modo !== modo; });
    }

    function mensaje(texto) {
        lista.replaceChildren();
        const li = document.createElement('li');
        li.className = 'list-group-item text-muted small';
        li.textContent = texto;
        lista.appendChild(li);
    }

    function elegir(persona) {
        elegida.value = persona.id;
        nombreElegida.textContent = persona.nombre;
        avisoElegida.classList.remove('d-none');
        lista.replaceChildren();
    }

    async function buscar() {
        const texto = caja.value.trim();
        if (texto.length < 2) { mensaje('Escribe al menos 2 caracteres.'); return; }

        let personas;
        try {
            const url = caja.dataset.url + (caja.dataset.url.includes('?') ? '&' : '?') + 'texto=' + encodeURIComponent(texto);
            const r = await fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
            if (!r.ok) throw new Error(String(r.status));
            personas = await r.json();
        } catch (e) {
            mensaje('No se pudo buscar. Intenta de nuevo.');
            return;
        }

        if (!personas.length) { mensaje('No se encontró a nadie. Puedes elegir «Crearla».'); return; }

        lista.replaceChildren();
        personas.forEach(function (p) {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center gap-2';

            const datos = document.createElement('div');
            const nombre = document.createElement('div');
            nombre.className = 'fw-semibold';
            nombre.textContent = p.nombre;
            const detalle = document.createElement('div');
            detalle.className = 'text-muted small';
            const partes = [p.documento || 'Sin documento'];
            if (p.empresas && p.empresas.length) partes.push(p.empresas.join(', '));
            if (p.usuarios && p.usuarios.length) partes.push('Usuarios: ' + p.usuarios.join(', '));
            detalle.textContent = partes.join(' · ');
            datos.append(nombre, detalle);

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-sm btn-primary';
            btn.textContent = 'Elegir';
            btn.addEventListener('click', function () { elegir(p); });

            li.append(datos, btn);
            lista.appendChild(li);
        });
    }

    radios.forEach(function (r) { r.addEventListener('change', mostrarModo); });
    boton.addEventListener('click', buscar);
    caja.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); buscar(); }
    });

    mostrarModo();
})();
