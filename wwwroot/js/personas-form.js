/*
 * Formulario de Personas (Nuevo y Editar): máscaras, listas en cascada, edad, sección de conductor y validación
 * en el navegador. Las reglas esenciales son las mismas del servidor (Services/Personas/PersonaValidacionService),
 * que es el que manda: esto solo avisa antes de enviar.
 */
(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState !== 'loading') fn();
        else document.addEventListener('DOMContentLoaded', fn);
    }

    ready(function () {
        var form = document.getElementById('form-persona');
        if (!form) return;

        var config = leerJson('persona-config') || {};
        var municipios = leerJson('persona-municipios') || [];

        function leerJson(id) {
            var el = document.getElementById(id);
            try { return el ? JSON.parse(el.textContent) : null; } catch (e) { return null; }
        }

        function campo(nombre) { return form.querySelector('[name="' + nombre + '"]'); }
        function valor(nombre) { var el = campo(nombre); return el ? el.value : ''; }
        function limpiar(texto) { return (texto || '').replace(/\s+/g, ' ').trim(); }
        function soloDigitos(texto) { return (texto || '').replace(/\D/g, ''); }

        var N = {
            tipoDoc: 'Datos.TipoDocumento', doc: 'Datos.Documento',
            pNombre: 'Datos.PrimerNombre', sNombre: 'Datos.SegundoNombre',
            pApellido: 'Datos.PrimerApellido', sApellido: 'Datos.SegundoApellido',
            nacimiento: 'Datos.FechaNacimiento',
            tel: 'Datos.Telefono', tel2: 'Datos.TelefonoSecundario', email: 'Datos.Email',
            emNombre: 'Datos.ContactoEmergenciaNombre', emTel: 'Datos.ContactoEmergenciaTelefono', emParentesco: 'Datos.ContactoEmergenciaParentesco',
            licTipo: 'Datos.LicenciaTipo', licNumero: 'Datos.LicenciaNumero', licVence: 'Datos.LicenciaVencimiento',
            cargo: 'Datos.Empleado.Cargo', codigo: 'Datos.Empleado.CodigoInterno',
            ingreso: 'Datos.Empleado.FechaIngreso', baja: 'Datos.Empleado.FechaBaja', tarifa: 'Datos.Empleado.TarifaDiaria'
        };

        // ── Máscaras ────────────────────────────────────────────────────────

        function formatearDni(texto) {
            var d = soloDigitos(texto).slice(0, 13);
            return [d.slice(0, 4), d.slice(4, 8), d.slice(8)].filter(Boolean).join('-');
        }

        function formatearTelefono(texto) {
            var d = soloDigitos(texto);
            if (d.indexOf('504') === 0 && d.length > 8) d = d.slice(3);   // acepta el prefijo +504
            d = d.slice(0, 8);
            return d.length > 4 ? d.slice(0, 4) + '-' + d.slice(4) : d;
        }

        function aplicarMascara(el) {
            var tipo = el.getAttribute('data-mascara');
            var actual = el.value;
            var nuevo = actual;

            if (tipo === 'nombre') nuevo = actual.replace(/[^\p{L}' \-’]/gu, '');
            else if (tipo === 'telefono') nuevo = formatearTelefono(actual);
            else if (tipo === 'codigo') nuevo = actual.replace(/[^A-Za-z0-9]/g, '');
            else if (tipo === 'licencia') nuevo = actual.toUpperCase().replace(/[^A-Z0-9\-]/g, '');
            else if (tipo === 'documento') {
                nuevo = valor(N.tipoDoc) === 'DNI' ? formatearDni(actual) : actual.toUpperCase().replace(/[^A-Z0-9\-. ]/g, '');
            }
            if (nuevo !== actual) el.value = nuevo;
        }

        form.querySelectorAll('[data-mascara]').forEach(function (el) {
            el.addEventListener('input', function () { aplicarMascara(el); });
            aplicarMascara(el);
        });

        var tipoDoc = campo(N.tipoDoc);
        if (tipoDoc) {
            tipoDoc.addEventListener('change', function () {
                var doc = campo(N.doc);
                if (doc) aplicarMascara(doc);
            });
        }

        // ── Departamento → municipio ────────────────────────────────────────

        function llenarMunicipios(destino, idDepartamento) {
            destino.innerHTML = '';
            var vacio = document.createElement('option');
            vacio.value = '';
            vacio.textContent = '—';
            destino.appendChild(vacio);
            municipios
                .filter(function (m) { return String(m.d) === String(idDepartamento); })
                .sort(function (a, b) { return a.n.localeCompare(b.n, 'es'); })
                .forEach(function (m) {
                    var opcion = document.createElement('option');
                    opcion.value = m.id;
                    opcion.textContent = m.n;
                    destino.appendChild(opcion);
                });
        }

        form.querySelectorAll('[data-cascada]').forEach(function (origen) {
            var destino = document.getElementById(origen.getAttribute('data-cascada'));
            if (!destino) return;
            origen.addEventListener('change', function () { llenarMunicipios(destino, origen.value); });
        });

        // ── Edad y sección de conductor ─────────────────────────────────────

        function partesFecha(iso) {
            var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso || '');
            return m ? { a: +m[1], m: +m[2], d: +m[3] } : null;
        }

        function edadEn(nacimiento, hoy) {
            var edad = hoy.a - nacimiento.a;
            if (hoy.m < nacimiento.m || (hoy.m === nacimiento.m && hoy.d < nacimiento.d)) edad--;
            return edad;
        }

        function esConductor() { return valor(N.cargo) === config.codigoConductor; }

        function actualizarEdad() {
            var salida = document.getElementById('edad-calculada');
            if (!salida) return;
            var nac = partesFecha(valor(N.nacimiento)), hoy = partesFecha(config.hoy);
            if (!nac || !hoy) { salida.textContent = ''; return; }
            var edad = edadEn(nac, hoy);
            salida.textContent = edad >= 0 ? 'Edad: ' + edad + (edad === 1 ? ' año' : ' años') : '';
        }

        function tieneLicencia() {
            return !!(limpiar(valor(N.licNumero)) || valor(N.licTipo) || valor(N.licVence));
        }

        function actualizarConductor() {
            var seccion = document.getElementById('seccion-conductor');
            if (!seccion || seccion.hasAttribute('data-siempre-visible')) return;
            seccion.classList.toggle('d-none', !(esConductor() || tieneLicencia()));
        }

        [N.nacimiento].forEach(function (n) { var el = campo(n); if (el) el.addEventListener('change', actualizarEdad); });
        [N.cargo, N.licTipo, N.licNumero, N.licVence].forEach(function (n) {
            var el = campo(n);
            if (el) { el.addEventListener('change', actualizarConductor); el.addEventListener('input', actualizarConductor); }
        });
        actualizarEdad();
        actualizarConductor();

        // ── Validación en el navegador ──────────────────────────────────────

        function errorNombre(nombre, obligatorio, etiqueta) {
            var v = limpiar(valor(nombre));
            if (!v) return obligatorio ? 'El ' + etiqueta + ' es obligatorio.' : null;
            if (!/^[\p{L}' \-’]+$/u.test(v) || !/\p{L}/u.test(v)) return 'El ' + etiqueta + ' solo admite letras, espacios, apóstrofo y guion.';
            if (v.length > 50) return 'El ' + etiqueta + ' no puede pasar de 50 caracteres.';
            return null;
        }

        function errorTelefono(nombre, etiqueta) {
            var v = valor(nombre).trim();
            if (!v) return null;
            if (!/^[\d\s().+\-]+$/.test(v)) return etiqueta + ' debe tener 8 dígitos.';
            var d = soloDigitos(v);
            if (d.length === 11 && d.indexOf('504') === 0) d = d.slice(3);
            return d.length === 8 ? null : etiqueta + ' debe tener 8 dígitos.';
        }

        var reglas = {};

        reglas[N.pNombre] = function () { return errorNombre(N.pNombre, true, 'primer nombre'); };
        reglas[N.sNombre] = function () { return errorNombre(N.sNombre, false, 'segundo nombre'); };
        reglas[N.pApellido] = function () { return errorNombre(N.pApellido, true, 'primer apellido'); };
        reglas[N.sApellido] = function () { return errorNombre(N.sApellido, false, 'segundo apellido'); };

        reglas[N.tipoDoc] = function () {
            return limpiar(valor(N.doc)) && !valor(N.tipoDoc) ? 'Elige el tipo de documento.' : null;
        };

        reglas[N.doc] = function () {
            var tipo = valor(N.tipoDoc), numero = limpiar(valor(N.doc));
            if (!tipo && !numero) {
                var codigo = limpiar(valor(N.codigo));
                return !config.esEdicion && !codigo ? 'Escribe el documento de identidad o, mientras tanto, el código de empleado.' : null;
            }
            if (!tipo) return null;
            if (!numero) return 'Escribe el número del documento.';
            var d = soloDigitos(numero);
            if (tipo === 'DNI') {
                if (!/^[\d\- ]+$/.test(numero) || d.length !== 13) return 'El DNI debe tener 13 dígitos.';
                var depto = +d.slice(0, 2), anio = +d.slice(4, 8), hoy = partesFecha(config.hoy);
                if (depto < 1 || depto > 18) return 'El DNI no corresponde a un departamento válido.';
                if (anio < 1900 || (hoy && anio > hoy.a)) return 'El año del DNI no es válido.';
            } else if (tipo === 'RTN') {
                if (!/^[\d\- ]+$/.test(numero) || d.length !== 14) return 'El RTN debe tener 14 dígitos.';
            } else if (!/^[A-Za-z0-9\-. ]+$/.test(numero)) {
                return 'El número solo admite letras y números.';
            }
            return null;
        };

        reglas[N.nacimiento] = function () {
            var v = valor(N.nacimiento);
            if (!v) return !config.esEdicion ? 'La fecha de nacimiento es obligatoria.' : null;
            var nac = partesFecha(v), hoy = partesFecha(config.hoy);
            if (!nac) return 'La fecha de nacimiento no es válida.';
            if (v > config.hoy) return 'La fecha de nacimiento no puede ser futura.';
            if (nac.a < 1900) return 'La fecha de nacimiento no es válida.';
            var minima = esConductor() ? config.edadMinimaConductor : config.edadMinima;
            if (hoy && edadEn(nac, hoy) < minima) {
                return esConductor() ? 'El conductor debe tener al menos ' + minima + ' años.' : 'La persona debe tener al menos ' + minima + ' años.';
            }
            return null;
        };

        reglas[N.tel] = function () { return errorTelefono(N.tel, 'El teléfono'); };
        reglas[N.tel2] = function () { return errorTelefono(N.tel2, 'El teléfono secundario'); };
        reglas[N.email] = function () {
            var v = valor(N.email).trim();
            return !v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v) ? null : 'Escribe un correo válido, por ejemplo nombre@empresa.com.';
        };

        reglas[N.emNombre] = function () {
            var nombre = limpiar(valor(N.emNombre)), tel = limpiar(valor(N.emTel)), par = limpiar(valor(N.emParentesco));
            if ((nombre || tel || par) && !(nombre && tel && par)) return 'Completa nombre, teléfono y parentesco del contacto.';
            return null;
        };
        reglas[N.emTel] = function () { return errorTelefono(N.emTel, 'El teléfono del contacto'); };

        reglas[N.cargo] = function () {
            return !config.esEdicion && !valor(N.cargo) ? 'Elige un cargo definido para esta empresa.' : null;
        };
        reglas[N.codigo] = function () {
            var v = limpiar(valor(N.codigo));
            return !v || /^[A-Za-z0-9]{1,30}$/.test(v) ? null : 'El código de empleado solo admite letras y números, sin espacios, hasta 30 caracteres.';
        };
        reglas[N.baja] = function () {
            var ingreso = valor(N.ingreso), baja = valor(N.baja);
            return ingreso && baja && baja < ingreso ? 'La fecha de baja no puede ser anterior al ingreso.' : null;
        };
        reglas[N.tarifa] = function () {
            var v = valor(N.tarifa).trim();
            if (!v) return null;
            if (!/^-?\d+(\.\d+)?$/.test(v)) return 'La tarifa no es un número válido.';
            if (parseFloat(v) < 0) return 'La tarifa no puede ser negativa.';
            return /^\d+(\.\d{1,2})?$/.test(v) ? null : 'La tarifa admite como máximo 2 decimales.';
        };

        reglas[N.licTipo] = function () {
            return !config.esEdicion && esConductor() && !valor(N.licTipo) ? 'Elige la categoría de la licencia.' : null;
        };
        reglas[N.licNumero] = function () {
            var v = limpiar(valor(N.licNumero));
            if (!v) return !config.esEdicion && esConductor() ? 'Escribe el número de la licencia.' : null;
            return /^[A-Za-z0-9\-]{1,30}$/.test(v) ? null : 'El número de licencia solo admite letras, números y guiones, hasta 30 caracteres.';
        };
        reglas[N.licVence] = function () {
            var v = valor(N.licVence);
            if (!v) return !config.esEdicion && esConductor() ? 'Escribe el vencimiento de la licencia.' : null;
            return !config.esEdicion && v <= config.hoy ? 'La licencia ya venció o vence hoy; registra una licencia vigente.' : null;
        };

        function mostrarError(el, mensaje) {
            var existente = el.parentNode.querySelector('.js-error[data-para="' + el.name + '"]');
            if (!mensaje) {
                if (existente) existente.remove();
                el.classList.remove('is-invalid');
                return;
            }
            if (!existente) {
                existente = document.createElement('div');
                existente.className = 'js-error text-danger small';
                existente.setAttribute('data-para', el.name);
                el.parentNode.appendChild(existente);
            }
            existente.textContent = mensaje;
            el.classList.add('is-invalid');
        }

        function validarCampo(nombre) {
            var el = campo(nombre);
            if (!el || !reglas[nombre]) return null;
            var mensaje = reglas[nombre]();
            mostrarError(el, mensaje);
            return mensaje;
        }

        // Al salir de un campo se valida; al escribir se quita el error que ya estaba.
        Object.keys(reglas).forEach(function (nombre) {
            var el = campo(nombre);
            if (!el) return;
            el.addEventListener('blur', function () { validarCampo(nombre); });
            el.addEventListener('input', function () { if (el.classList.contains('is-invalid')) mostrarError(el, null); });
            el.addEventListener('change', function () { validarCampo(nombre); });
        });

        // Campos que dependen de otros: se revalidan juntos.
        [[N.doc, [N.tipoDoc, N.codigo]], [N.nacimiento, [N.cargo]], [N.baja, [N.ingreso]],
         [N.emNombre, [N.emTel, N.emParentesco]]].forEach(function (par) {
            par[1].forEach(function (n) {
                var el = campo(n);
                if (el) el.addEventListener('change', function () { if (campo(par[0]).value || n === N.cargo) validarCampo(par[0]); });
            });
        });

        form.addEventListener('submit', function (evento) {
            var primero = null;
            Object.keys(reglas).forEach(function (nombre) {
                if (validarCampo(nombre) && !primero) primero = campo(nombre);
            });

            var aviso = document.getElementById('js-resumen-errores');
            if (primero) {
                evento.preventDefault();
                if (!aviso) {
                    aviso = document.createElement('div');
                    aviso.id = 'js-resumen-errores';
                    aviso.className = 'alert alert-danger small';
                    aviso.textContent = 'Revisa los campos marcados en rojo.';
                    form.insertBefore(aviso, form.firstChild);
                }
                // La sección de conductor puede estar oculta si el error es suyo: se muestra.
                var seccion = primero.closest('#seccion-conductor');
                if (seccion) seccion.classList.remove('d-none');
                primero.scrollIntoView({ behavior: 'smooth', block: 'center' });
                primero.focus({ preventScroll: true });
            } else if (aviso) {
                aviso.remove();
            }
        });
    });
})();
