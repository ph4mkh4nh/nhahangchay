(function () {
    function ensureOverlay() {
        var overlay = document.getElementById("adminProcessingOverlay");

        if (!overlay) {
            overlay = document.createElement("div");
            overlay.id = "adminProcessingOverlay";
            overlay.className = "admin-processing-overlay";

            overlay.innerHTML =
                '<div class="admin-processing-box">' +
                '<i class="fas fa-circle-notch fa-spin"></i>' +
                '<strong id="adminProcessingText">Đang xử lý...</strong>' +
                '<span>Vui lòng không tắt trang hoặc bấm lại nhiều lần.</span>' +
                '</div>';

            document.body.appendChild(overlay);
        }

        return overlay;
    }

    function showOverlay(message) {
        var overlay = ensureOverlay();
        var text = document.getElementById("adminProcessingText");

        if (text) {
            text.innerText = message || "Đang xử lý...";
        }

        overlay.style.display = "flex";
        overlay.classList.add("show");
    }

    function lockSubmitButton(form) {
        var button = form.querySelector('button[type="submit"]');

        if (button) {
            button.disabled = true;
            button.innerHTML = '<i class="fas fa-circle-notch fa-spin"></i>';
        }
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;

        if (!form.classList.contains("js-admin-post")) {
            return;
        }

        if (form.dataset.submitted === "true") {
            event.preventDefault();
            return;
        }

        var confirmMessage = form.dataset.confirm || "";
        var loadingMessage = form.dataset.loading || "Đang xử lý...";

        if (confirmMessage && !confirm(confirmMessage)) {
            event.preventDefault();
            return;
        }

        event.preventDefault();

        form.dataset.submitted = "true";
        lockSubmitButton(form);
        showOverlay(loadingMessage);

        setTimeout(function () {
            HTMLFormElement.prototype.submit.call(form);
        }, 800);
    }, true);

    window.senvieShowOverlay = function (message) {
        showOverlay(message);
    };

    document.addEventListener("change", function (event) {
        var input = event.target;

        if (!input.classList.contains("js-dish-image-input")) {
            return;
        }

        var file = input.files && input.files[0];

        if (!file) {
            return;
        }

        var preview = document.querySelector(input.dataset.preview);
        var placeholder = document.querySelector(input.dataset.placeholder);

        if (preview) {
            preview.src = URL.createObjectURL(file);
            preview.style.display = "block";
        }

        if (placeholder) {
            placeholder.style.display = "none";
        }
    });
})();