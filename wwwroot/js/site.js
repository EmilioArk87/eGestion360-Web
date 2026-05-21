// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    'use strict';

    // Reusable client-side table pagination.
    // Usage: add class "paginated-table" to a <table>. Optionally set
    //   data-page-size="15"               -> rows per page (default 15)
    //   data-pager-target="#some-id"      -> element where the pager UI will render
    // If data-pager-target is omitted, the pager is inserted right after the table.
    // Rows whose only cell uses colspan (empty-state placeholder) are ignored.
    function initPaginatedTables() {
        var tables = document.querySelectorAll('table.paginated-table');
        tables.forEach(function (table) {
            var pageSize = parseInt(table.getAttribute('data-page-size'), 10);
            if (isNaN(pageSize) || pageSize < 1) pageSize = 15;

            var tbody = table.tBodies && table.tBodies[0];
            if (!tbody) return;

            var allRows = Array.prototype.slice.call(tbody.rows);
            // Filter out empty-state rows (single cell with colspan) and total/summary rows
            var dataRows = allRows.filter(function (r) {
                if (r.cells.length === 1 && r.cells[0].hasAttribute('colspan')) return false;
                if (r.classList.contains('no-paginate')) return false;
                return true;
            });

            if (dataRows.length <= pageSize) return; // No pager needed

            var pagerTargetSel = table.getAttribute('data-pager-target');
            var pagerHost = pagerTargetSel ? document.querySelector(pagerTargetSel) : null;
            if (!pagerHost) {
                pagerHost = document.createElement('div');
                pagerHost.className = 'paginated-table-pager mt-3';
                table.parentNode.insertBefore(pagerHost, table.nextSibling);
            }

            var state = { page: 1, totalPages: Math.ceil(dataRows.length / pageSize) };

            function render() {
                dataRows.forEach(function (row, idx) {
                    var page = Math.floor(idx / pageSize) + 1;
                    row.style.display = (page === state.page) ? '' : 'none';
                });
                renderPager();
            }

            function renderPager() {
                pagerHost.innerHTML = '';
                var nav = document.createElement('nav');
                nav.setAttribute('aria-label', 'Paginación de tabla');
                var ul = document.createElement('ul');
                ul.className = 'pagination pagination-sm justify-content-end mb-0';

                function addItem(label, page, opts) {
                    opts = opts || {};
                    var li = document.createElement('li');
                    li.className = 'page-item' + (opts.disabled ? ' disabled' : '') + (opts.active ? ' active' : '');
                    var a = document.createElement('a');
                    a.className = 'page-link';
                    a.href = '#';
                    a.innerHTML = label;
                    a.addEventListener('click', function (e) {
                        e.preventDefault();
                        if (opts.disabled || opts.active) return;
                        state.page = page;
                        render();
                    });
                    li.appendChild(a);
                    ul.appendChild(li);
                }

                addItem('&laquo;', Math.max(1, state.page - 1), { disabled: state.page === 1 });

                var windowSize = 5;
                var start = Math.max(1, state.page - Math.floor(windowSize / 2));
                var end = Math.min(state.totalPages, start + windowSize - 1);
                start = Math.max(1, end - windowSize + 1);

                if (start > 1) {
                    addItem('1', 1, {});
                    if (start > 2) addItem('&hellip;', 0, { disabled: true });
                }
                for (var p = start; p <= end; p++) {
                    addItem(String(p), p, { active: p === state.page });
                }
                if (end < state.totalPages) {
                    if (end < state.totalPages - 1) addItem('&hellip;', 0, { disabled: true });
                    addItem(String(state.totalPages), state.totalPages, {});
                }

                addItem('&raquo;', Math.min(state.totalPages, state.page + 1), { disabled: state.page === state.totalPages });

                nav.appendChild(ul);

                var info = document.createElement('div');
                info.className = 'text-muted small me-auto';
                var from = (state.page - 1) * pageSize + 1;
                var to = Math.min(state.page * pageSize, dataRows.length);
                info.textContent = 'Mostrando ' + from + '-' + to + ' de ' + dataRows.length;

                var wrapper = document.createElement('div');
                wrapper.className = 'd-flex align-items-center flex-wrap gap-2';
                wrapper.appendChild(info);
                wrapper.appendChild(nav);
                pagerHost.appendChild(wrapper);
            }

            render();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initPaginatedTables);
    } else {
        initPaginatedTables();
    }
})();
