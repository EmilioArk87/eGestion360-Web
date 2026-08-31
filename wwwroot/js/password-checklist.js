// Checklist de contraseña en vivo.
//
// Muestra las reglas pendientes desde que se abre el formulario y va quitando
// cada una en cuanto se cumple, mientras se escribe. Es ayuda visual: el
// servidor vuelve a validar todo con Services/PasswordPolicy.cs, que además es
// de donde salen los datos de reglas (mínimo, términos, lista de comunes).
(function () {
    'use strict';

    // Deja el texto comparable: minúsculas y sin acentos, pero conserva dígitos
    // y símbolos para poder detectar secuencias como 12345678.
    function normalizar(valor) {
        return valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
    }

    // Deshace las sustituciones habituales para que "P@ssw0rd" se compare como
    // "password" contra la lista de comunes.
    var sustituciones = {
        '@': 'a', '4': 'a', '3': 'e', '1': 'i', '!': 'i', '|': 'i',
        '0': 'o', '$': 's', '5': 's', '7': 't'
    };

    function deshacerSustituciones(normalizada) {
        var salida = '';
        for (var i = 0; i < normalizada.length; i++) {
            var c = normalizada[i];
            salida += Object.prototype.hasOwnProperty.call(sustituciones, c) ? sustituciones[c] : c;
        }
        return salida;
    }

    function bytesUtf8(valor) {
        return new TextEncoder().encode(valor).length;
    }

    function esSecuencia(valor) {
        if (valor.length < 2) return false;
        var sube = true, baja = true;
        for (var i = 1; i < valor.length; i++) {
            var salto = valor.charCodeAt(i) - valor.charCodeAt(i - 1);
            if (salto !== 1) sube = false;
            if (salto !== -1) baja = false;
        }
        return sube || baja;
    }

    function unSoloCaracter(valor) {
        for (var i = 1; i < valor.length; i++) {
            if (valor[i] !== valor[0]) return false;
        }
        return valor.length > 0;
    }

    // Del usuario o el email saca lo que no debe aparecer dentro de la contraseña.
    // Ignora términos de menos de 4 caracteres para no rechazar por casualidad.
    function terminoContextual(dato) {
        if (!dato) return null;
        var termino = dato.trim();
        var arroba = termino.indexOf('@');
        if (arroba > 0) termino = termino.substring(0, arroba);
        termino = normalizar(termino);
        return termino.length >= 4 ? termino : null;
    }

    function construirReglas(config, camposContexto) {
        var comunes = {};
        (config.comunes || []).forEach(function (c) { comunes[c] = true; });

        var reglas = [
            {
                id: 'largo',
                texto: 'Al menos ' + config.largoMinimo + ' caracteres',
                cumple: function (pass) { return pass.length >= config.largoMinimo; }
            },
            {
                id: 'maximo',
                texto: 'Como máximo ' + config.largoMaximoBytes + ' caracteres',
                cumple: function (pass) { return bytesUtf8(pass) <= config.largoMaximoBytes; }
            },
            {
                id: 'comun',
                texto: 'Que no sea una contraseña de las más usadas',
                cumple: function (pass, norm, sinSust) {
                    return !comunes[norm] && !comunes[sinSust];
                }
            },
            {
                id: 'patron',
                texto: 'Sin secuencias (12345678) ni un solo carácter repetido',
                cumple: function (pass, norm) {
                    return !esSecuencia(norm) && !unSoloCaracter(norm);
                }
            },
            {
                id: 'sistema',
                texto: 'Que no contenga el nombre del sistema ni de la empresa',
                cumple: function (pass, norm, sinSust) {
                    return !(config.terminosDelSistema || []).some(function (t) {
                        return norm.indexOf(t) !== -1 || sinSust.indexOf(t) !== -1;
                    });
                }
            }
        ];

        if (camposContexto.length > 0) {
            reglas.push({
                id: 'contexto',
                texto: 'Que no contenga tu usuario ni tu email',
                cumple: function (pass, norm, sinSust) {
                    return !camposContexto.some(function (campo) {
                        var termino = terminoContextual(campo.value);
                        return termino !== null &&
                            (norm.indexOf(termino) !== -1 || sinSust.indexOf(termino) !== -1);
                    });
                }
            });
        }

        return reglas;
    }

    function iniciar(contenedor) {
        var config = JSON.parse(contenedor.getAttribute('data-reglas'));
        var input = document.getElementById(contenedor.getAttribute('data-password'));
        if (!input) return;

        var idConfirmacion = contenedor.getAttribute('data-confirmacion');
        var confirmacion = idConfirmacion ? document.getElementById(idConfirmacion) : null;

        var camposContexto = (contenedor.getAttribute('data-contexto') || '')
            .split(',')
            .map(function (id) { return document.getElementById(id.trim()); })
            .filter(function (el) { return el !== null; });

        var reglas = construirReglas(config, camposContexto);

        if (confirmacion) {
            reglas.push({
                id: 'coincide',
                texto: 'Que la confirmación sea igual',
                cumple: function (pass) {
                    return confirmacion.value.length > 0 && confirmacion.value === pass;
                }
            });
        }

        var lista = document.createElement('ul');
        lista.className = 'password-checklist';

        var elementos = {};
        reglas.forEach(function (regla) {
            var li = document.createElement('li');
            li.className = 'password-checklist__item';
            li.innerHTML = '<i class="fas fa-circle-notch" aria-hidden="true"></i><span></span>';
            li.querySelector('span').textContent = regla.texto;
            lista.appendChild(li);
            elementos[regla.id] = li;
        });

        var listo = document.createElement('p');
        listo.className = 'password-checklist__listo';
        listo.innerHTML = '<i class="fas fa-circle-check" aria-hidden="true"></i> La contraseña cumple todo.';

        contenedor.appendChild(lista);
        contenedor.appendChild(listo);

        // Región viva: quien use lector de pantalla escucha lo que va faltando
        // sin tener que salir del campo.
        contenedor.setAttribute('role', 'status');
        contenedor.setAttribute('aria-live', 'polite');

        function repasar() {
            var pass = input.value;
            var norm = normalizar(pass);
            var sinSust = deshacerSustituciones(norm);
            var pendientes = 0;

            reglas.forEach(function (regla) {
                // Con el campo vacío todo se muestra pendiente: es el estado inicial
                // que pide ver qué hace falta antes de empezar a escribir.
                var cumple = pass.length > 0 && regla.cumple(pass, norm, sinSust);
                elementos[regla.id].classList.toggle('password-checklist__item--cumplida', cumple);
                if (!cumple) pendientes++;
            });

            contenedor.classList.toggle('password-checklist--completo', pendientes === 0);
        }

        // 'change' además de 'input' porque el autocompletado del navegador y los
        // gestores de contraseñas rellenan el campo sin emitir 'input'.
        ['input', 'change'].forEach(function (evento) {
            input.addEventListener(evento, repasar);
            if (confirmacion) confirmacion.addEventListener(evento, repasar);
            camposContexto.forEach(function (campo) { campo.addEventListener(evento, repasar); });
        });

        repasar();

        // El autorrelleno suele llegar después de DOMContentLoaded.
        setTimeout(repasar, 500);
    }

    document.addEventListener('DOMContentLoaded', function () {
        var contenedores = document.querySelectorAll('[data-password-checklist]');
        Array.prototype.forEach.call(contenedores, iniciar);
    });
})();
