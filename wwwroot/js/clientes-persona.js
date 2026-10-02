/*
 * Alta de cliente (Catálogos > Clientes > Nuevo): muestra la identificación de la persona solo para un cliente natural,
 * deja la razón social para cuando no hay documento (con documento sale del nombre de la persona) y aplica las
 * máscaras de nombre y documento. Es solo ayuda: el servidor (Services/Personas/VinculoService) valida todo.
 */
(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState !== 'loading') fn();
        else document.addEventListener('DOMContentLoaded', fn);
    }

    ready(function () {
        var form = document.getElementById('form-cliente');
        if (!form) return;

        function campo(nombre) { return form.querySelector('[name="' + nombre + '"]'); }
        function soloDigitos(texto) { return (texto || '').replace(/\D/g, ''); }

        var tipo = campo('Cliente.Tipo');
        var razonSocial = campo('Cliente.RazonSocial');
        var tipoDoc = campo('Persona.TipoDocumento');
        var documento = campo('Persona.Documento');
        var seccion = document.getElementById('seccion-persona');
        var ayuda = document.getElementById('ayuda-razon-social');

        function formatearDni(texto) {
            var d = soloDigitos(texto).slice(0, 13);
            return [d.slice(0, 4), d.slice(4, 8), d.slice(8)].filter(Boolean).join('-');
        }

        function aplicarMascara(el) {
            var clase = el.getAttribute('data-mascara');
            var actual = el.value;
            var nuevo = actual;

            if (clase === 'nombre') {
                nuevo = actual.replace(/[^\p{L}' \-’]/gu, '');
            } else if (clase === 'documento') {
                nuevo = tipoDoc && tipoDoc.value === 'DNI'
                    ? formatearDni(actual)
                    : actual.toUpperCase().replace(/[^A-Z0-9\-. ]/g, '');
            }
            if (nuevo !== actual) el.value = nuevo;
        }

        form.querySelectorAll('[data-mascara]').forEach(function (el) {
            el.addEventListener('input', function () { aplicarMascara(el); });
            aplicarMascara(el);
        });

        // Con documento, la razón social la pone el servidor: el campo se desactiva (no se envía ni se valida).
        function actualizar() {
            var natural = !tipo || tipo.value === 'natural';
            if (seccion) seccion.classList.toggle('d-none', !natural);

            var conFicha = natural && documento && documento.value.trim().length > 0;
            if (razonSocial) razonSocial.disabled = !!conFicha;
            if (ayuda) ayuda.classList.toggle('d-none', !conFicha);
        }

        if (tipo) tipo.addEventListener('change', actualizar);
        if (documento) documento.addEventListener('input', actualizar);
        if (tipoDoc) {
            tipoDoc.addEventListener('change', function () {
                if (documento) aplicarMascara(documento);
            });
        }
        actualizar();
    });
})();
