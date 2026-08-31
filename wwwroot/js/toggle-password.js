// Botón de ver/ocultar contraseña, reutilizable.
//
// En el HTML basta con:
//     <button type="button" class="btn ..." data-toggle-password="IdDelInput">
//         <i class="fas fa-eye"></i>
//     </button>
//
// El icono de dentro se cambia solo entre fa-eye y fa-eye-slash.
(function () {
    'use strict';

    function alternar(boton) {
        var input = document.getElementById(boton.getAttribute('data-toggle-password'));
        if (!input) return;

        var icono = boton.querySelector('i');
        var mostrar = input.type === 'password';

        input.type = mostrar ? 'text' : 'password';

        if (icono) {
            icono.classList.toggle('fa-eye', !mostrar);
            icono.classList.toggle('fa-eye-slash', mostrar);
        }

        boton.setAttribute('aria-label', mostrar ? 'Ocultar contraseña' : 'Mostrar contraseña');

        // El cursor vuelve al final del campo: alternar el type lo manda al inicio
        // en algunos navegadores y se termina escribiendo donde no corresponde.
        var largo = input.value.length;
        input.focus();
        try {
            input.setSelectionRange(largo, largo);
        } catch (e) {
            // Los input de tipo password no siempre permiten mover la selección.
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var botones = document.querySelectorAll('[data-toggle-password]');
        Array.prototype.forEach.call(botones, function (boton) {
            if (!boton.hasAttribute('aria-label')) {
                boton.setAttribute('aria-label', 'Mostrar contraseña');
            }
            boton.addEventListener('click', function () { alternar(boton); });
        });
    });
})();
