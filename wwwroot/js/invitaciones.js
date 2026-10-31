// Invitar por correo: la lista de roles sigue a la empresa elegida y desaparece cuando se invita a un administrador de empresa
// (que tiene todos los permisos y no lleva rol). Los nombres de los roles vienen de la base: se arman con textContent, nunca con HTML.
(function () {
    'use strict';

    const empresa = document.getElementById('selectEmpresa');
    const tipo = document.getElementById('selectTipo');
    const bloqueRol = document.getElementById('bloqueRol');
    const rol = document.getElementById('selectRol');
    if (!empresa || !tipo || !bloqueRol || !rol) return;

    function mostrarRol() {
        const esAdministrador = tipo.value === 'empresa_admin';
        bloqueRol.hidden = esAdministrador;
        if (esAdministrador) rol.value = '';
    }

    async function cargarRoles() {
        const id = empresa.value;
        const elegido = rol.value;
        rol.replaceChildren();
        const primero = document.createElement('option');
        primero.value = '';
        primero.textContent = '— Elige un rol —';
        rol.appendChild(primero);
        if (!id || !empresa.dataset.url) return;

        try {
            const url = empresa.dataset.url + (empresa.dataset.url.includes('?') ? '&' : '?') + 'idEmpresa=' + encodeURIComponent(id);
            const r = await fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
            if (!r.ok) throw new Error(String(r.status));
            const roles = await r.json();
            roles.forEach(function (x) {
                const o = document.createElement('option');
                o.value = x.id;
                o.textContent = x.nombre;
                if (String(x.id) === elegido) o.selected = true;
                rol.appendChild(o);
            });
        } catch (e) {
            primero.textContent = 'No se pudieron cargar los roles';
        }
    }

    tipo.addEventListener('change', mostrarRol);
    // Para el administrador general la empresa es una lista; para un administrador de empresa es un campo oculto que no cambia.
    if (empresa.tagName === 'SELECT') empresa.addEventListener('change', cargarRoles);
    mostrarRol();
})();
