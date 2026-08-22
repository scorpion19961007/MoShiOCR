const root = document.documentElement;
const themeToggle = document.querySelector('#themeToggle');
const menuToggle = document.querySelector('#menuToggle');
const nav = document.querySelector('.desktop-nav');
const toast = document.querySelector('#toast');

function setTheme(theme) {
  root.dataset.theme = theme;
  document.body.classList.toggle('dark-mode', theme === 'dark');
  themeToggle.setAttribute('aria-label', theme === 'dark' ? '切换浅色模式' : '切换夜间模式');
  themeToggle.innerHTML = `<i data-lucide="${theme === 'dark' ? 'sun' : 'moon'}"></i>`;
  window.lucide?.createIcons();
  localStorage.setItem('moshi-theme', theme);
}

setTheme(localStorage.getItem('moshi-theme') || 'light');
themeToggle.addEventListener('click', () => setTheme(root.dataset.theme === 'dark' ? 'light' : 'dark'));

menuToggle.addEventListener('click', () => {
  const isOpen = nav.classList.toggle('open');
  menuToggle.setAttribute('aria-expanded', String(isOpen));
  menuToggle.innerHTML = `<i data-lucide="${isOpen ? 'x' : 'menu'}"></i>`;
  window.lucide?.createIcons();
});

document.querySelectorAll('.nav-link').forEach(link => link.addEventListener('click', () => {
  nav.classList.remove('open');
  menuToggle.setAttribute('aria-expanded', 'false');
  menuToggle.innerHTML = '<i data-lucide="menu"></i>';
  window.lucide?.createIcons();
}));

function showToast(message) {
  toast.querySelector('span').textContent = message;
  toast.classList.add('show');
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => toast.classList.remove('show'), 2200);
}

document.querySelector('#copyPreview').addEventListener('click', async () => {
  const text = '让信息流动起来，而不是停在屏幕里。';
  try {
    await navigator.clipboard.writeText(text);
    showToast('示例结果已复制');
  } catch {
    showToast('示例结果：' + text);
  }
});

const sections = [...document.querySelectorAll('main section[id]')];
const links = [...document.querySelectorAll('.nav-link')];
const observer = new IntersectionObserver(entries => entries.forEach(entry => {
  if (!entry.isIntersecting) return;
  links.forEach(link => link.classList.toggle('is-active', link.getAttribute('href') === `#${entry.target.id}`));
}), { rootMargin: '-25% 0px -65% 0px', threshold: 0 });
sections.forEach(section => observer.observe(section));

window.lucide?.createIcons();
