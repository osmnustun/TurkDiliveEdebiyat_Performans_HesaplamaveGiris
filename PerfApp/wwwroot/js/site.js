(function () {
    'use strict';

    const trNumber = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 });

    function fmt(value) {
        return trNumber.format(Math.round(value * 10) / 10);
    }

    function clampScore(value) {
        let n = parseInt(value, 10);
        if (isNaN(n)) n = 0;
        return Math.max(0, Math.min(100, n));
    }

    function updateFill(range) {
        const value = clampScore(range.value);
        range.style.setProperty('--fill', value + '%');
    }

    function updateCard(card) {
        const range = card.querySelector('[data-range]');
        const number = card.querySelector('[data-number]');
        if (!range || !number) return;

        const score = clampScore(number.value);
        range.value = String(score);
        updateFill(range);

        number.classList.toggle('unset', score === 0 && number.dataset.touched !== '1');

        card.querySelectorAll('.quick-chips button').forEach(function (btn) {
            btn.classList.toggle('active', parseInt(btn.dataset.value, 10) === score);
        });

        const groupMax = parseFloat(card.dataset.max || '0');
        const earned = card.querySelector('[data-earned]');
        if (earned) earned.textContent = fmt(score / 100 * groupMax) + ' / ' + fmt(groupMax);

        card.querySelectorAll('.crit-line[data-crit-max]').forEach(function (line) {
            const max = parseFloat(line.dataset.critMax || '0');
            const value = line.querySelector('b');
            if (value) value.textContent = fmt(score / 100 * max);
        });
    }

    function updateLiveScore() {
        const live = document.querySelector('[data-live-score]');
        if (!live) return;

        let raw = 0;
        let total = 0;
        document.querySelectorAll('.score-card[data-max]').forEach(function (card) {
            const max = parseFloat(card.dataset.max || '0');
            const number = card.querySelector('[data-number]');
            const score = number ? clampScore(number.value) : 0;
            raw += score / 100 * max;
            total += max;
        });

        const pct = total > 0 ? Math.round(raw / total * 100) : 0;
        live.textContent = String(pct);
        live.classList.toggle('is-zero', pct === 0);
    }

    function bindScoreCards() {
        document.querySelectorAll('.score-card[data-max]').forEach(function (card) {
            const range = card.querySelector('[data-range]');
            const number = card.querySelector('[data-number]');
            if (!range || !number) return;

            range.addEventListener('input', function () {
                number.value = range.value;
                number.dataset.touched = '1';
                updateCard(card);
                updateLiveScore();
            });

            number.addEventListener('input', function () {
                number.dataset.touched = '1';
                updateCard(card);
                updateLiveScore();
            });

            number.addEventListener('blur', function () {
                number.value = String(clampScore(number.value));
                updateCard(card);
                updateLiveScore();
            });

            card.querySelectorAll('.quick-chips button').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    number.value = btn.dataset.value;
                    number.dataset.touched = '1';
                    updateCard(card);
                    updateLiveScore();
                    if (navigator.vibrate) navigator.vibrate(8);
                });
            });

            updateCard(card);
        });

        updateLiveScore();
    }

    function bindConfirm() {
        document.querySelectorAll('form[data-confirm]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                if (!window.confirm(form.dataset.confirm)) {
                    event.preventDefault();
                }
            });
        });
    }

    function bindToast() {
        const toast = document.getElementById('appToast');
        if (!toast) return;
        window.setTimeout(function () {
            toast.classList.add('hide');
        }, 3200);
    }

    function bindSubmitOnce() {
        document.querySelectorAll('form[data-submit-once]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                if (form.dataset.submitting === '1') {
                    event.preventDefault();
                    return;
                }
                form.dataset.submitting = '1';
                // Tıklanan düğme devre dışı bırakılmamalı; aksi halde
                // name="action" value="next" gibi değerler gönderilmez.
                form.querySelectorAll('button[type=submit]').forEach(function (btn) {
                    if (btn !== event.submitter) {
                        btn.disabled = true;
                    }
                    btn.classList.add('disabled');
                });
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        bindScoreCards();
        bindConfirm();
        bindToast();
        bindSubmitOnce();
    });
})();
