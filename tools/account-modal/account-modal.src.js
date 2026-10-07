/*!
 * mcp.dmca.com account modal: the DMCA.com plan / log in / register modal from www.dmca.com/add/
 * (Add/Assets/pro-comparison-modal.ascx + PP2020/js/status-upgrade.js), ported to run entirely on this page.
 * - Log in:   POST https://api.dmca.com/login     (CORS *)  -> DMCA API token, kept in sessionStorage only
 * - Register: POST https://api.dmca.com/register  (CORS *)  -> account created, login details emailed
 * - Pay:      in-modal Stripe Payment Element via https://www.dmca.com/StripeSubscription.ashx (Token header),
 *             confirmed with redirect:'if_required'. Works once www.dmca.com allows the https://mcp.dmca.com origin.
 * No redirects: PayPal / Crypto (they leave the page) and Google sign-in (www-only) are hidden here.
 * Built by tools/account-modal/build.py - edit the source there, not wwwroot/account-modal.js.
 */
(function (w, d) {
    'use strict';
    if (w.DMCAAccount) { return; }

    var API = 'https://api.dmca.com';
    var PE_API = 'https://www.dmca.com/StripeSubscription.ashx';
    var STRIPE_JS = 'https://js.stripe.com/v3/';
    var MCP_URL = 'https://mcp.dmca.com/runtime/webhooks/mcp';
    var SOURCE = 'mcp';
    var TOKEN_KEY = 'dmcaMcpToken', EMAIL_KEY = 'dmcaMcpEmail', PLAN_KEY = 'dmcaMcpPlan';
    var PLANS = {
        pro: { name: 'Pro', monthly: { id: 'DMCA-PPRO10', value: 10 }, annual: { id: 'DMCA-PPRO100', value: 100 } },
        business: { name: 'Business', monthly: { id: 'plan_Fa3iWOYeyUvYPl', value: 15 }, annual: { id: 'plan_FetTI5bfdaVbLD', value: 150 } }
    };
    var PRICES = { monthly: { free: 0, pro: 10, bus: 15 }, annual: { free: 0, pro: 100, bus: 150 } };
    var METHOD_LABEL = { credit: 'Credit Card' };
    var EMAIL_RE = /^[^\s@<>()[\]\\,;:"]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$/;
    var GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    var CHECKOUT_OFF_MSG = 'Card checkout isn\u2019t switched on for mcp.dmca.com yet. Your account works with the MCP server right now on the Free plan \u2014 please try upgrading again later.';

    var HTML = __MODAL_HTML__;

    var state = { sel: null };

    /* ---------- helpers ---------- */
    function $1(sel, root) { return (root || d).querySelector(sel); }
    function ssGet(k) { try { return w.sessionStorage.getItem(k) || ''; } catch (e) { return ''; } }
    function ssSet(k, v) { try { w.sessionStorage.setItem(k, String(v)); } catch (e) { } }
    function ssDel(k) { try { w.sessionStorage.removeItem(k); } catch (e) { } }
    function token() { return ssGet(TOKEN_KEY); }
    function planLevel() { var p = ssGet(PLAN_KEY); return p === '2' ? 2 : (p === '1' ? 1 : (p === '0' ? 0 : -1)); }
    function planName(l) { return l === 2 ? 'Business' : (l === 1 ? 'Pro' : (l === 0 ? 'Free' : '')); }
    function validEmail(e) { return EMAIL_RE.test(String(e || '').trim()); }
    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
    function isFree(sel) { return !!sel && sel.plan === 'free'; }
    function priceText(sel) { var c = PLANS[sel.plan][sel.cycle]; return '$' + c.value + (sel.cycle === 'annual' ? '/yr' : '/mo'); }
    function readJson(res) {
        return res.text().then(function (text) {
            var data = null;
            try { data = text ? JSON.parse(text) : null; } catch (e) { data = text; }
            // api.dmca.com wraps some JSON bodies in a second layer of string quoting.
            if (typeof data === 'string' && /^\s*[\[{]/.test(data)) { try { data = JSON.parse(data); } catch (e2) { } }
            return { ok: res.ok, status: res.status, data: data, text: text || '' };
        });
    }
    function root() { return d.getElementById('dmcaAcct'); }
    function modal() { return $1('#dmcaAcct .proComparisonModal.plan-cards-layout'); }
    function veil() { return $1('#dmcaAcct .redirectModalVeil'); }
    function step() { return $1('#dmcaAcct .plan-auth-step'); }
    function visible() { var m = modal(); return !!m && m.style.display === 'block'; }

    /* ---------- header pill (index.html span.account) ---------- */
    function syncPill() {
        var tok = token();
        d.querySelectorAll('[data-acct="login"]').forEach(function (a) { a.textContent = tok ? 'My account' : 'Log in'; });
        d.querySelectorAll('[data-acct="register"]').forEach(function (a) { a.hidden = !!tok; });
    }

    /* ---------- modal head / panels (same behaviour as status-upgrade.js) ---------- */
    var original = null;
    function rememberHead() {
        var m = modal();
        if (!m || original) { return; }
        var t = $1('h2.section__title', m), s = $1('.plan-modal-subtitle', m);
        original = { title: t ? t.textContent.trim() : '', sub: s ? s.textContent.trim() : '' };
    }
    function setHead(title, sub) {
        var m = modal();
        if (!m) { return; }
        rememberHead();
        var t = $1('h2.section__title', m), s = $1('.plan-modal-subtitle', m);
        if (t) { t.textContent = title; }
        if (s) { s.textContent = sub; }
    }
    function msg(el, text, kind) {
        if (!el) { return; }
        el.textContent = text || '';
        el.className = 'plan-auth__msg' + (kind ? ' plan-auth__msg--' + kind : '');
        el.hidden = !text;
    }
    function openStep(view) {
        var m = modal(), s = step();
        if (!m || !s) { return false; }
        rememberHead();
        m.classList.add('is-auth-step');
        s.hidden = false;
        s.setAttribute('data-view', view);
        var inner = $1('.innerModalCont', m);
        if (inner) { inner.scrollTop = 0; }
        return true;
    }
    function summaryText() {
        var sel = state.sel;
        if (!sel) { return 'checkout'; }
        if (isFree(sel)) { return 'your free account'; }
        return PLANS[sel.plan].name + ' (' + priceText(sel) + ') with ' + METHOD_LABEL.credit;
    }
    function showPanel(which) {
        var s = step();
        if (!s) { return; }
        ['register', 'login'].forEach(function (k) {
            var f = $1('.plan-auth__form--' + k, s), tab = $1('.plan-auth__tab[data-tab="' + k + '"]', s);
            if (f) { f.hidden = (k !== which); }
            if (tab) { tab.classList.toggle('is-active', k === which); tab.setAttribute('aria-selected', k === which ? 'true' : 'false'); }
        });
        var tabs = $1('.plan-auth__tabs', s), prog = $1('.plan-auth__progress', s), pay = $1('.plan-pay', s), acct = $1('.plan-acct', s);
        var sum = $1('.plan-auth__summary', s);
        if (tabs) { tabs.hidden = (which === 'progress' || which === 'pay' || which === 'account'); }
        if (prog) { prog.hidden = (which !== 'progress'); }
        if (pay) { pay.hidden = (which !== 'pay'); }
        if (acct) { acct.hidden = (which !== 'account'); }
        if (sum) { sum.hidden = (which === 'account'); }
        var freeSub = 'Free plan \u00b7 no credit card needed';
        if (which === 'register') { setHead('Create your free account', isFree(state.sel) ? freeSub : 'Then continue to ' + summaryText()); }
        else if (which === 'login') { setHead('Log in to continue', isFree(state.sel) ? freeSub : 'Then continue to ' + summaryText()); }
    }
    function progress(text, kind) {
        openStep('progress');
        showPanel('progress');
        var s = step(), p = s && $1('.plan-auth__progress-text', s), sp = s && $1('.plan-auth__spinner', s);
        if (p) { p.textContent = text; p.className = 'plan-auth__progress-text' + (kind ? ' is-' + kind : ''); }
        if (sp) { sp.hidden = (kind === 'error' || kind === 'done'); }
        var retry = s && $1('.plan-auth__retry', s);
        if (retry) { retry.hidden = (kind !== 'error'); }
    }
    function freeVariant(on) {
        var m = modal(), s = step();
        if (m) { m.classList.toggle('is-free-signup', !!on); }
        if (!s) { return; }
        [['register', 'Create free account'], ['login', 'Log in']].forEach(function (x) {
            var b = $1('.plan-auth__form--' + x[0] + ' button[type="submit"]', s);
            if (!b) { return; }
            if (!b.getAttribute('data-orig-label')) { b.setAttribute('data-orig-label', b.textContent); }
            var label = on ? x[1] : b.getAttribute('data-orig-label');
            b.textContent = label;
            b.setAttribute('data-label', label);
        });
    }
    function clearInvalid(rootEl) { if (rootEl) { rootEl.querySelectorAll('[aria-invalid]').forEach(function (el) { el.removeAttribute('aria-invalid'); }); } }
    function markInvalid(form, name) {
        if (!form) { return; }
        clearInvalid(form);
        var el = name && $1('[name="' + name + '"]', form);
        if (el) { el.setAttribute('aria-invalid', 'true'); }
    }
    function setBusy(form, busy, label) {
        var b = form && $1('button[type="submit"]', form);
        if (!b) { return; }
        if (!b.getAttribute('data-label')) { b.setAttribute('data-label', b.textContent); }
        b.disabled = !!busy;
        b.textContent = busy ? (label || 'Please wait\u2026') : b.getAttribute('data-label');
        if (busy) { b.setAttribute('aria-busy', 'true'); } else { b.removeAttribute('aria-busy'); }
    }
    function reset() {
        var m = modal(), s = step();
        freeVariant(false);
        if (m) { m.classList.remove('is-auth-step'); }
        if (s) {
            s.hidden = true;
            s.querySelectorAll('.plan-auth__msg').forEach(function (el) { msg(el, ''); });
            clearInvalid(s);
            s.querySelectorAll('button[type="submit"]').forEach(function (b) { b.disabled = false; b.removeAttribute('aria-busy'); if (b.getAttribute('data-label')) { b.textContent = b.getAttribute('data-label'); } });
            var pw = $1('[name="password"]', s); if (pw) { pw.value = ''; }
        }
        peTeardown();
        if (original && m) {
            var t = $1('h2.section__title', m), sb = $1('.plan-modal-subtitle', m);
            if (t) { t.textContent = original.title; }
            if (sb) { sb.textContent = original.sub; }
        }
        markCurrent();
        syncFreeCtas();
    }

    /* ---------- plan cards: billing toggle, CURRENT label, Free CTA ---------- */
    function cycle() { var m = modal(); return m && $1('.plan-billing-toggle .yearly.current', m) ? 'annual' : 'monthly'; }
    function applyPrices() {
        var m = modal();
        if (!m) { return; }
        var c = cycle(), unit = c === 'annual' ? '/yr' : '/mo', pr = PRICES[c];
        [['free-price', pr.free], ['pro-price', pr.pro], ['bus-price', pr.bus]].forEach(function (x) {
            m.querySelectorAll('.' + x[0]).forEach(function (el) {
                var inCard = !!el.closest('.plan-card__price');
                el.innerHTML = '$<strong>' + x[1] + '</strong>' + (inCard ? '<span class="plan-card__unit">' + unit + '</span>' : unit);
            });
        });
    }
    function setCycle(c) {
        var m = modal();
        if (!m) { return; }
        m.querySelectorAll('.monthly-yearly .monthly').forEach(function (el) { el.classList.toggle('current', c === 'monthly'); });
        m.querySelectorAll('.monthly-yearly .yearly').forEach(function (el) { el.classList.toggle('current', c === 'annual'); });
        applyPrices();
    }
    function markCurrent() {
        var m = modal();
        if (!m) { return; }
        var lvl = token() ? planLevel() : -1;
        var map = { 0: '.free-plan-up', 1: '.pro-plan-up', 2: '.bus-plan-up' };
        m.querySelectorAll('.free-plan-up, .pro-plan-up, .bus-plan-up').forEach(function (a) {
            if (!a.hasAttribute('data-cta-label')) { a.setAttribute('data-cta-label', a.textContent); }
            var cur = lvl >= 0 && a.matches(map[lvl]);
            a.classList.toggle('active-plan', cur);
            if (cur) { a.setAttribute('aria-current', 'true'); } else { a.removeAttribute('aria-current'); }
            a.textContent = cur ? 'CURRENT' : a.getAttribute('data-cta-label');
        });
    }
    function syncFreeCtas() {
        var guest = !token();
        d.querySelectorAll('#dmcaAcct .plan-card__cta--free').forEach(function (a) {
            a.classList.toggle('is-free-signup', guest);
            if (guest) { a.removeAttribute('aria-disabled'); a.setAttribute('tabindex', '0'); }
            else { a.setAttribute('aria-disabled', 'true'); a.removeAttribute('tabindex'); }
        });
    }

    /* ---------- open / close ---------- */
    var lockY = 0;
    function open(view) {
        mount();
        var m = modal(), v = veil();
        if (!visible()) {
            reset();
            lockY = w.pageYOffset || 0;
            d.documentElement.classList.add('dmca-acct-open');
            v.style.display = 'block';
            m.style.display = 'block';
            m.scrollTop = 0;
        }
        if (token() && view !== 'plans') { showAccount(); return; }
        if (view === 'login' || view === 'register') {
            state.sel = { plan: 'free', cycle: 'monthly', method: 'credit' };
            showAuth(state.sel, view);
            return;
        }
        reset();
    }
    function close() {
        var m = modal(), v = veil();
        if (!m) { return; }
        m.style.display = 'none';
        if (v) { v.style.display = 'none'; }
        d.documentElement.classList.remove('dmca-acct-open');
        if (Math.abs((w.pageYOffset || 0) - lockY) > 1) { w.scrollTo(0, lockY); }
        setTimeout(reset, 250);
    }

    /* ---------- auth ---------- */
    function switchTab(which, email) {
        var s = step();
        if (!s) { return; }
        showPanel(which);
        var f = $1('.plan-auth__form--' + which, s);
        if (email && f) { var e = $1('[name="email"]', f); if (e) { e.value = email; } }
        setTimeout(function () {
            var focus = f && (email ? $1(which === 'login' ? '[name="password"]' : '[name="firstName"]', f) : $1('[name="email"]', f));
            if (focus && w.innerWidth > 520) { try { focus.focus(); } catch (e) { } }
        }, 30);
    }
    function showAuth(sel, tab, note) {
        state.sel = sel;
        if (!openStep('auth')) { return; }
        var sum = $1('.plan-auth__summary', step());
        freeVariant(isFree(sel));
        if (sum) { sum.innerHTML = isFree(sel) ? '<strong>Free</strong> \u00b7 $0' : '<strong>' + esc(PLANS[sel.plan].name) + '</strong> \u00b7 ' + esc(priceText(sel)) + ' \u00b7 ' + esc(METHOD_LABEL.credit); }
        switchTab(tab || 'register');
        if (note) { msg($1('.plan-auth__form--' + (tab || 'register') + ' .plan-auth__msg', step()), note, 'info'); }
    }
    function register(form) {
        var email = $1('[name="email"]', form).value.trim();
        var first = $1('[name="firstName"]', form).value.trim();
        var last = $1('[name="lastName"]', form).value.trim();
        var terms = $1('[name="terms"]', form);
        var out = $1('.plan-auth__msg', form);
        if (!validEmail(email)) { markInvalid(form, 'email'); msg(out, 'Please enter a valid email address.', 'error'); return; }
        if (!first || !last) { markInvalid(form, !first ? 'firstName' : 'lastName'); msg(out, 'Please enter your first and last name.', 'error'); return; }
        if (terms && !terms.checked) { markInvalid(form, 'terms'); msg(out, 'Please agree to the Terms & Privacy to continue.', 'error'); return; }
        markInvalid(form, null);
        msg(out, '');
        setBusy(form, true, 'Creating account\u2026');
        fetch(API + '/register', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({
                FirstName: first, LastName: last, CompanyName: first + ' ' + last, Email: email,
                mpi: 'Protection Program',
                LSD: 'Languages: ' + (navigator.language || '') + ' | source: mcp.dmca.com account modal'
            })
        }).then(readJson).then(function (r) {
            setBusy(form, false);
            var v = typeof r.data === 'string' ? r.data.trim() : '';
            if (r.ok && GUID_RE.test(v)) {
                ssSet(EMAIL_KEY, email);
                $1('[name="firstName"]', form).value = ''; $1('[name="lastName"]', form).value = '';
                switchTab('login', email);
                msg($1('.plan-auth__form--login .plan-auth__msg', step()), 'Account created \u2014 your login details are on the way to ' + email + '. Enter the password from that email to log in here.', 'info');
                return;
            }
            if (/fail/i.test(v + r.text)) {
                // api.dmca.com/register answers "Failed" for an email that already has an account.
                switchTab('login', email);
                msg($1('.plan-auth__form--login .plan-auth__msg', step()), 'You may already have an account with ' + email + '. Log in to continue.', 'info');
                return;
            }
            msg(out, 'We couldn\u2019t create your account right now. Please try again in a minute or contact support.', 'error');
        }).catch(function () {
            setBusy(form, false);
            msg(out, 'Network error \u2014 please try again.', 'error');
        });
    }
    function login(form) {
        var email = $1('[name="email"]', form).value.trim();
        var pass = $1('[name="password"]', form).value;
        var out = $1('.plan-auth__msg', form);
        if (!validEmail(email)) { markInvalid(form, 'email'); msg(out, 'Please check your email address.', 'error'); return; }
        if (!pass) { markInvalid(form, 'password'); msg(out, 'Please enter your password.', 'error'); return; }
        markInvalid(form, null);
        msg(out, '');
        setBusy(form, true, 'Logging in\u2026');
        var fail = function (text) { setBusy(form, false); msg(out, text || 'Incorrect email or password. Please try again.', 'error'); };
        fetch(API + '/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({ email: email, password: pass })
        }).then(readJson).then(function (r) {
            var tok = typeof r.data === 'string' ? r.data.trim() : '';
            if (r.status === 429) { fail('Too many attempts \u2014 please wait a minute and try again.'); return; }
            if (r.status >= 500) { fail('DMCA.com login is having trouble right now. Please try again in a moment.'); return; }
            if (!r.ok || !tok || tok.indexOf('Failed') > -1 || tok.length > 400) { fail(); return; }
            $1('[name="password"]', form).value = '';
            setBusy(form, false);
            ssSet(TOKEN_KEY, tok);
            ssSet(EMAIL_KEY, email);
            syncPill();
            fetchPlan(tok).then(function (lvl) {
                var sel = state.sel;
                if (sel && !isFree(sel)) {
                    if (lvl === 1 || lvl === 2) { alreadyPaid(lvl); return; }
                    progress('Logged in. Continuing to checkout\u2026');
                    proceed(sel);
                    return;
                }
                showAccount();
            });
        }).catch(function () { fail('Network error \u2014 please try again.'); });
    }
    // api.dmca.com/getProStatus -> 0 free, 1 pro, 2 business (same mapping as /add/login).
    function fetchPlan(tok) {
        return fetch(API + '/getProStatus', { method: 'GET', headers: { 'token': tok } })
            .then(readJson)
            .then(function (r) {
                var s = r.data && r.data[0] ? r.data[0] : null;
                if (!r.ok || !s) { return planLevel(); }
                var pro = !!s.PRO_STATUS, bus = !!s.BUSINESS_STATUS;
                var lvl = (pro && bus) ? 2 : (pro ? 1 : 0);
                ssSet(PLAN_KEY, lvl);
                return lvl;
            })
            .catch(function () { return planLevel(); });
    }
    function logout() {
        ssDel(TOKEN_KEY); ssDel(PLAN_KEY);
        syncPill();
        state.sel = { plan: 'free', cycle: 'monthly', method: 'credit' };
        reset();
        showAuth(state.sel, 'login');
    }
    function alreadyPaid(lvl) {
        var name = lvl === 2 ? 'Business' : 'Pro';
        progress('You\u2019re already on the ' + name + ' plan \u2014 no need to subscribe again.', 'done');
        setHead('You\u2019re already on ' + name, 'Your subscription is active.');
        setTimeout(function () { if (visible()) { showAccount(); } }, 2500);
    }

    /* ---------- logged-in panel: token + MCP config ---------- */
    function mask(t) { return t.length > 12 ? t.slice(0, 4) + '\u2022'.repeat(12) + t.slice(-4) : '\u2022'.repeat(t.length); }
    function configText(t) {
        return JSON.stringify({ mcpServers: { 'dmca-cases': { url: MCP_URL, headers: { Authorization: 'Bearer ' + t } } } }, null, 2);
    }
    function paintAccount() {
        var s = step(), a = s && $1('.plan-acct', s);
        if (!a) { return; }
        var t = token(), shown = a.getAttribute('data-reveal') === '1';
        $1('.plan-acct__email', a).textContent = ssGet(EMAIL_KEY) || 'your DMCA.com account';
        var lvl = planLevel();
        $1('.plan-acct__plan', a).textContent = lvl >= 0 ? planName(lvl) : '\u2014';
        $1('.plan-acct__token', a).textContent = shown ? t : mask(t);
        $1('.plan-acct__reveal', a).textContent = shown ? 'Hide' : 'Reveal';
        $1('.plan-acct__reveal', a).setAttribute('aria-pressed', shown ? 'true' : 'false');
        $1('.plan-acct__config', a).textContent = configText(shown ? t : mask(t));
        var up = $1('.plan-acct__upgrade', a);
        if (up) { up.textContent = lvl === 2 ? 'View plans' : 'Upgrade plan'; }
    }
    function showAccount() {
        if (!token()) { showAuth({ plan: 'free', cycle: 'monthly', method: 'credit' }, 'login'); return; }
        openStep('account');
        freeVariant(false);
        showPanel('account');
        var a = $1('.plan-acct', step());
        if (a) { a.setAttribute('data-reveal', '0'); }
        paintAccount();
        var lvl = planLevel();
        setHead('You\u2019re logged in', (lvl >= 0 ? planName(lvl) + ' plan \u00b7 ' : '') + 'Use your token with the DMCA Cases MCP server');
        if (lvl < 0) { fetchPlan(token()).then(function () { if (visible()) { paintAccount(); var l2 = planLevel(); if (l2 >= 0) { setHead('You\u2019re logged in', planName(l2) + ' plan \u00b7 Use your token with the DMCA Cases MCP server'); } } }); }
    }
    function copyText(text, btn) {
        var done = function (ok) {
            if (!btn) { return; }
            if (!btn.getAttribute('data-label')) { btn.setAttribute('data-label', btn.textContent); }
            btn.textContent = ok ? 'Copied' : 'Copy failed';
            setTimeout(function () { btn.textContent = btn.getAttribute('data-label'); }, 1600);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function () { done(true); }, function () { done(fallbackCopy(text)); });
        } else { done(fallbackCopy(text)); }
    }
    function fallbackCopy(text) {
        var ta = d.createElement('textarea');
        ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.opacity = '0';
        d.body.appendChild(ta); ta.select();
        var ok = false; try { ok = d.execCommand('copy'); } catch (e) { }
        d.body.removeChild(ta);
        return ok;
    }

    /* ---------- checkout: in-modal Stripe Payment Element (port of status-upgrade.js) ---------- */
    var pe = { cfg: null, stripeP: null, stripe: null, elements: null, element: null, sel: null, sub: null, busy: false };
    function peKey(sel) { return sel.plan + ':' + sel.cycle; }
    function peApi(action, body) {
        return fetch(PE_API + '?action=' + action, {
            method: body ? 'POST' : 'GET',
            headers: body ? { 'Content-Type': 'application/json', 'Token': token() } : {},
            body: body ? JSON.stringify(body) : undefined
        }).then(readJson);
    }
    function peConfig() {
        if (pe.cfg) { return Promise.resolve(pe.cfg); }
        return peApi('config').then(function (r) {
            if (r.ok && r.data && r.data.enabled && /^pk_(test|live)_/.test(r.data.publishableKey || '')) { pe.cfg = r.data; return pe.cfg; }
            return null;
        }).catch(function () { return null; });
    }
    function peStripe(pk) {
        if (pe.stripe) { return Promise.resolve(pe.stripe); }
        if (!pe.stripeP) {
            pe.stripeP = new Promise(function (resolve, reject) {
                if (w.Stripe) { resolve(); return; }
                var sc = d.createElement('script');
                sc.src = STRIPE_JS; sc.async = true;
                sc.onload = function () { resolve(); };
                sc.onerror = function () { pe.stripeP = null; reject(new Error('stripe_js_blocked')); };
                d.head.appendChild(sc);
            });
        }
        return pe.stripeP.then(function () { pe.stripe = w.Stripe(pk); return pe.stripe; });
    }
    function pePanel() {
        var s = step();
        if (!s) { return null; }
        var p = $1('.plan-pay', s);
        if (p) { return p; }
        p = d.createElement('div');
        p.className = 'plan-pay';
        p.hidden = true;
        p.innerHTML = '<div class="plan-pay__element" aria-label="Payment details"></div>' +
            '<p class="plan-auth__msg" role="alert" hidden></p>' +
            '<button type="button" class="btn plan-auth__submit plan-pay__submit"></button>' +
            '<p class="plan-auth__fine plan-pay__fine"></p>';
        var prog = $1('.plan-auth__progress', s);
        s.insertBefore(p, prog || null);
        $1('.plan-pay__submit', p).addEventListener('click', function (e) { e.preventDefault(); pePay(); });
        return p;
    }
    function peMsg(text, kind) { var p = pePanel(); msg(p && $1('.plan-auth__msg', p), text, kind); }
    function peBusy(busy, label) {
        pe.busy = !!busy;
        var b = $1('.plan-pay__submit', pePanel());
        if (!b) { return; }
        b.disabled = !!busy;
        b.textContent = busy ? (label || 'Processing\u2026') : b.getAttribute('data-label');
        if (busy) { b.setAttribute('aria-busy', 'true'); } else { b.removeAttribute('aria-busy'); }
    }
    function peTeardown() {
        try { if (pe.element) { pe.element.destroy(); } } catch (e) { }
        pe.element = null; pe.elements = null; pe.busy = false;
        var p = step() && $1('.plan-pay', step());
        if (p) { p.hidden = true; msg($1('.plan-auth__msg', p), ''); }
    }
    function peAppearance() {
        return {
            theme: 'stripe',
            variables: {
                colorPrimary: '#6BC530', colorText: '#1f1f1f', colorDanger: '#a4160f', colorBackground: '#ffffff',
                fontFamily: '"Google Sans", Roboto, "Segoe UI", "Helvetica Neue", Arial, sans-serif', fontSizeBase: '16px', borderRadius: '8px', spacingUnit: '4px'
            },
            rules: {
                '.Input': { border: '1px solid #bdbdbd', boxShadow: 'none', padding: '11px 12px' },
                '.Input:focus': { borderColor: '#6BC530', boxShadow: '0 0 0 3px rgba(107, 197, 48, .25)' },
                '.Label': { fontSize: '13px', fontWeight: '700', color: '#1f1f1f' },
                '.Tab': { border: '1px solid #bdbdbd', boxShadow: 'none' },
                '.Tab--selected': { borderColor: '#6BC530', boxShadow: '0 0 0 1px #6BC530' }
            }
        };
    }
    function proceed(sel) {
        if (!sel || isFree(sel)) { showAccount(); return; }
        if (!token()) { showAuth(sel); return; }
        state.sel = sel;
        var sum = $1('.plan-auth__summary', step());
        if (sum) { sum.innerHTML = '<strong>' + esc(PLANS[sel.plan].name) + '</strong> \u00b7 ' + esc(priceText(sel)) + ' \u00b7 ' + esc(METHOD_LABEL.credit); }
        progress('Loading secure payment form for ' + PLANS[sel.plan].name + ' (' + priceText(sel) + ')\u2026');
        peConfig().then(function (cfg) {
            // No hosted-Checkout fallback here: it would leave mcp.dmca.com.
            if (!cfg) { progress(CHECKOUT_OFF_MSG, 'error'); setHead('Upgrade to ' + PLANS[sel.plan].name, PLANS[sel.plan].name + ' plan \u00b7 ' + priceText(sel)); return; }
            return peStripe(cfg.publishableKey).then(function (stripe) { peMount(stripe, sel); },
                function () { progress('The secure payment form couldn\u2019t load (Stripe was blocked). Please allow js.stripe.com and try again.', 'error'); });
        });
    }
    function peMount(stripe, sel) {
        var c = PLANS[sel.plan][sel.cycle];
        if (pe.sel && peKey(pe.sel) !== peKey(sel)) { pe.sub = null; }
        peTeardown();
        pe.sel = sel;
        var p = pePanel();
        if (!p || !openStep('pay')) { return; }
        var sum = $1('.plan-auth__summary', step());
        if (sum) { sum.innerHTML = '<strong>' + esc(PLANS[sel.plan].name) + '</strong> \u00b7 ' + esc(priceText(sel)) + ' \u00b7 ' + (sel.cycle === 'annual' ? 'billed yearly' : 'billed monthly'); }
        var btn = $1('.plan-pay__submit', p), label = 'Pay $' + c.value + ' & upgrade';
        btn.setAttribute('data-label', label); btn.textContent = label; btn.disabled = true;
        $1('.plan-pay__fine', p).textContent = '\uD83D\uDD12 Secure payment by Stripe. Renews at $' + c.value + (sel.cycle === 'annual' ? '/year' : '/month') + ' until you cancel \u2014 cancel anytime from your dashboard.';
        showPanel('pay');
        setHead('Complete your upgrade', PLANS[sel.plan].name + ' plan \u00b7 ' + priceText(sel));
        pe.elements = stripe.elements({
            mode: 'subscription', amount: Math.round(c.value * 100), currency: 'usd',
            paymentMethodTypes: ['card'],
            appearance: peAppearance(), loader: 'auto',
            fonts: [{ cssSrc: 'https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700&display=swap' }]
        });
        pe.element = pe.elements.create('payment', { layout: { type: 'tabs', defaultCollapsed: false }, business: { name: 'DMCA.com' }, wallets: { applePay: 'auto', googlePay: 'auto' } });
        pe.element.on('ready', function () { btn.disabled = false; });
        pe.element.on('change', function () { peMsg(''); });
        pe.element.on('loaderror', function () { peTeardown(); progress('The secure payment form couldn\u2019t load. Please try again in a moment.', 'error'); });
        pe.element.mount($1('.plan-pay__element', p));
    }
    function peFail(err, fallbackText) {
        peBusy(false);
        if (err && (err.type === 'card_error' || err.type === 'validation_error')) { return; } // shown by the Payment Element
        peMsg(fallbackText || 'Your payment didn\u2019t go through. Please try again.', 'error');
    }
    function pePay() {
        var sel = pe.sel, stripe = pe.stripe, elements = pe.elements;
        if (!sel || !stripe || !elements || pe.busy) { return; }
        peMsg('');
        peBusy(true, 'Processing\u2026');
        elements.submit().then(function (r) {
            if (r && r.error) { peBusy(false); peMsg(r.error.message, 'error'); return null; }
            if (pe.sub && pe.sub.key === peKey(sel)) { return pe.sub; }
            return peApi('create', { plan: sel.plan, cycle: sel.cycle, source: SOURCE }).then(function (res) {
                var dt = res.data || {};
                if (res.ok && dt.clientSecret && dt.subscriptionId) {
                    pe.sub = { key: peKey(sel), id: dt.subscriptionId, secret: dt.clientSecret };
                    return pe.sub;
                }
                if (res.status === 401) {
                    ssDel(TOKEN_KEY); syncPill(); peTeardown();
                    showAuth(sel, 'login', 'Your session has expired. Please log in again.');
                    return null;
                }
                if (res.status === 409 && dt.error === 'already_subscribed') {
                    peTeardown();
                    fetchPlan(token()).then(function (l) { alreadyPaid(l === 2 || dt.plan === 'business' ? 2 : 1); });
                    return null;
                }
                peFail({ code: dt.error || ('http_' + res.status) }, 'We couldn\u2019t start your payment. Please try again in a moment.');
                return null;
            });
        }).then(function (sub) {
            if (!sub) { return; }
            return stripe.confirmPayment({
                elements: elements,
                clientSecret: sub.secret,
                confirmParams: { return_url: 'https://mcp.dmca.com/?upgrade=success&sub=' + encodeURIComponent(sub.id) },
                redirect: 'if_required'
            }).then(function (r) {
                if (r.error) { peFail(r.error); return; }
                var st = r.paymentIntent && r.paymentIntent.status;
                if (st === 'succeeded' || st === 'processing') { peVerify(sel, sub.id); return; }
                peFail({ code: 'payment_' + (st || 'unknown') });
            });
        }).catch(function () {
            peFail({ code: 'network' }, 'We couldn\u2019t reach the payment service. Please check your connection and try again.');
        });
    }
    // Server check (Stripe invoice paid + subscription active) before showing success - never the client's word alone.
    function peVerify(sel, subId) {
        progress('Payment received \u2014 activating your plan\u2026');
        var delays = [0, 1500, 3000, 5000], i = 0;
        (function attempt() {
            setTimeout(function () {
                peApi('confirm', { subscriptionId: subId }).then(function (r) {
                    var dt = r.data || {};
                    if (r.ok && dt.paid) { peDone(dt); return; }
                    if (r.ok && (dt.paymentStatus === 'processing' || dt.paymentStatus === 'succeeded') && ++i < delays.length) { attempt(); return; }
                    if (!r.ok && r.status >= 500 && ++i < delays.length) { attempt(); return; }
                    progress(r.ok && dt.paymentStatus === 'processing'
                        ? 'Your payment is still processing. Your plan will switch on as soon as it clears \u2014 we\u2019ll email you.'
                        : 'We couldn\u2019t confirm your payment yet. If you were charged, your plan will update within a few minutes; otherwise please try again.', 'error');
                }).catch(function () {
                    if (++i < delays.length) { attempt(); return; }
                    progress('We couldn\u2019t confirm your payment right now. If you were charged, your plan will update within a few minutes.', 'error');
                });
            }, delays[i]);
        })();
    }
    function peDone(dt) {
        var plan = PLANS[dt.plan] ? dt.plan : (pe.sel ? pe.sel.plan : 'pro');
        var level = dt.planLevel || (plan === 'business' ? 2 : 1);
        ssSet(PLAN_KEY, level);
        peTeardown();
        pe.sub = null;
        var name = PLANS[plan].name;
        progress('You\u2019re upgraded to ' + name + '! ' + (dt.activated ? 'Your plan is active now.' : 'Your plan will be active within a minute.') + ' A receipt is on its way to your email.', 'done');
        setHead('Welcome to ' + name, 'Payment confirmed');
        state.sel = null;
        setTimeout(function () { if (visible()) { showAccount(); } }, 3500);
    }

    /* ---------- wiring ---------- */
    function onClick(e) {
        var t = e.target;
        if (!t || !t.closest) { return; }
        var m = modal();
        if (t.closest('#dmcaAcct .redirectModalVeil') || t.closest('#dmcaAcct .proComparisonModal > .close-modal')) { e.preventDefault(); close(); return; }
        if (!m || !m.contains(t)) { return; }
        var x;
        if ((x = t.closest('.monthly-yearly .monthly, .monthly-yearly .yearly'))) { e.preventDefault(); setCycle(x.classList.contains('yearly') ? 'annual' : 'monthly'); return; }
        if ((x = t.closest('.plan-compare-more'))) {
            e.preventDefault(); m.classList.add('is-comparing'); x.setAttribute('aria-expanded', 'true');
            setTimeout(function () {
                var inner = $1('.innerModalCont', m), tg = $1('.plan-full-features', m);
                if (inner && tg && tg.getBoundingClientRect().height) { try { inner.scrollTo({ top: Math.max(0, inner.scrollTop + tg.getBoundingClientRect().top - inner.getBoundingClientRect().top - 12), behavior: 'smooth' }); } catch (er) { } }
            }, 60);
            return;
        }
        if ((x = t.closest('.plan-compare-less'))) { e.preventDefault(); m.classList.remove('is-comparing'); var mb = $1('.plan-compare-more', m); if (mb) { mb.setAttribute('aria-expanded', 'false'); } return; }
        if ((x = t.closest('.plan-pay-toggle__opt'))) { e.preventDefault(); return; } // Credit Card only on this page
        if ((x = t.closest('.free-plan-up'))) {
            e.preventDefault();
            if (!token()) { state.sel = { plan: 'free', cycle: 'monthly', method: 'credit' }; showAuth(state.sel, 'register'); }
            return;
        }
        if ((x = t.closest('a.subscribe-btn'))) {
            e.preventDefault();
            var plan = x.classList.contains('bus-plan-up') || x.classList.contains('bus-btn') ? 'business' : 'pro';
            var sel = { plan: plan, cycle: cycle(), method: 'credit' };
            state.sel = sel;
            if (!token()) { showAuth(sel, 'register'); return; }
            var lvl = planLevel();
            if (lvl === 2 || (lvl === 1 && plan === 'pro')) { alreadyPaid(lvl); return; }
            proceed(sel);
            return;
        }
        if ((x = t.closest('.plan-auth__tab'))) { e.preventDefault(); switchTab(x.getAttribute('data-tab')); return; }
        if (t.closest('.plan-auth__back')) { e.preventDefault(); reset(); return; }
        if (t.closest('.plan-auth__retry')) { e.preventDefault(); if (state.sel && !isFree(state.sel)) { pe.cfg = null; proceed(state.sel); } else { showAccount(); } return; }
        if (t.closest('.plan-acct__reveal')) { e.preventDefault(); var a = $1('.plan-acct', step()); a.setAttribute('data-reveal', a.getAttribute('data-reveal') === '1' ? '0' : '1'); paintAccount(); return; }
        if ((x = t.closest('.plan-acct__copy-token'))) { e.preventDefault(); copyText(token(), x); return; }
        if ((x = t.closest('.plan-acct__copy-config'))) { e.preventDefault(); copyText(configText(token()), x); return; }
        if (t.closest('.plan-acct__upgrade')) { e.preventDefault(); reset(); return; }
        if (t.closest('.plan-acct__logout')) { e.preventDefault(); logout(); return; }
    }
    function wire() {
        d.addEventListener('click', onClick);
        var s = step();
        var reg = $1('.plan-auth__form--register', s), log = $1('.plan-auth__form--login', s);
        reg.addEventListener('submit', function (e) { e.preventDefault(); register(reg); });
        log.addEventListener('submit', function (e) { e.preventDefault(); login(log); });
        var unmark = function (e) { var t = e.target; if (t && t.getAttribute && t.getAttribute('aria-invalid')) { t.removeAttribute('aria-invalid'); } };
        s.addEventListener('input', unmark);
        s.addEventListener('change', unmark);
        d.addEventListener('keydown', function (e) {
            if (visible() && (e.key === 'Escape' || e.key === 'Esc')) { close(); return; }
            var f = e.target && e.target.closest && e.target.closest('#dmcaAcct .plan-card__cta--free.is-free-signup');
            if (f && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); f.click(); }
        });
        // iOS Safari: bind the X and veil directly too (taps on non-clickable elements don't reach document listeners).
        [$1('#dmcaAcct .proComparisonModal > .close-modal'), veil()].forEach(function (el) {
            if (!el) { return; }
            el.style.cursor = 'pointer';
            el.addEventListener('click', function (e) { e.stopPropagation(); close(); });
        });
        var m = modal();
        m.addEventListener('scroll', function () { if (m.scrollTop && w.innerWidth <= 520) { m.scrollTop = 0; } });
    }
    function mount() {
        if (root()) { return; }
        var r = d.createElement('div');
        r.id = 'dmcaAcct';
        r.innerHTML = HTML;
        d.body.appendChild(r);
        wire();
        applyPrices();
    }

    w.DMCAAccount = { open: open, close: close, syncPill: syncPill, version: '__VERSION__' };
    syncPill();
})(window, document);
