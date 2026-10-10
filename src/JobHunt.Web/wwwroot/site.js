document.querySelector('[data-select-jobs]')?.addEventListener('click', () => {
    const boxes = [...document.querySelectorAll('input[name="Selected"]')];
    const mark = boxes.some(box => !box.checked);
    boxes.forEach(box => { box.checked = mark; });
});
if (document.querySelector('[data-refresh="true"]')) {
    window.setTimeout(() => window.location.reload(), 4000);
}
document.querySelectorAll('nav a').forEach(link => {
    if (new URL(link.href).pathname === window.location.pathname) link.setAttribute('aria-current', 'page');
});
