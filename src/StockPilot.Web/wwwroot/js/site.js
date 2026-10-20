(function () {
    'use strict';

    const money = (value) => 'Rs ' + Number(value || 0).toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    document.addEventListener('DOMContentLoaded', function () {
        initConfirmations();
        initLineForms();
        initProductSummary();
        initTooltips();
        initAlerts();
        initSubmitLock();
    });

    function initConfirmations() {
        document.querySelectorAll('form[data-confirm]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                if (form.dataset.confirmed === 'true') {
                    return;
                }
                const message = form.getAttribute('data-confirm') || 'Are you sure?';
                if (!window.confirm(message)) {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    return;
                }
                form.dataset.confirmed = 'true';
            });
        });
    }

    function initSubmitLock() {
        document.querySelectorAll('form').forEach(function (form) {
            form.addEventListener('submit', function () {
                if (form.dataset.confirmed === 'false') {
                    return;
                }
                const button = form.querySelector('button[type="submit"]:not([formnovalidate])');
                if (!button) {
                    return;
                }
                const label = button.getAttribute('data-loading-label');
                if (label) {
                    button.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>' + label;
                }
                button.disabled = true;
                form.classList.add('is-submitting');
            });
        });
    }

    function initTooltips() {
        if (!window.bootstrap || !window.bootstrap.Tooltip) {
            return;
        }
        document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (element) {
            new window.bootstrap.Tooltip(element);
        });
    }

    function initAlerts() {
        document.querySelectorAll('.app-alert').forEach(function (alert) {
            window.setTimeout(function () {
                if (!window.bootstrap || !document.body.contains(alert)) {
                    return;
                }
                const instance = window.bootstrap.Alert.getOrCreateInstance(alert);
                instance.close();
            }, 10000);
        });
    }

    function initLineForms() {
        document.querySelectorAll('[data-lines-form]').forEach(function (form) {
            const body = form.querySelector('[data-lines-body]');
            const template = form.querySelector('[data-line-template]');
            const addButton = form.querySelector('[data-add-line]');
            const kind = form.getAttribute('data-lines-form');
            if (!body || !template || !addButton) {
                return;
            }

            function reindex() {
                const rows = body.querySelectorAll('.line-row');
                rows.forEach(function (row, index) {
                    row.querySelectorAll('[name]').forEach(function (input) {
                        input.name = input.name.replace(/Items\[\d+\]/, 'Items[' + index + ']');
                    });
                    row.querySelectorAll('[id]').forEach(function (input) {
                        input.id = input.id.replace(/Items_\d+__/, 'Items_' + index + '__');
                    });
                    row.querySelectorAll('label[for]').forEach(function (label) {
                        label.htmlFor = label.htmlFor.replace(/Items_\d+__/, 'Items_' + index + '__');
                    });
                    row.querySelectorAll('[data-remove-line]').forEach(function (button) {
                        button.disabled = rows.length <= 1;
                    });
                });
            }

            function recalculate() {
                let orderTotal = 0;
                body.querySelectorAll('.line-row').forEach(function (row) {
                    const quantity = parseFloat(row.querySelector('[data-quantity-input]')?.value || '0') || 0;
                    const price = parseFloat(row.querySelector('[data-price-input]')?.value || '0') || 0;
                    const lineTotal = Math.round((quantity * price + Number.EPSILON) * 100) / 100;
                    orderTotal += lineTotal;
                    const cell = row.querySelector('[data-line-total]');
                    if (cell) {
                        cell.textContent = money(lineTotal);
                    }

                    const select = row.querySelector('[data-product-select]');
                    const stockHint = row.querySelector('[data-stock-hint]');
                    if (select && stockHint && select.selectedOptions.length && select.value) {
                        const option = select.selectedOptions[0];
                        const stock = parseFloat(option.dataset.stock || '0');
                        const unit = option.dataset.unit || '';
                        const low = quantity > stock;
                        stockHint.textContent = 'Available: ' + stock.toLocaleString('en-US', { maximumFractionDigits: 3 }) + ' ' + unit;
                        stockHint.classList.toggle('text-danger', low);
                        if (low) {
                            stockHint.textContent += ' — insufficient for this quantity';
                        }
                    }
                });

                const totalTarget = form.querySelector('[data-order-total]');
                if (totalTarget) {
                    totalTarget.textContent = money(orderTotal);
                }
            }

            function bindRow(row) {
                row.querySelectorAll('[data-quantity-input], [data-price-input]').forEach(function (input) {
                    input.addEventListener('input', recalculate);
                });

                const select = row.querySelector('[data-product-select]');
                if (select) {
                    select.addEventListener('change', function () {
                        const option = select.selectedOptions[0];
                        if (!option) {
                            return;
                        }
                        const priceInput = row.querySelector('[data-price-input]');
                        if (priceInput) {
                            const value = kind === 'purchase' ? option.dataset.cost : option.dataset.price;
                            if (value !== undefined) {
                                priceInput.value = Number(value).toFixed(2);
                            }
                        }
                        const stockHint = row.querySelector('[data-stock-hint]');
                        if (stockHint) {
                            stockHint.textContent = 'Available: ' +
                                Number(option.dataset.stock || 0).toLocaleString('en-US', { maximumFractionDigits: 3 }) +
                                ' ' + (option.dataset.unit || '');
                            stockHint.classList.remove('text-danger');
                        }
                        recalculate();
                    });
                }
            }

            function addRow() {
                const index = body.querySelectorAll('.line-row').length;
                const html = template.innerHTML.replace(/__index__/g, index);
                const wrapper = document.createElement('tbody');
                wrapper.innerHTML = html.trim();
                const row = wrapper.querySelector('.line-row');
                if (!row) {
                    return;
                }
                body.appendChild(row);
                bindRow(row);
                reindex();
                recalculate();
                row.querySelector('select')?.focus();
            }

            body.querySelectorAll('.line-row').forEach(bindRow);

            addButton.addEventListener('click', addRow);

            form.addEventListener('click', function (event) {
                const remove = event.target.closest('[data-remove-line]');
                if (!remove) {
                    return;
                }
                const row = remove.closest('.line-row');
                if (row && body.querySelectorAll('.line-row').length > 1) {
                    row.remove();
                    reindex();
                    recalculate();
                }
            });

            reindex();
            recalculate();
        });
    }

    function initProductSummary() {
        document.querySelectorAll('[data-product-summary]').forEach(function (select) {
            const balance = document.querySelector('[data-balance-target]');
            const unit = document.querySelector('[data-unit-target]');
            const cost = document.querySelector('[data-cost-target]');

            function update() {
                const option = select.selectedOptions[0];
                if (!option) {
                    return;
                }
                if (balance) {
                    balance.textContent = Number(option.dataset.stock || 0).toLocaleString('en-US', { maximumFractionDigits: 3 });
                }
                if (unit) {
                    unit.textContent = option.dataset.unit || '';
                }
                if (cost && option.dataset.cost) {
                    cost.placeholder = 'Current cost: Rs ' + Number(option.dataset.cost).toFixed(2);
                }
            }

            select.addEventListener('change', update);
            update();
        });
    }
})();
