(function () {
    function ensureCustomerOverlay() {
        var overlay = document.getElementById('customerProcessingOverlay');

        if (overlay) {
            return overlay;
        }

        overlay = document.createElement('div');
        overlay.id = 'customerProcessingOverlay';
        overlay.className = 'customer-processing-overlay';
        overlay.innerHTML =
            '<div class="customer-processing-box">' +
            '<i class="fas fa-circle-notch fa-spin"></i>' +
            '<strong id="customerProcessingText">Đang xử lý...</strong>' +
            '<span>Vui lòng không tắt trang hoặc bấm lại nhiều lần.</span>' +
            '</div>';

        document.body.appendChild(overlay);
        return overlay;
    }

    window.senvieShowCustomerLoading = function (message) {
        var overlay = ensureCustomerOverlay();
        var text = document.getElementById('customerProcessingText');

        if (text) {
            text.textContent = message || 'Đang xử lý...';
        }

        overlay.classList.add('show');
    };

    window.senvieHideCustomerLoading = function () {
        var overlay = document.getElementById('customerProcessingOverlay');

        if (overlay) {
            overlay.classList.remove('show');
        }
    };

    window.senvieDisableFormButtons = function (form) {
        if (!form) {
            return;
        }

        var buttons = form.querySelectorAll('button, input[type="submit"]');

        buttons.forEach(function (button) {
            button.dataset.originalText = button.innerHTML || button.value || '';
            button.disabled = true;

            if (button.tagName.toLowerCase() === 'button') {
                button.innerHTML = '<i class="fas fa-circle-notch fa-spin"></i> Đang xử lý...';
            }
        });
    };

    window.senvieSubmitOnce = function (form, loadingMessage) {
        if (!form) {
            return true;
        }

        if (form.dataset.submitted === 'true') {
            return false;
        }

        if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
            return true;
        }

        form.dataset.submitted = 'true';
        window.senvieDisableFormButtons(form);
        window.senvieShowCustomerLoading(loadingMessage || 'Đang xử lý yêu cầu...');

        return true;
    };

    window.senvieConfirmSubmit = function (form, confirmMessage, loadingMessage) {
        if (!form) {
            return true;
        }

        if (form.dataset.submitted === 'true') {
            return false;
        }

        if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
            return true;
        }

        var ok = confirm(confirmMessage || 'Bạn có chắc chắn muốn thực hiện thao tác này không?');

        if (!ok) {
            return false;
        }

        form.dataset.submitted = 'true';
        window.senvieDisableFormButtons(form);
        window.senvieShowCustomerLoading(loadingMessage || 'Đang xử lý yêu cầu...');

        return true;
    };

    document.addEventListener('submit', function (event) {
        if (event.defaultPrevented) {
            return;
        }
        var form = event.target;
        if (!form || !form.matches('form.js-customer-submit-once')) {
            return;
        }

        if (form.dataset.submitted === 'true') {
            event.preventDefault();
            return false;
        }

        if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
            return true;
        }

        form.dataset.submitted = 'true';
        window.senvieDisableFormButtons(form);
        window.senvieShowCustomerLoading(form.dataset.loadingText || 'Đang xử lý yêu cầu...');
    });
})();